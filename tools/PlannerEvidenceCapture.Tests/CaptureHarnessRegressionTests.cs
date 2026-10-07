using System.Text.RegularExpressions;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class CaptureHarnessRegressionTests
{
    [Fact]
    public void StopEventProbeNeverEnablesRuntimeAndRecordsBlockedProductionBranch()
    {
        var source = Read("BulletGuideCapture.cs");
        Assert.Contains("StopEventProbe();", source);
        Assert.Contains("stop-event-old-combat", ObserveNames(source));
        Assert.Contains("stop-event-reset", ObserveNames(source));
        Assert.Contains("CheckBox.UncheckedEvent", source);
        Assert.Contains("stop-event-probe.json", source);
        Assert.Contains("ProductionStopBranchExecuted = afterGeneration > beforeGeneration", source);
        Assert.Contains("Runtime guard prevents Fixture from executing production Stop body", source);
        // Source-wiring contract only; behavior is exercised by ScanStopTransitionTests.
        Assert.Contains("main.StopControlledObservation()", source);
        Assert.Contains("stop-common-transition-manual-zero", source);
        Assert.Contains("guard-preserved-common-stop-observation-transition", Read("BulletGuideRowProjection.cs"));
        Assert.Contains("stopped.Frame.CombatObservations.IsEmpty", source);
        Assert.DoesNotContain("GetField(\"_runtimeEffects\", flags)!.SetValue", source);
    }

    [Fact]
    public void QueenEventFixtureUsesBothCombosAndRejectsStaleRenderedTokens()
    {
        // Source contract only: actual WPF events remain a separately authorized run.
        var source = Read("BulletGuideCapture.cs");
        Assert.Contains("queen-event-owned", ObserveNames(source));
        Assert.Contains("queen-event-reset", ObserveNames(source));
        Assert.Contains("QueenEventChecks();", source);
        Assert.Contains("combo.SelectedValue = input", source);
        Assert.Contains("stale with { Revision = stale.Revision - 1 }", source);
        Assert.Contains("stale with { MatchGeneration = stale.MatchGeneration - 1 }", source);
        Assert.Contains("Queen user input disagrees between main and overlay", source);
        Assert.Contains("User evidence is not native observation", source);
        Assert.DoesNotContain("GetMethod(\"ResetMatchSession\"", source);
        Assert.Contains("ConfirmsSessionBoundary = BulletGuideRowProjection.ConfirmsSessionBoundary(name)", source);
        Assert.True(BulletGuideRowProjection.ConfirmsSessionBoundary("queen-event-reset"));
        Assert.True(BulletGuideRowProjection.ConfirmsSessionBoundary("stop-event-reset"));
    }

    [Fact]
    public void NasjuroScenariosUseCurrentFramesAndBothRenderedSurfaces()
    {
        var source = Read("BulletGuideCapture.cs");
        foreach (var name in new[] { "nasjuro-live", "nasjuro-dead", "nasjuro-unobserved", "nasjuro-no-wisp",
            "nasjuro-none", "nasjuro-ship-protected", "nasjuro-disconnected", "nasjuro-reset-warcury",
            "nasjuro-reset-nasjuro", "nasjuro-reset-none", "nasjuro-upgrade-wisp", "nasjuro-upgrade-spent",
            "nasjuro-ship-shortage" }) Assert.Contains(name, ObserveNames(source));
        Assert.Contains("CheckNasjuroRendered",source);
        Assert.Contains("BulletGuideRowProjection.Observe(name, result.Decision, result.Frame)", source);
        Assert.Contains("= frame.Gorosei", Read("BulletGuideRowProjection.cs"));
        Assert.Contains("= frame.RecognitionRevision", Read("BulletGuideRowProjection.cs"));
        Assert.Contains("Nasjuro None retained actual guidance on main or overlay",source);
        Assert.Contains("Nasjuro boundary discarded first fresh marker or reused combat",source);
        Assert.Contains("nasjuro-", source);
    }
    [Fact]
    public void BlackMariaScenariosAssertBothRenderedSurfacesThroughObserve()
    {
        var source = Read("BulletGuideCapture.cs");
        foreach (var name in new[] { "blackmaria-stun-shortage", "blackmaria-slow-shortage", "blackmaria-both-shortages",
            "blackmaria-selection-unknown", "blackmaria-foreign", "blackmaria-disconnected" })
            Assert.Contains(name, ObserveNames(source));
        Assert.Contains("BlackMariaSelectedMode = BlackMariaMode.Burn", source);
        Assert.Contains("CheckBlackMariaRendered", source);
        Assert.Contains("스턴 부족 → 스턴 선택 권고", source);
        Assert.Contains("이감 부족 → 이감 선택 권고", source);
        Assert.Contains("스턴·이감 모두 부족", source);
        Assert.Contains("BlackMaria disconnect retained selection or role advice", source);
        Assert.Contains("BlackMaria render did not match support/operation on main and overlay", source);
        Assert.Contains("support.IsVisible && operation.IsVisible", source);
    }

    [Fact]
    public void BulletGuideObserveNamesAreUniqueForCreateNewSink()
    {
        var names = ObserveNames(Read("BulletGuideCapture.cs"));
        var duplicates = names.GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.True(duplicates.Length == 0,
            "Duplicate Observe names collide with CaptureOutputScope.CreateNew: " +
            string.Join(", ", duplicates));
    }

    [Fact]
    public void BulletGuideOvernightSessionResetUsesDistinctCreateNewName()
    {
        var source = Read("BulletGuideCapture.cs");
        var names = ObserveNames(source);
        Assert.Contains("new-session", names);
        Assert.Equal(1, names.Count(name => name == "new-session"));
        Assert.Contains("overnight-new-session", names);
        Assert.Contains("overnight-uncommon-reserved", names);
        Assert.Contains("overnight-uncommon-sale", names);
        Assert.Contains("overnight-uncommon-unknown", names);
        Assert.Contains("overnight-ancient-ship", names);
        Assert.Contains("overnight-milestone-50-no-common", names);
        Assert.Contains("overnight-placement-distance", names);
        Assert.Contains("ConfirmsSessionBoundary = BulletGuideRowProjection.ConfirmsSessionBoundary(name)", source);
        Assert.True(BulletGuideRowProjection.ConfirmsSessionBoundary("new-session"));
        Assert.True(BulletGuideRowProjection.ConfirmsSessionBoundary("overnight-new-session"));
    }

    [Fact]
    public void CoachAppLeavesBeginnerThroughPlayModeCombo()
    {
        var source = Read("CoachAppCapture.cs");
        Assert.DoesNotContain("BeginnerModeCheck", source);
        Assert.Contains("PlayModeCombo", source);
        Assert.Contains("PlayMode.Manual", source);
        Assert.Contains("PlayMode.Beginner", source);
        Assert.DoesNotContain("Expert mode did not preserve the fixed Gaban goal.", source);
        Assert.Contains("Manual mode did not keep the Gaban goal, visible coach, automatic navigation, and AutoStartGoal off.", source);
        Assert.Contains("Automatic mode discarded an already-owned top.", source);
        Assert.Contains("!view.IsVisible", source);
        Assert.Contains("settings.AutoStartGoal", source);
        Assert.Contains("!settings.AutoRecommendNavigation", source);
        Assert.Contains("rawcode:F40h", source);
    }

    [Fact]
    public void CoachFinishedRewardsSelectsOperationNotesExpanderNotFirstOrUnfilteredSingle()
    {
        var source = Read("CoachFinishedRewardCapture.cs");
        Assert.DoesNotContain("OfType<Expander>().Single()", source);
        Assert.DoesNotContain("OfType<Expander>().First()", source);
        Assert.Contains("while (node is not Expander)", source);
        Assert.Contains("Operation expander parent was not found.", source);
        Assert.Contains("FindName(\"OperationText\")", source);
    }

    private static IReadOnlyList<string> ObserveNames(string source) =>
        Regex.Matches(source, @"Observe\(\s*""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToArray();

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(CaptureIsolationSourceTests.SourceRoot(), name));
}
