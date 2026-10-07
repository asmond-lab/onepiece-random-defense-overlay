using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322CraftPresentationTests
{
    [Fact]
    public async Task YujiroDetachedViewKeepsConditionsAndFullWidthMaterialGroups()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var catalog = new DataCatalog();
                catalog.Load(mapVersion: "2.322");
                var goal = catalog.AllUnits.Single(unit => unit.Rawcodes.Contains("2C0h"));
                var model = new NormalCandidateBrowser(catalog.AllUnits) { ReferencePresentationIsValid = () => true };
                var view = new NormalCandidateView { IsMainWorkspace = true };
                view.SetCraftPlanner(new NormalCraftPlanner(catalog));
                var workspace = view.DetachCraftWorkspace(renderCraft: true);
                var now = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
                var observation = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
                    Warcraft300Diagnostic.Hash, new string('A', 64), 1, 0, now, now.AddMilliseconds(10),
                    TimeSpan.FromMilliseconds(10), [], [], 1, new string('C', 64), new string('B', 64));
                Assert.Equal(DiagnosticInventoryAvailability.Ready, observation.Availability);
                model.UpdateReference(observation, 1);
                model.Select(goal.Id);
                view.SetModel(model);
                workspace.Measure(new Size(326, 386));
                workspace.Arrange(new Rect(0, 0, 326, 386));
                workspace.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                workspace.UpdateLayout();

                var craftNodes = Descendants(workspace).ToArray();
                var stepExpander = Assert.Single(craftNodes.OfType<Expander>(), element =>
                    AutomationProperties.GetAutomationId(element) == "normal-craft-step-toggle-rawcode:2C0h");
                // A templated header is data, not a logical child of the Expander.
                var headerContent = (DependencyObject)stepExpander.Header.GetType()
                    .GetProperty("Content")!.GetValue(stepExpander.Header)!;
                var nodes = craftNodes.Concat(Descendants(headerContent)).ToArray();
                var selection = Assert.Single(nodes.OfType<FrameworkElement>(), element =>
                    AutomationProperties.GetAutomationId(element) == "normal-craft-selection-KING:h0C2-PICK:A800");
                Assert.NotEqual(AutomationProperties.GetAutomationId(selection), AutomationProperties.GetName(selection));
                Assert.DoesNotContain("KING:h0C2", selection.ToolTip?.ToString());
                Assert.DoesNotContain("PICK:A800", selection.ToolTip?.ToString());
                Assert.DoesNotContain("KING", AutomationProperties.GetName(selection));
                var action = Assert.Single(nodes.OfType<FrameworkElement>(), element =>
                    AutomationProperties.GetAutomationId(element) == "normal-craft-action-rawcode:2C0h");
                Assert.DoesNotContain("KING", AutomationProperties.GetName(action));
                Assert.DoesNotContain("PICK:A800", AutomationProperties.GetName(action));
                Assert.DoesNotContain("?", string.Join(" ", Descendants(action).OfType<TextBlock>().Select(text => text.Text)));
                var unavailable = Assert.Single(nodes.OfType<TextBlock>(), text =>
                    AutomationProperties.GetAutomationId(text) == "normal-material-unavailable-reason");
                Assert.DoesNotContain("근거 미확인", unavailable.Text);
                var source = Assert.Single(nodes.OfType<Expander>(), element =>
                    element.Header is string title && title.Contains("출처"));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName((FrameworkElement)source.Content)));
                var missingSection = Assert.Single(nodes.OfType<Expander>(), element =>
                    AutomationProperties.GetAutomationId(element) == "normal-missing-materials");
                Assert.NotNull(missingSection.HeaderTemplate);
                Assert.DoesNotContain("System.Windows.Controls.", missingSection.Header.ToString());
                var missing = Assert.Single(nodes.OfType<UniformGrid>(), grid =>
                    AutomationProperties.GetAutomationId(grid) == "normal-missing-grid");
                var ingredients = Assert.Single(nodes.OfType<UniformGrid>(), grid =>
                    AutomationProperties.GetAutomationId(grid) == "normal-craft-materials-rawcode:2C0h");
                Assert.Equal(1, missing.Columns);
                Assert.Equal(1, ingredients.Columns);
                var stepDetails = Assert.Single(nodes.OfType<Expander>(), element =>
                    AutomationProperties.GetAutomationId(element) == "normal-craft-step-toggle-rawcode:2C0h");
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName((FrameworkElement)stepDetails.Content)));
                Assert.NotNull(stepDetails.HeaderTemplate);
                Assert.DoesNotContain("System.Windows.Controls.", stepDetails.Header.ToString());
                foreach (var resourceId in new[] { "GOLD", "LUMBER" })
                {
                    var ingredient = Assert.Single(nodes.OfType<FrameworkElement>(), element =>
                        AutomationProperties.GetAutomationId(element) == "normal-craft-material-rawcode:2C0h/rawcode:" + resourceId);
                    Assert.DoesNotContain(resourceId, AutomationProperties.GetName(ingredient));
                }
                foreach (var id in new[] { "rawcode:Y20h", "PICK:A800" })
                {
                    var tile = Assert.Single(nodes.OfType<FrameworkElement>(), element =>
                        AutomationProperties.GetAutomationId(element) == "normal-craft-material-missing/" + id);
                    Assert.Equal(Visibility.Visible, tile.Visibility);
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(tile)));
                }
                Assert.Contains(nodes.OfType<FrameworkElement>(), element =>
                    AutomationProperties.GetAutomationId(element) == "normal-missing-resource-GOLD" &&
                    element is TextBlock text && text.Text.Contains("10000"));
                Assert.Contains(nodes.OfType<FrameworkElement>(), element =>
                    AutomationProperties.GetAutomationId(element) == "normal-missing-resource-LUMBER" &&
                    element is TextBlock text && text.Text.Contains("7"));
                var step = Assert.Single(new NormalCraftPlanner(catalog).Build(goal.Id, model.CurrentInventory).Steps);
                Assert.False(step.IsMaterialReady);
                Assert.Equal(RecipeConditionStatus.Unknown, step.Conditions.Status);
                done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void BoardProjectsConditionDiagnosticsWithoutChangingWarningCounts()
    {
        var rawCondition = "조합 보류: 미해결 원본 조건 · KING:h0C2 / PICK:A800";
        var displayed = RecommendationBoard.DisplayNextAction(rawCondition);
        Assert.NotEqual(rawCondition, displayed);
        Assert.DoesNotContain("KING:h0C2", displayed);
        Assert.DoesNotContain("PICK:A800", displayed);

        var warning = "목표 재료 소모 — 하위 패 3개 추가 확보 필요";
        var projected = RecommendationBoard.DisplayWarning(warning);
        var quantity = Assert.Single(System.Text.RegularExpressions.Regex.Matches(projected, @"\d+"));
        Assert.Equal(3, int.Parse(quantity.Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var node in Descendants(child)) yield return node;
    }
}
