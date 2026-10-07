using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticInventoryPresentationIntegrationTests
{
    private static readonly Lazy<DataCatalog> Data = new(() => { var c = new DataCatalog(); c.Load(mapVersion: "2.320"); return c; });
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
    private static DiagnosticInventoryObservation Observation(string? id = null, string? context = null, long revision = 1, int slot = 0) =>
        DiagnosticInventoryObservation.Create(Data.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            context ?? new string('A', 64), revision, slot, Now.AddSeconds(-0.2), Now, TimeSpan.FromSeconds(.2),
            [new() { UnitId = id ?? "rawcode:I10h", Count = 2 }], []);
    private static RecognitionResult Result(DiagnosticInventoryObservation? value, string? source = null) => new()
    {
        DiagnosticObservation = value,
        Diagnostics = new() { Source = source ?? DiagnosticInventoryObservation.SourceName,
            ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash }
    };
    private static bool Present(DiagnosticInventoryObservation value, DateTimeOffset now) =>
        DiagnosticInventoryConsumerPolicy.CanPresent(value, now, "2.320", Data.Value.OfflineBundle!.Fingerprint,
            value.ContextId, value.SourceRevision, value.ViewSlot!.Value);

    [Fact] public void ReferenceAdvancesObservedProgressWithoutGameplayOwnership()
    {
        var upper = Data.Value.AllUnits.First(NormalCandidateBrowser.IsUpper);
        var value = Observation(upper.Id);
        var model = new NormalCandidateBrowser(Data.Value.AllUnits) { ReferencePresentationIsValid = () => true };
        model.UpdateReference(value, 1);
        Assert.Equal(NormalCandidateStage.Utility, model.Stage); Assert.Equal(upper.Id, model.FirstUpperId);
        model.SetReferenceStage(NormalCandidateStage.Upper); model.Select(upper.Id);
        Assert.False(model.SelectedCandidate!.Owned);
        Assert.False(model.CanHighlight(model.SelectedCandidate));
        Assert.Equal(2, model.SelectedCandidate.ObservedCount);
        Assert.Contains("참고 안내", model.Snapshot.Status); Assert.Contains("게임에서 확인", model.SelectedCandidate.Caveat);
        Assert.False(value.GameplayReady); Assert.False(value.CanProvideCoachCurrent);
    }
    [Fact] public void PresentationExpiresFromStartWithoutAcceptanceReplay()
    {
        var value = Observation(); var now = Now;
        var model = new NormalCandidateBrowser(Data.Value.AllUnits) { ReferencePresentationIsValid = () => Present(value, now) };
        model.UpdateReference(value, 1); Assert.True(model.Snapshot.IsCurrent);
        now = value.StartedAt.AddSeconds(3);
        model.Select(value.Entries[0].UnitId); model.FoldAll(false); model.SetDirection("physical");
        model.SetReferenceStage(NormalCandidateStage.Upper);
        Assert.False(model.Snapshot.IsCurrent); Assert.Null(model.SelectedCandidate!.Allocation);
        now = Now; model.RevalidatePresentation(); Assert.False(model.Snapshot.IsCurrent);
        Assert.False(DiagnosticInventoryConsumerPolicy.TryAccept(Result(value), now, "2.320", value.DatasetFingerprint,
            value.ContextId, value.SourceRevision, value.ViewSlot!.Value, out _, out _));
    }
    [Fact] public void ContextAndViewChangesResetExplicitStageAndPin()
    {
        var model = new NormalCandidateBrowser(Data.Value.AllUnits) { ReferencePresentationIsValid = () => true };
        var first = Observation(); model.UpdateReference(first, 1);
        model.SetReferenceStage(NormalCandidateStage.Utility); model.Select(first.Entries[0].UnitId);
        model.UpdateReference(Observation(context: new string('B', 64), revision: 2), 1);
        Assert.Equal(NormalCandidateStage.Rare, model.Stage); Assert.Null(model.SelectedUnitId); Assert.Null(model.FirstUpperId);
        model.SetReferenceStage(NormalCandidateStage.Upper); model.UpdateReference(Observation(context: new string('B', 64), revision: 3, slot: 2), 1);
        Assert.Equal(NormalCandidateStage.Rare, model.Stage);
    }
    [Fact] public void ReferenceStatsNeverInventUnknownCountsOrCombatTotals()
    {
        var value = DiagnosticReferenceStats.From(Observation(), true);
        Assert.Equal(2, value.ObservedCount); Assert.Equal("?", value.AliveCount); Assert.Equal("?", value.LocalCount);
        Assert.Equal("?", value.Stun); Assert.Equal("?", value.Slow); Assert.Equal("?", value.Armor); Assert.Equal("?", value.Damage);
        Assert.Null(DiagnosticReferenceStats.From(Observation(), false).ObservedCount);
        Assert.Null(DiagnosticReferenceStats.From(null, true).ObservedCount);
    }
    [Theory] [InlineData(null)] [InlineData("WarcraftMemory")]
    public void MissingOrSpoofedContractCannotProvideReference(string? source)
    {
        var value = Observation(); var result = source is null ? Result(null) : Result(value, source);
        Assert.False(DiagnosticInventoryConsumerPolicy.TryAccept(result, Now, "2.320", value.DatasetFingerprint,
            value.ContextId, 0, 0, out _, out _));
    }
    [Fact] public void PresentationRejectsContextViewRevisionAndDatasetChanges()
    {
        var value = Observation();
        Assert.True(Present(value, Now));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(value, Now, "2.320", value.DatasetFingerprint, new string('B', 64), 1, 0));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(value, Now, "2.320", value.DatasetFingerprint, value.ContextId, 2, 0));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(value, Now, "2.320", value.DatasetFingerprint, value.ContextId, 1, 1));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(value, Now, "2.314", value.DatasetFingerprint, value.ContextId, 1, 0));
    }
    [Fact] public void EffectiveLiveSupportPropertyRemainsFalseNotJustACommentOrLookalike()
    {
        Assert.False(Data.Value.OfflineBundle!.LiveRecognitionSupported);
        Assert.Matches(@"public\s+bool\s+LiveRecognitionSupported\s*=>\s*false\s*;", Source("Map2320DataBundle.cs"));
        Assert.False(Observation().GameplayReady);
    }
    [Theory]
    [InlineData(OverlayDisplayMode.Full, PlayMode.Normal, true, true)]
    [InlineData(OverlayDisplayMode.StatsOnly, PlayMode.Normal, false, true)]
    [InlineData(OverlayDisplayMode.Hidden, PlayMode.Normal, false, false)]
    [InlineData(OverlayDisplayMode.Full, PlayMode.Guide, false, true)]
    [InlineData(OverlayDisplayMode.Full, PlayMode.Manual, false, true)]
    [InlineData(OverlayDisplayMode.Full, PlayMode.Beginner, false, true)]
    public void ReferenceVisibilityRespectsUserChoiceAndNeverShowsOtherModesAsLiveCoach(
        OverlayDisplayMode mode, PlayMode playMode, bool browser, bool stats)
    {
        var visible = DiagnosticReferencePresentationPolicy.Visibility(true, playMode, mode, OverlayDisplayMode.StatsOnly);
        Assert.Equal(browser, visible.RecommendationVisible); Assert.Equal(stats, visible.StatsVisible);
        var expired = DiagnosticReferencePresentationPolicy.Visibility(false, playMode, mode, OverlayDisplayMode.Full);
        Assert.False(expired.RecommendationVisible); Assert.False(expired.StatsVisible);
    }
    [Fact] public void RejectionStatusIsSpecificAndDoesNotEchoUntrustedNativeDetails()
    {
        Assert.Contains("지원하지 않는", DiagnosticReferencePresentationPolicy.RejectionStatus(new() { State = RecognitionState.Unsupported, Status = "SECRET ADDRESS" }, ""));
        Assert.Contains("읽기 실패", DiagnosticReferencePresentationPolicy.RejectionStatus(new() { State = RecognitionState.TransientReadError }, ""));
        Assert.Contains("오래되어", DiagnosticReferencePresentationPolicy.RejectionStatus(new(), "Diagnostic observation stale, future-dated or over budget"));
        Assert.Contains("게임 정보를 확인 중", DiagnosticReferencePresentationPolicy.RejectionStatus(new() { State = RecognitionState.Waiting }, ""));
        Assert.Contains("게임 입장 대기 중", DiagnosticReferencePresentationPolicy.RejectionStatus(new() { State = RecognitionState.Waiting, ConfirmsSessionBoundary = true }, ""));
        Assert.DoesNotContain("SECRET ADDRESS", DiagnosticReferencePresentationPolicy.RejectionStatus(new() { State = RecognitionState.Unsupported, Status = "SECRET ADDRESS" }, ""));
    }
    [Fact] public void VisibilityAndStatusAreActualWindowOperationsNotOnlyAnAvailabilityFlag()
    {
        var lane = Source("MainWindow.DiagnosticInventory.cs") + Source("MainWindow.DiagnosticRendering.cs");
        Assert.Contains("RecognitionStatus.Foreground = RandyPickTheme.Warning", lane);
        Assert.Contains("_overlay.Show()", lane); Assert.Contains("_overlay.Hide()", lane);
        Assert.Contains("_overlay.Stats.Show()", lane); Assert.Contains("_overlay.Stats.Hide()", lane);
        Assert.Contains("ApplyDiagnosticOverlayVisibility();", lane);
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(lane, @"_settings\.OverlayDisplayMode\s*=(?!=)"));
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(lane, @"_settings\.LastVisibleOverlayDisplayMode\s*=(?!=)"));
        Assert.DoesNotContain("_overlay.RenderCoach(", lane);
        Assert.Contains("CaptureDiagnosticInventoryUiProof()", lane);
    }
    [Fact] public void RefreshAllDoesNotOverwriteDiagnosticInventoryRows()
    {
        var source = Source("MainWindow.xaml.cs");
        var start = source.IndexOf("private async void RefreshAll(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var clear = source.IndexOf("InventoryList.Items.Clear();", start, StringComparison.Ordinal);
        Assert.True(clear > start);
        var guard = source.LastIndexOf("if (!UsesMap2320)", clear, StringComparison.Ordinal);
        Assert.True(guard > start);
        Assert.Equal("if (!UsesMap2320)\n        {",
            source[guard..clear].Replace("\r\n", "\n").Trim());
        var end = source.IndexOf("\n        }", clear, StringComparison.Ordinal);
        Assert.True(end > clear);
        Assert.Contains("foreach (var item in inventory)", source[clear..end]);
        Assert.Contains("if (inventory.Count == 0) InventoryList.Items.Add", source[clear..end]);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrandOverlay.csproj"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, file));
    }
    [Fact] public void IsolatedLaneContainsNoGameplayMutationOrRefreshAll()
    {
        var source = Source("MainWindow.DiagnosticInventory.cs") + Source("MainWindow.DiagnosticAcceptance.cs") +
            Source("MainWindow.DiagnosticRendering.cs") + Source("MainWindow.DiagnosticFrames.cs");
        foreach (var forbidden in new[] { "_automatic.Clear(", "_automatic[", "_completedTopUnits", "_outcome.Observe", "_liveSessionActive =", "RefreshAll(", "CaptureMatchTelemetry", "_bulletLearning" })
            Assert.DoesNotContain(forbidden, source);
        Assert.Contains("if (!UsesMap2320)", source); Assert.Contains("return true; // Includes untyped/spoofed Ready", source);
        Assert.Contains("Dispatcher.VerifyAccess()", source); Assert.Contains("DiagnosticExpiryTick", source);
        var stats = Source("StatsOverlayWindow.DiagnosticInventory.cs");
        Assert.DoesNotContain("AddMeter(", stats); Assert.DoesNotContain("TeamStatCalculator", stats);
        Assert.Contains("실제 보유 유닛과 다를 수 있어요", stats);
    }
}
