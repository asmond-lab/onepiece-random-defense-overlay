using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticBasicInventoryObservationTests
{
    private static readonly Lazy<DataCatalog> Bundled = new(() => { var c = new DataCatalog(); c.Load(mapVersion: "2.320"); return c; });
    private static DataCatalog Catalog => Bundled.Value;
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Context = new('A', 64);
    private static readonly string World = new('B', 64);
    private static readonly string Binding = new('C', 64);
    private static InventoryEntry Entry(int count = 1) => new() { UnitId = "rawcode:I10h", Count = count };
    private static DiagnosticBasicInventoryObservation Make(IEnumerable<InventoryEntry>? entries = null,
        string? world = null, string? binding = null, string? context = null, long revision = 2, int slot = 0,
        double duration = 0.2, string? version = null, string? hash = null) =>
        DiagnosticBasicInventoryObservation.Create(Catalog, version ?? Warcraft300Diagnostic.Version,
            hash ?? Warcraft300Diagnostic.Hash, context ?? Context, revision, slot,
            Now.AddSeconds(-duration), Now, TimeSpan.FromSeconds(duration), entries ?? [Entry()], world ?? World, binding ?? Binding);
    private static RecognitionDiagnostics Diagnostics(string? source = null, string? version = null, string? hash = null) => new()
    {
        Source = source ?? DiagnosticBasicInventoryObservation.SourceName,
        ProcessVersion = version ?? Warcraft300Diagnostic.Version, ExecutableSha256 = hash ?? Warcraft300Diagnostic.Hash
    };
    private static bool Accept(DiagnosticBasicInventoryObservation? value, RecognitionDiagnostics? diagnostics = null,
        DateTimeOffset? now = null, string? map = null, string? fingerprint = null, string? context = null,
        long previous = 1, int slot = 0) => DiagnosticBasicInventoryConsumerPolicy.TryAccept(value,
            diagnostics ?? Diagnostics(), now ?? Now, map ?? "2.320", fingerprint ?? Catalog.OfflineBundle!.Fingerprint,
            context ?? Context, previous, slot, out _, out _);

    [Fact]
    public void BasicIsDistinctAndHasNoGrowthRoundOrGameplayEvidence()
    {
        IDiagnosticInventoryReference basic = Make();
        Assert.IsType<DiagnosticBasicInventoryObservation>(basic);
        Assert.NotEqual(DiagnosticInventoryObservation.SourceName, basic.Source);
        Assert.True(Accept((DiagnosticBasicInventoryObservation)basic));
        Assert.False(basic.GrowthAttributionAvailable); Assert.Empty(basic.GrowthReservedUnitIds); Assert.Null(basic.ObservedRound);
        Assert.False(basic.GameplayReady); Assert.False(basic.CanProvideCoachCurrent);
        Assert.False(basic.CanProveAlive); Assert.False(basic.CanProveLocalOwnership);
        Assert.False(basic.CanProveCompleteness); Assert.False(basic.CanProveRuntimeMapCurrentness);
        Assert.Equal(DiagnosticEvidence.Unknown, basic.Alive); Assert.Equal(DiagnosticEvidence.Unknown, basic.LocalOwnership);
        Assert.Equal(World, basic.WorldStampFingerprint); Assert.Equal(Binding, basic.BindingContextId);
    }

    [Fact]
    public void EachBasicReadOwnsItsTimestampsAndCannotRefreshAnOlderObservation()
    {
        var older = Make(duration: 2.9);
        var newer = Make(revision: 3);
        Assert.True(Accept(older)); Assert.True(Accept(newer, now: Now.AddSeconds(0.2), previous: 2));
        Assert.False(Accept(older, now: Now.AddSeconds(0.2)));
        Assert.Equal(Now.AddSeconds(-2.9), older.StartedAt);
        Assert.False(DiagnosticBasicInventoryConsumerPolicy.CanPresent(older, Now.AddSeconds(0.1), "2.320",
            Catalog.OfflineBundle!.Fingerprint, Context, 2, 0));
        Assert.True(DiagnosticBasicInventoryConsumerPolicy.CanPresent(newer, Now.AddSeconds(0.2), "2.320",
            Catalog.OfflineBundle!.Fingerprint, Context, 3, 0));
    }

    [Fact]
    public void BasicCannotUseTheEnrichedProducerSourceOrWrongExecutablePins()
    {
        Assert.False(Accept(Make(), Diagnostics(DiagnosticInventoryObservation.SourceName)));
        Assert.False(Accept(Make(), Diagnostics(version: "3.0.0.1")));
        Assert.False(Accept(Make(), Diagnostics(hash: new string('0', 64))));
        Assert.False(Accept(Make(version: "3.0.0.1"))); Assert.False(Accept(Make(hash: new string('0', 64))));
        Assert.False(Accept(null));
        Assert.False(DiagnosticBasicInventoryConsumerPolicy.TryAccept(Make(), null, Now, "2.320",
            Catalog.OfflineBundle!.Fingerprint, Context, 1, 0, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x1234")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void BindingAndWorldFingerprintsAreMandatoryOpaqueHashes(string malformed)
    {
        var invalidWorld = Make(world: malformed); var invalidBinding = Make(binding: malformed);
        Assert.False(Accept(invalidWorld)); Assert.False(Accept(invalidBinding));
        Assert.Empty(invalidWorld.Entries); Assert.Empty(invalidBinding.Entries);
        Assert.Empty(invalidWorld.WorldStampFingerprint); Assert.Empty(invalidBinding.BindingContextId);
    }

    [Fact]
    public void ContextRevisionViewAndDatasetFencesRejectThenAllowANewerValidRead()
    {
        var basic = Make();
        Assert.False(Accept(basic, context: new string('D', 64))); Assert.False(Accept(basic, map: "2.319"));
        Assert.False(Accept(basic, fingerprint: World)); Assert.False(Accept(basic, previous: 2));
        Assert.False(Accept(basic, previous: -1)); Assert.False(Accept(basic, slot: 1));
        Assert.True(Accept(Make(revision: 3), previous: 2));
        Assert.False(DiagnosticBasicInventoryConsumerPolicy.CanPresent(basic, Now, "2.320", Catalog.OfflineBundle!.Fingerprint, Context, 3, 0));
        Assert.False(DiagnosticBasicInventoryConsumerPolicy.CanPresent(basic, Now, "2.320", Catalog.OfflineBundle!.Fingerprint, Context, 2, 1));
    }

    [Fact]
    public void InputsAreCopiedAndExportsCannotMutateACompletedRead()
    {
        var entry = Entry(); var entries = new List<InventoryEntry> { entry }; var basic = Make(entries);
        entry.Count = 99; entries.Clear(); var export = basic.CloneEntries(); export[0].Count = 7; export.Clear();
        Assert.Equal(1, basic.Counts["rawcode:I10h"]); Assert.Single(basic.Entries);
    }

    [Fact]
    public void InvalidEntryQualityAndBoundsFailClosed()
    {
        Assert.False(Accept(Make([Entry(), Entry()]))); Assert.False(Accept(Make([Entry(0)])));
        Assert.False(Accept(Make([Entry(-1)]))); Assert.False(Accept(Make([Entry(65537)])));
        Assert.False(Accept(Make([new() { UnitId = "unknown-unit", Count = 1 }])));
        Assert.False(Accept(Make([null!]))); Assert.False(Accept(Make(context: "invalid")));
        Assert.False(Accept(Make(revision: 0))); Assert.False(Accept(Make(slot: 24)));
        Assert.False(Accept(Make(Enumerable.Repeat(Entry(), 65537))));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(32)]
    [InlineData(-1)]
    public void ExpiredOrInvalidReadNeverPublishesInventory(double duration)
    {
        var basic = Make(duration: duration);
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, basic.Availability);
        Assert.False(Accept(basic)); Assert.Empty(basic.Entries); Assert.Empty(basic.Counts);
        Assert.Null(basic.ViewSlot); Assert.Empty(basic.ContextId);
    }

    [Fact]
    public void FutureCompletionAndExactThreeSecondAgeAreRejected()
    {
        var basic = Make();
        Assert.False(Accept(basic, now: Now.AddMilliseconds(-1)));
        Assert.False(Accept(basic, now: basic.StartedAt.AddSeconds(3)));
        Assert.True(Accept(basic, now: basic.StartedAt.AddSeconds(3).AddTicks(-1)));
    }

    [Fact]
    public void FrameFactoriesKeepBasicAndCompletedChannelsDistinct()
    {
        var basic = Make(); var diagnostics = Diagnostics();
        var frame = DiagnosticRecognitionFrame.ForBasic(basic, diagnostics);
        Assert.Same(basic, frame.Basic); Assert.Null(frame.CompletedResult); Assert.Same(diagnostics, frame.Diagnostics);
        var completed = new RecognitionResult { State = RecognitionState.TransientReadError };
        var completedFrame = DiagnosticRecognitionFrame.ForCompleted(completed);
        Assert.Same(completed, completedFrame.CompletedResult); Assert.Null(completedFrame.Basic);
        Assert.Same(completed.Diagnostics, completedFrame.Diagnostics);
        Assert.Throws<ArgumentNullException>(() => DiagnosticRecognitionFrame.ForBasic(null!, diagnostics));
        Assert.Throws<ArgumentNullException>(() => DiagnosticRecognitionFrame.ForCompleted(null!));
    }

    [Fact]
    public void EnrichedMetadataIsOptionalForOldFixturesButCannotBeHalfBound()
    {
        DiagnosticInventoryObservation Full(string world = "", string binding = "") => DiagnosticInventoryObservation.Create(
            Catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, Context, 2, 0,
            Now.AddMilliseconds(-200), Now, TimeSpan.FromMilliseconds(200), [Entry()], ["rawcode:I10h"],
            observedRound: 4, worldStampFingerprint: world, bindingContextId: binding);
        IDiagnosticInventoryReference legacy = Full();
        Assert.Equal(DiagnosticInventoryAvailability.Ready, legacy.Availability); Assert.True(legacy.GrowthAttributionAvailable);
        Assert.Empty(legacy.WorldStampFingerprint); Assert.Empty(legacy.BindingContextId);
        var bound = Full(World, Binding); Assert.Equal(World, bound.WorldStampFingerprint); Assert.Equal(Binding, bound.BindingContextId);
        Assert.Equal(4, bound.ObservedRound); Assert.Single(bound.GrowthReservedUnitIds);
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, Full(World).Availability);
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, Full(World, "invalid").Availability);
    }
}
