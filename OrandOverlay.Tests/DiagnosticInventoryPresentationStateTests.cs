using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticInventoryPresentationStateTests
{
    private static readonly Lazy<DataCatalog> Data = new(() => { var c = new DataCatalog(); c.Load(mapVersion: "2.320"); return c; });
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Context = new('A', 64);
    private static readonly string Binding = new('B', 64);
    private static readonly string World = new('C', 64);
    private static string Fingerprint => Data.Value.OfflineBundle!.Fingerprint;
    private static RecognitionDiagnostics Diagnostics(string source) => new()
    { Source = source, ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash };
    private static DiagnosticBasicInventoryObservation Basic(long revision, DateTimeOffset now, string? world = null) =>
        DiagnosticBasicInventoryObservation.Create(Data.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            Context, revision, 0, now.AddMilliseconds(-10), now, TimeSpan.FromMilliseconds(10),
            [new() { UnitId = "rawcode:I10h", Count = 2 }], world ?? World, Binding);
    private static RecognitionResult Full(long revision, DateTimeOffset now, string? world = null) => new()
    {
        State = RecognitionState.Ready, Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName),
        DiagnosticObservation = DiagnosticInventoryObservation.Create(Data.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            Context, revision, 0, now.AddMilliseconds(-100), now, TimeSpan.FromMilliseconds(100),
            [new() { UnitId = "rawcode:I10h", Count = 2 }], ["rawcode:I10h"], 1, world ?? World, Binding)
    };
    private static bool Accept(DiagnosticInventoryPresentationState state, DiagnosticBasicInventoryObservation value, DateTimeOffset now) =>
        state.TryAcceptBasic(value, Diagnostics(DiagnosticBasicInventoryObservation.SourceName), now, "2.320", Fingerprint, out _);
    private static bool Accept(DiagnosticInventoryPresentationState state, RecognitionResult value, DateTimeOffset now) =>
        state.TryAcceptFull(value, now, "2.320", Fingerprint, out _);
    private static IDiagnosticInventoryReference? Current(DiagnosticInventoryPresentationState state, DateTimeOffset now) =>
        state.Current(now, "2.320", Fingerprint);

    [Fact]
    public void FreshBasicContinuesAcrossGrowthFailureAndFourSecondStall()
    {
        var state = new DiagnosticInventoryPresentationState();
        Assert.True(Accept(state, Basic(1, Now), Now));
        Assert.True(Accept(state, Full(1, Now), Now));
        Assert.True(Current(state, Now)!.GrowthAttributionAvailable);
        for (var second = 1; second <= 5; second++)
        {
            var time = Now.AddSeconds(second);
            Assert.True(Accept(state, Basic(second + 1, time), time));
            Assert.False(Accept(state, new RecognitionResult { State = RecognitionState.TransientReadError, Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName) }, time));
            var visible = Current(state, time)!;
            Assert.Equal(2, visible.Entries.Sum(x => x.Count));
            if (second < 3)
            {
                Assert.True(visible.GrowthAttributionAvailable);
                Assert.Equal("rawcode:I10h", Assert.Single(visible.GrowthReservedUnitIds));
            }
            else
            {
                Assert.False(visible.GrowthAttributionAvailable);
                Assert.Empty(visible.GrowthReservedUnitIds);
                Assert.Null(visible.ObservedRound);
            }
        }
    }

    [Fact]
    public void GrowthExpiryFallsBackToLatestBasicWithoutRefreshingFullTimestamp()
    {
        var state = new DiagnosticInventoryPresentationState();
        var full = Full(1, Now);
        Assert.True(Accept(state, Basic(1, Now), Now));
        Assert.True(Accept(state, full, Now));
        var later = Now.AddSeconds(2.9);
        var latest = Basic(2, later);
        Assert.True(Accept(state, latest, later));
        Assert.Same(latest, Current(state, later));
        Assert.Equal(Now.AddMilliseconds(-100), full.DiagnosticObservation!.StartedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MismatchedFullCannotReplaceCurrentBasic(bool worldMismatch)
    {
        var state = new DiagnosticInventoryPresentationState();
        var basic = Basic(1, Now);
        Assert.True(Accept(state, basic, Now));
        var full = worldMismatch ? Full(1, Now, new string('D', 64)) : Full(1, Now);
        if (!worldMismatch)
            state.Invalidate();
        Assert.False(Accept(state, full, Now));
        Assert.Same(worldMismatch ? basic : null, Current(state, Now));
    }

    [Fact]
    public void BasicFailureKeepsLastGoodUntilANewerBasicArrives()
    {
        var state = new DiagnosticInventoryPresentationState();
        var basic = Basic(1, Now);
        Assert.True(Accept(state, basic, Now));
        Assert.True(Accept(state, Full(1, Now), Now));
        Assert.False(state.TryAcceptBasic(null, Diagnostics(DiagnosticBasicInventoryObservation.SourceName), Now,
            "2.320", Fingerprint, out _));
        Assert.Equal(2, Current(state, Now)!.Entries.Sum(item => item.Count));
        Assert.True(Accept(state, Full(2, Now), Now));
        Assert.False(Accept(state, Basic(1, Now), Now));
        Assert.True(Accept(state, Basic(2, Now), Now));
        Assert.True(Accept(state, Full(3, Now), Now));
        Assert.True(Current(state, Now)!.GrowthAttributionAvailable);
    }

    [Fact]
    public void BasicExpiryAndSessionBoundaryClearBothAndRetainRevisionFences()
    {
        var state = new DiagnosticInventoryPresentationState();
        var basic = Basic(1, Now);
        Assert.True(Accept(state, basic, Now));
        Assert.True(Accept(state, Full(1, Now), Now));
        Assert.Null(Current(state, basic.StartedAt.AddSeconds(3)));
        Assert.False(Accept(state, new RecognitionResult { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true }, Now));
        Assert.Null(Current(state, Now));
        Assert.False(Accept(state, basic, Now));
        Assert.False(Accept(state, Full(1, Now), Now));
    }

    [Fact]
    public void FullOnlyFixtureWorksUntilBasicLaneHasBeenObserved()
    {
        var state = new DiagnosticInventoryPresentationState();
        var full = Full(1, Now);
        Assert.True(Accept(state, full, Now));
        Assert.Same(full.DiagnosticObservation, Current(state, Now));
        state.Invalidate();
        Assert.False(Accept(state, full, Now));
        Assert.True(Accept(state, Full(2, Now), Now));
    }
    [Fact]
    public void BindingIdentityPreservesSelectionAcrossDistinctBasicAndFullContexts()
    {
        var basic = Basic(1, Now);
        var full = DiagnosticInventoryObservation.Create(Data.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            new string('D', 64), 1, 0, Now.AddMilliseconds(-100), Now, TimeSpan.FromMilliseconds(100),
            [new() { UnitId = "rawcode:I10h", Count = 2 }], ["rawcode:I10h"], 1, World, Binding);
        var model = new NormalCandidateBrowser(Data.Value.AllUnits) { ReferencePresentationIsValid = () => true };
        model.UpdateReference(basic, 1);
        model.SetReferenceStage(NormalCandidateStage.Upper);
        model.Select("rawcode:I10h");
        model.Fold("fixture", true);
        var unchangedSnapshot = model.Snapshot;
        model.UpdateReference(full, 1);
        Assert.Same(unchangedSnapshot, model.Snapshot);
        Assert.True(model.ReferenceGrowthAttributionAvailable);
        Assert.Equal(NormalCandidateStage.Upper, model.Stage);
        Assert.Equal("rawcode:I10h", model.SelectedUnitId);
        Assert.Contains("fixture", model.CollapsedCategories);
        model.UpdateReference(Basic(2, Now), 1);
        Assert.Equal(NormalCandidateStage.Upper, model.Stage);
        Assert.Equal("rawcode:I10h", model.SelectedUnitId);
        Assert.Contains("fixture", model.CollapsedCategories);
        Assert.Same(unchangedSnapshot, model.Snapshot);
        Assert.False(model.ReferenceGrowthAttributionAvailable);
        Assert.Contains("인식한 패 기준 참고 안내", model.Snapshot.Status);
        Assert.False(model.SelectedCandidate!.Owned);
    }

    [Theory]
    [InlineData(RecognitionState.Unsupported)]
    [InlineData(RecognitionState.UnverifiedProfile)]
    [InlineData(RecognitionState.ConfigurationError)]
    public void HardResultBoundariesClearBothLanes(RecognitionState stateValue)
    {
        var state = new DiagnosticInventoryPresentationState();
        Assert.True(Accept(state, Basic(1, Now), Now));
        Assert.True(Accept(state, Full(1, Now), Now));
        Assert.False(Accept(state, new RecognitionResult { State = stateValue,
            Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName) }, Now));
        Assert.Null(Current(state, Now));
        Assert.False(Accept(state, Full(2, Now), Now));
    }

    [Fact]
    public void TransientNonDiagnosticFailureKeepsPreviousBasic()
    {
        var state = new DiagnosticInventoryPresentationState();
        var basic = Basic(1, Now);
        Assert.True(Accept(state, basic, Now));
        Assert.False(Accept(state, new RecognitionResult { State = RecognitionState.TransientReadError }, Now));
        Assert.Same(basic, Current(state, Now));
    }

    [Fact]
    public void RepeatedTransientCompletionsDoNotRenewFreshnessOrPermitRevisionReplay()
    {
        var state = new DiagnosticInventoryPresentationState();
        var basic = Basic(1, Now);
        Assert.True(Accept(state, basic, Now));
        Assert.True(Accept(state, Full(1, Now), Now));
        foreach (var seconds in new[] { .5, 1, 2.8 })
        {
            var later = Now.AddSeconds(seconds);
            Assert.False(Accept(state, new RecognitionResult { State = RecognitionState.TransientReadError }, later));
            Assert.True(Current(state, later)!.GrowthAttributionAvailable);
        }
        Assert.Equal(Now.AddMilliseconds(-10), basic.StartedAt);
        Assert.Null(Current(state, basic.StartedAt.AddSeconds(3)));
        Assert.False(Accept(state, basic, Now));
        Assert.Null(Current(state, Now));
        var recovery = Basic(2, Now.AddSeconds(4));
        Assert.True(Accept(state, recovery, recovery.CompletedAt));
        Assert.Same(recovery, Current(state, recovery.CompletedAt));
    }

    [Fact]
    public void PublicReferenceInterfaceDoesNotAuthorizeUnknownImplementations()
    {
        var poison = System.Reflection.DispatchProxy.Create<IDiagnosticInventoryReference, PoisonReference>();
        var model = new NormalCandidateBrowser(Data.Value.AllUnits);
        Assert.Throws<ArgumentException>(() => model.UpdateReference(poison, 1));
        Assert.Null(DiagnosticReferenceStats.From(poison, true).ObservedCount);
    }

    public class PoisonReference : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Unknown reference fields must not be trusted or read.");
    }

}
