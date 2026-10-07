using System.Reflection;
using System.Text.Json;
using OrandOverlay;
using Xunit;
using Xunit.Abstractions;
namespace PlannerEvidenceCapture.Tests;

public sealed class RandipickCleanupReplayTests(ITestOutputHelper output)
{
    [Fact]
    public void ActualProducerRowsKeepActionBudgetsAndNavigationWarnings()
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var type = typeof(CaptureInputContract).Assembly.GetType("PlannerEvidenceCapture.RandipickCleanupFixture");
        Assert.NotNull(type);
        var cases = (IReadOnlyList<FastUniqueUiCase>)type!.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [catalog])!;
        Assert.Equal(16, cases.Count);
        var model = typeof(FastUniqueUiPolicyReplayTests).GetMethod("ModelFrame", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var item in cases)
        {
            Assert.All(item.Inventory.Values, count => Assert.True(count > 0));
            var request = FastUniqueUiFixture.Request(item, 1, 1);
            var frame = (CoachFrame)model.Invoke(null, [catalog, item, request, FastUniqueState.Unknown, 1L, 1L])!;
            frame = frame with { ShipReservations = ShipReservationPolicy.Evaluate(frame, catalog) };
            var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
            var display = CoachPresentation.Create(decision, frame);
            Assert.Equal(decision.Title, display.Title); Assert.Equal(decision.Controls, display.Controls);
            if (item.Name == "selection-1")
            {
                Assert.NotEqual("e018", decision.RewardWispId);
                Assert.Null(decision.SelectionBatch);
                Assert.Equal(1, frame.RewardWisps.GetValueOrDefault("e018"));
                Assert.True(display.ShowEssentialReason); Assert.True(display.ShowConfirmation);
            }
            if (item.Name == "first-rare-7") { Assert.Equal(CoachActionKind.Craft, decision.Kind); Assert.NotNull(decision.CraftRecipe); }
            if (item.Name == "e016-received") { Assert.Equal(CoachActionKind.Reward, decision.Kind); Assert.True(display.ShowEssentialReason); }
            if (item.Name == "actual-other")
            {
                Assert.Equal(NativeNavigationStatus.Selected, frame.NativeNavigation.Status);
                Assert.NotEqual(frame.GuidePlan!.PlannedNavigation, frame.NativeNavigation.OptionId);
                Assert.Contains(NavigationProfiles.Find(frame.NativeNavigation.OptionId!).Name, display.NavigationAlert);
            }
            if (item.Name == "navigation-conflict-held")
            {
                Assert.Equal(CoachActionKind.Waiting, decision.Kind);
                Assert.Equal("navigation-conflict", decision.Id);
                Assert.Equal(decision.Title, display.Title);
                Assert.True(display.ShowEssentialReason);
                Assert.Equal(NativeNavigationPresentation.Describe(frame.NativeNavigation, null), display.NavigationAlert);
            }
            if (item.Name == "legend-19-progress")
            {
                Assert.Equal(CoachActionKind.Craft, decision.Kind);
                Assert.Contains(decision.TargetUnitId, new[] { "rawcode:F00h", "rawcode:V00h" });
            }
            if (item.Name is "legend-19-auxiliary-consumable" or "legend-19-auxiliary-spare")
            {
                Assert.Equal(CoachActionKind.Craft, decision.Kind);
                Assert.Equal("rawcode:220h", decision.TargetUnitId);
                var spare = item.Name == "legend-19-auxiliary-spare" ? 1 : 0;
                Assert.Equal(1 + spare, frame.Inventory.GetValueOrDefault("rawcode:D10h"));
                var after = BulletGuideCraftSafety.ProjectAfterCraft(catalog, decision.TargetUnitId!, frame.Inventory);
                Assert.NotNull(after);
                Assert.Equal(spare, after.GetValueOrDefault("rawcode:D10h"));
                Assert.Equal(1, after.GetValueOrDefault("rawcode:220h"));
                Assert.Equal(frame.Inventory.GetValueOrDefault("rawcode:HA0h"), after.GetValueOrDefault("rawcode:HA0h"));
            }
            if (item.Name.StartsWith("legend-chain-", StringComparison.Ordinal))
            {
                if (item.Name == "legend-chain-complete")
                {
                    Assert.Equal(1, frame.Inventory.GetValueOrDefault("rawcode:B30h"));
                    Assert.Equal(BulletGuideStage.SecondLegend, frame.GuidePlan!.Stage);
                }
                else
                {
                    var index = int.Parse(item.Name["legend-chain-".Length..]);
                    string[] expected = ["F00h", "V00h", "V00h", "220h", "E20h", "J20h", "B30h"];
                    Assert.Equal(CoachActionKind.Craft, decision.Kind);
                    Assert.Equal("rawcode:" + expected[index], decision.TargetUnitId);
                    Assert.Equal(7 - index, display.RemainingGuideCrafts);
                }
            }
            output.WriteLine(JsonSerializer.Serialize(new { item.Name, Kind = "synthetic-headless-policy-not-WPF", request, decision, display }));
        }
    }
}
