using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class VerticalCraftPresentationTests
{
    [Fact]
    public Task SameHandAndFreshnessChangesKeepDetachedStepControls() => Sta(() =>
    {
        var fixture = new Fixture();
        var first = fixture.Steps()[0];
        var revision = fixture.Model.BrowsingRevision;
        var header = (DependencyObject)first.Header.GetType().GetProperty("Content")!.GetValue(first.Header)!;
        string[] HeaderText() => Descendants(header).OfType<TextBlock>().Select(text => text.Text).ToArray();
        var freshHeader = HeaderText();
        Assert.True(first.IsExpanded);
        first.IsExpanded = false;

        fixture.Model.InvalidateReference();
        fixture.Render();
        Assert.Same(first, fixture.Steps()[0]);
        Assert.False(first.IsExpanded);
        Assert.False(fixture.Model.Snapshot.IsCurrent);
        var staleHeader = HeaderText();
        Assert.NotEqual(freshHeader, staleHeader);

        fixture.Update(chopperCount: 0);
        Assert.Equal(revision, fixture.Model.BrowsingRevision);
        Assert.Same(first, fixture.Steps()[0]);
        Assert.False(first.IsExpanded);
        Assert.True(fixture.Model.Snapshot.IsCurrent);
        Assert.Equal(freshHeader, HeaderText());
    });

    [Fact]
    public Task StepChoicesSurviveChangedInventoryAndResetInNewSession() => Sta(() =>
    {
        var fixture = new Fixture();
        var before = fixture.Steps();
        Assert.True(before.Length >= 3);
        Assert.True(before[0].IsExpanded);
        Assert.False(before[1].IsExpanded);
        before[0].IsExpanded = false;
        before[1].IsExpanded = true;
        var firstId = AutomationProperties.GetAutomationId(before[0]);
        var secondId = AutomationProperties.GetAutomationId(before[1]);
        var revision = fixture.Model.BrowsingRevision;

        fixture.Update(chopperCount: 1);
        Assert.True(fixture.Model.BrowsingRevision > revision);
        var changed = fixture.Steps();
        var first = changed.Single(step => AutomationProperties.GetAutomationId(step) == firstId);
        var second = changed.Single(step => AutomationProperties.GetAutomationId(step) == secondId);
        Assert.NotSame(before[0], first);
        Assert.False(first.IsExpanded);
        Assert.True(second.IsExpanded);

        var session = fixture.Model.SessionRevision;
        fixture.Model.InvalidateReference(resetContext: true);
        fixture.Render();
        fixture.Update(chopperCount: 1, generation: 2);
        fixture.Model.Select(fixture.GoalId);
        fixture.Render();
        Assert.True(fixture.Model.SessionRevision > session);
        var reset = fixture.Steps();
        Assert.True(reset[0].IsExpanded);
        Assert.All(reset.Skip(1), step => Assert.False(step.IsExpanded));
    });

    [Fact]
    public Task DetachedWorkspaceKeepsHostContentRowWhenInventoryRendersAgain() => Sta(() =>
    {
        var fixture = new Fixture();
        var host = new Grid();
        host.RowDefinitions.Add(new() { Height = new GridLength(36) });
        host.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new() { Height = new GridLength(16) });
        Grid.SetRow(fixture.Workspace, 1);
        host.Children.Add(fixture.Workspace);

        fixture.Update(chopperCount: 1);
        host.Measure(new Size(320, 440));
        host.Arrange(new Rect(0, 0, 320, 440));
        host.UpdateLayout();

        // The view must leave host placement to the detached window, including after a real re-render.
        Assert.Equal(1, Grid.GetRow(fixture.Workspace));
        Assert.True(fixture.Workspace.ActualHeight >= 380);
        var scroll = Descendants(fixture.Workspace).OfType<ScrollViewer>()
            .Single(view => AutomationProperties.GetAutomationId(view) == "normal-craft-scroll");
        Assert.True(scroll.ActualHeight > 100);
    });

    [Fact]
    public Task ManyMissingMaterialsHaveARealViewportAndScrollToTheEndAtMinimumHeight() => Sta(() =>
    {
        var fixture = new Fixture();
        fixture.SelectGoalWithManyMissingMaterials();
        var resources = ProductionResources();
        var host = new Window();
        var window = new NormalCraftWindow(host, host, fixture.Workspace, runtimeEffects: false)
            { Resources = resources };
        try
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(320, 260));
            root.Arrange(new Rect(0, 0, 320, 260));
            root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            root.UpdateLayout();
            Assert.True(window.AllowsTransparency);
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)window.Background).Color);
            var clip = Assert.IsType<RectangleGeometry>(root.Clip);
            Assert.Equal(new Rect(0, 0, 320, 260), clip.Rect);
            Assert.Equal(OverlayTheme.ChromeRadius, clip.RadiusX);
            Assert.Equal(OverlayTheme.ChromeRadius, clip.RadiusY);
            var shellOutline = Assert.Single(((Grid)root).Children.OfType<Border>().Where(border =>
                border.BorderThickness == new Thickness(1) && border.CornerRadius == new CornerRadius(OverlayTheme.ChromeRadius)));
            Assert.Same(OverlayTheme.PlanLine, shellOutline.BorderBrush);
            Assert.Equal(3, Grid.GetRowSpan(shellOutline));
            var scrolls = VisualDescendants(fixture.Workspace).OfType<ScrollViewer>().ToArray();
            var missing = scrolls.Single(view => AutomationProperties.GetAutomationId(view) == "normal-missing-scroll");
            var steps = scrolls.Single(view => AutomationProperties.GetAutomationId(view) == "normal-craft-scroll");
            Assert.Single(VisualDescendants(missing).OfType<ScrollContentPresenter>());

            Assert.True(missing.ViewportHeight > 0,
                $"Missing list: actual={missing.ActualHeight}, viewport={missing.ViewportHeight}, extent={missing.ExtentHeight}, visibility={missing.Visibility}; workspace={fixture.Workspace.ActualHeight}.");
            Assert.True(missing.ExtentHeight > missing.ViewportHeight);
            Assert.True(steps.ViewportHeight > 0);
            missing.ScrollToEnd();
            root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            root.UpdateLayout();
            Assert.True(missing.VerticalOffset > 0);
            Assert.InRange(missing.ScrollableHeight - missing.VerticalOffset, 0, 1);
        }
        finally { window.CloseForApplication(); host.Close(); }
    });

    private static ResourceDictionary ProductionResources()
    {
        // Load the same production control templates without constructing a process-global Application on a test STA.
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var app = XDocument.Parse(StatsCompactBLayoutTests.Source("App.xaml"));
        var dictionary = new XElement(wpf + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            new XAttribute(XNamespace.Xmlns + "local", "clr-namespace:OrandOverlay;assembly=OrandOverlay"),
            app.Root!.Element(wpf + "Application.Resources")!.Nodes());
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in VisualDescendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private sealed class Fixture
    {
        private readonly DataCatalog _catalog = new();
        private long _revision;
        internal readonly NormalCandidateBrowser Model;
        internal readonly NormalCandidateView View;
        internal readonly FrameworkElement Workspace;
        internal readonly string GoalId;

        internal Fixture()
        {
            _catalog.Load(mapVersion: "2.320");
            GoalId = _catalog.AllUnits.Single(unit => unit.Rawcodes.Contains("210h")).Id;
            Model = new NormalCandidateBrowser(_catalog.AllUnits) { ReferencePresentationIsValid = () => true };
            View = new NormalCandidateView { IsMainWorkspace = true };
            View.SetCraftPlanner(new NormalCraftPlanner(_catalog));
            Workspace = View.DetachCraftWorkspace(renderCraft: true);
            Update(chopperCount: 0);
            Model.Select(GoalId);
            Render();
        }

        internal void Update(int chopperCount, long generation = 1)
        {
            var start = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero).AddSeconds(++_revision);
            var units = new List<InventoryEntry>
            {
                new() { UnitId = UnitId("300h"), Count = 1 },
                new() { UnitId = UnitId("700h"), Count = 1 }
            };
            if (chopperCount > 0) units.Add(new() { UnitId = UnitId("800h"), Count = chopperCount });
            var observation = DiagnosticInventoryObservation.Create(_catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                new string('A', 64), _revision, 0, start, start.AddMilliseconds(10), TimeSpan.FromMilliseconds(10),
                units, [], 1, new string('C', 64), new string('B', 64));
            Assert.True(observation.Availability == DiagnosticInventoryAvailability.Ready, observation.Reason);
            Model.UpdateReference(observation, generation);
            Render();
        }

        internal void Render()
        {
            View.SetModel(Model);
            Layout(new Size(326, 386));
        }

        internal void Layout(Size size)
        {
            foreach (var control in Descendants(Workspace).OfType<Control>()) control.ApplyTemplate();
            Workspace.Measure(size);
            Workspace.Arrange(new Rect(new Point(), size));
            Workspace.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Workspace.UpdateLayout();
        }

        private string UnitId(string rawcode) => _catalog.AllUnits.Single(unit => unit.Rawcodes.Contains(rawcode)).Id;

        internal Expander[] Steps() => Descendants(Workspace).OfType<Expander>()
            .Where(step => AutomationProperties.GetAutomationId(step).StartsWith("normal-craft-step-toggle-", StringComparison.Ordinal)).ToArray();

        internal void SelectGoalWithManyMissingMaterials()
        {
            var planner = new NormalCraftPlanner(_catalog);
            var goal = _catalog.AllUnits.First(unit =>
                planner.Build(unit.Id, Model.CurrentInventory).MissingMaterials.Count >= 8);
            Model.Select(goal.Id);
            Render();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var node in Descendants(child)) yield return node;
    }

    private static async Task Sta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception error) { done.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
