using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticInventoryObservationTests
{
    private static readonly Lazy<DataCatalog> Bundled = new(() => { var c = new DataCatalog(); c.Load(mapVersion: "2.320"); return c; });
    private static DataCatalog Catalog => Bundled.Value;
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Context = new('A', 64);
    private static InventoryEntry Entry(int count = 1) => new() { UnitId = "rawcode:I10h", Count = count };
    private static DiagnosticInventoryObservation Make(IEnumerable<InventoryEntry>? entries = null,
        IEnumerable<string>? growth = null, double duration = 0.2, string? version = null, string? hash = null,
        string? context = null, long revision = 2, int slot = 0) =>
        DiagnosticInventoryObservation.Create(Catalog, version ?? Warcraft300Diagnostic.Version,
            hash ?? Warcraft300Diagnostic.Hash, context ?? Context, revision, slot,
            Now.AddSeconds(-duration), Now, TimeSpan.FromSeconds(duration), entries ?? [Entry()], growth ?? ["rawcode:I10h"]);
    private static RecognitionResult Result(DiagnosticInventoryObservation? observation, string? source = null,
        RecognitionState state = RecognitionState.Ready) => new()
    {
        DiagnosticObservation = observation, State = state,
        Diagnostics = new() { Source = source ?? DiagnosticInventoryObservation.SourceName,
            ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash }
    };
    private static bool Accept(RecognitionResult? result, DateTimeOffset? now = null, string? map = null,
        string? fingerprint = null, string? context = null, long previous = 1, int slot = 0) =>
        DiagnosticInventoryConsumerPolicy.TryAccept(result, now ?? Now, map ?? "2.320",
            fingerprint ?? Catalog.OfflineBundle!.Fingerprint, context ?? Context, previous, slot, out _, out _);

    [Fact] public void ValidTypedReferenceNeverGrantsGameplayOrUnknownFacts()
    {
        var observation = Make();
        Assert.True(Accept(Result(observation)));
        Assert.False(observation.CanProveLocalOwnership); Assert.False(observation.CanProveAlive);
        Assert.False(observation.CanProveCompleteness);
        Assert.False(observation.GameplayReady); Assert.False(observation.CanProvideCoachCurrent);
        Assert.False(observation.CanProveRuntimeMapCurrentness);
        Assert.Equal(DiagnosticEvidence.Unknown, observation.LocalOwnership);
        Assert.Equal(DiagnosticEvidence.Unknown, observation.Alive);
        Assert.Equal(DiagnosticEvidence.Unknown, observation.Completeness);
        Assert.Equal(DiagnosticEvidence.Unknown, observation.RuntimeMapProof);
        Assert.Equal(DiagnosticInventoryScope.DiagnosticCurrentView, observation.Scope);
    }
    [Fact] public void ClonesInputsAndEveryMutableExportWithoutExtraGrowthResource()
    {
        var entry = Entry(); var entries = new List<InventoryEntry> { entry };
        var growth = new List<string> { entry.UnitId }; var observation = Make(entries, growth);
        entry.Count = 9; entries.Clear(); growth.Clear();
        var clone = observation.CloneEntries(); clone[0].Count = 20; clone.Clear();
        Assert.Single(observation.Entries); Assert.Single(observation.Counts);
        Assert.Equal(1, observation.Counts["rawcode:I10h"]);
        Assert.Single(observation.GrowthReservedUnitIds);
        Assert.Equal(1, observation.CloneEntries()[0].Count);
    }
    [Fact] public void MissingSpoofedReadySourceAndFailedResultFailClosed()
    {
        Assert.False(Accept(null)); Assert.False(Accept(Result(null)));
        Assert.False(Accept(Result(Make(), source: "WarcraftMemory")));
        Assert.False(Accept(Result(Make(), state: RecognitionState.TransientReadError)));
        Assert.False(Accept(Result(new RecognitionResult().DiagnosticObservation)));
    }
    [Fact] public void CurrentContextRevisionViewAndDatasetFences()
    {
        var result = Result(Make());
        Assert.False(Accept(result, map: "2.319")); Assert.False(Accept(result, fingerprint: new string('0', 64)));
        Assert.False(Accept(result, context: new string('B', 64)));
        Assert.False(Accept(result, previous: 2)); Assert.False(Accept(result, previous: -1));
        Assert.False(Accept(result, slot: 1)); Assert.True(Accept(Result(Make(revision: 3)), previous: 2));
    }
    [Fact] public void AgeUsesScanStartAndCannotBeHiddenByRecentCompletion()
    {
        Assert.True(Accept(Result(Make(duration: 2.9))));
        Assert.False(Accept(Result(Make(duration: 2.9)), now: Now.AddSeconds(0.2)));
        Assert.False(Accept(Result(Make()), now: Now.AddSeconds(-0.1)));
        Assert.False(Accept(Result(Make()), now: Now.AddSeconds(3)));
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, Make(duration: 32).Availability);
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, Make(duration: -1).Availability);
    }
    [Fact] public void UnavailableExportsEmptyInventoryContextAndReservation()
    {
        var failed = DiagnosticInventoryObservation.Unavailable("2.320", Catalog.OfflineBundle!.Fingerprint,
            Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, 3, Now, Now, TimeSpan.Zero, "Read failed");
        Assert.False(Accept(Result(failed))); Assert.Empty(failed.Entries); Assert.Empty(failed.Counts);
        Assert.Empty(failed.GrowthReservedUnitIds); Assert.Empty(failed.ContextId); Assert.Null(failed.ViewSlot);
        Assert.NotEmpty(failed.Reason);
    }
    [Theory]
    [InlineData("3.0.0.24269", "BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12")]
    [InlineData("3.0.0.24268", "0000000000000000000000000000000000000000000000000000000000000000")]
    public void ExactExecutablePinsRequired(string version, string hash) => Assert.False(Accept(Result(Make(version: version, hash: hash))));
    [Fact] public void MalformedEntriesAndReservationsFailClosed()
    {
        Assert.False(Accept(Result(Make(entries: [Entry(), Entry()]))));
        Assert.False(Accept(Result(Make(entries: [Entry(0)]))));
        Assert.False(Accept(Result(Make(entries: [Entry(-1)]))));
        Assert.False(Accept(Result(Make(entries: [Entry(65537)]))));
        Assert.False(Accept(Result(Make(entries: [new() { UnitId = "unknown-unit" }]))));
        Assert.False(Accept(Result(Make(entries: [new() { UnitId = new string('a', 257) }]))));
        Assert.False(Accept(Result(Make(growth: ["not-an-inventory-card"]))));
        Assert.False(Accept(Result(Make(growth: ["rawcode:I10h", "rawcode:I10h"]))));
        Assert.False(Accept(Result(Make(context: "0x1234"))));
        Assert.False(Accept(Result(Make(revision: 0)))); Assert.False(Accept(Result(Make(slot: 24))));
    }
}
