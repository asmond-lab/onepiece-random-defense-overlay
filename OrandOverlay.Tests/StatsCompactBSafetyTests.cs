using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;

namespace OrandOverlay.Tests;

// Presentation only: never show a window or start recognition.
public sealed class StatsCompactBSafetyTests
{
    [Fact]
    public Task WaitingAndStaleObservationsHideTotalsAndJudgments() => Sta(() =>
    {
        var window = new StatsOverlayWindow();
        try
        {
            var scroll = (ScrollViewer)window.FindName("StatsScroll");
            var totals = (StackPanel)window.FindName("CurrentStatsPanel");
            var primary = (UniformGrid)window.FindName("CoreKpiPanel");
            var metric = new StatsMetricView("스턴", 1, 1, "");
            primary.Children.Add(metric);
            Assert.Equal(Visibility.Collapsed, scroll.Visibility);
            window.SetStatSource(Stats());
            window.SetReadiness("known support", true);
            window.SetObservation(Decision("start"), Frame(true));
            Assert.False(window.HasCurrentObservation);
            Assert.Equal(Visibility.Collapsed, totals.Visibility);
            Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(metric));
            window.SetObservation(Decision("current"), Frame(true));
            Assert.True(window.HasCurrentObservation);
            Assert.True(window.TargetsKnown);
            Assert.DoesNotContain("met", AutomationProperties.GetItemStatus(metric));
            window.SetObservation(Decision("current"), Frame(false));
            Assert.False(window.TargetsKnown);
            Assert.Equal(Visibility.Collapsed, totals.Visibility);
            Assert.Equal(Visibility.Collapsed, scroll.Visibility);
            Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(Current(metric)));
            Assert.Equal("?", Current(metric).Text);
            Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("ReadinessSummaryText")).Visibility);
            window.SetObservation(Decision("current"), Frame(true));
            Assert.DoesNotContain("met", AutomationProperties.GetItemStatus(metric));
        }
        finally { window.CloseForApplication(); }
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1.25)]
    public Task UnknownSourceNumbersAnd2320ReferenceShowPartialTotalsWithoutTargets(double subtotal) => Sta(() =>
    {
        var window = new StatsOverlayWindow();
        try
        {
            var metric = new StatsMetricView("스턴", subtotal, 0, "");
            ((UniformGrid)window.FindName("CoreKpiPanel")).Children.Add(metric);
            var supportValue = new TextBlock();
            var supportRow = new Border { Child = supportValue };
            window.RegisterStatValue(supportRow, supportValue, OverlayTheme.Num(subtotal));
            window.SetStatSource(Stats() with { UnknownValueUnitCount = 3 });
            window.SetObservation(Decision("current"), Frame(true));
            Assert.False(window.TargetsKnown);
            Assert.Equal(OverlayTheme.Num(subtotal), Current(metric).Text);
            Assert.Equal(Visibility.Visible, ((StackPanel)window.FindName("CurrentStatsPanel")).Visibility);
            Assert.Equal(OverlayTheme.Num(subtotal), supportValue.Text);
            Assert.NotEmpty(AutomationProperties.GetHelpText(supportValue));
            Assert.False(metric.TargetsKnown);
            Assert.Equal(Visibility.Visible, ((TextBlock)window.FindName("StatsFooterText")).Visibility);
            var progress = Descendants(metric).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "stats-progress");
            Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(progress));
            Assert.Equal(Visibility.Collapsed, ((Border)progress.Child).Visibility);
            window.SetStatSource(Stats() with { IsLegacyReferenceForSelectedMap = true,
                ProfileStatus = "reference-only: unverified for 2.322" });
            Assert.False(window.TargetsKnown);
            Assert.DoesNotContain("reference-only", AutomationProperties.GetItemStatus(
                (TextBlock)window.FindName("SourceEvidenceText")));
            Assert.Matches("[가-힣]", AutomationProperties.GetItemStatus(
                (TextBlock)window.FindName("SourceEvidenceText")));
            Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(metric));
            Assert.Equal(((TextBlock)window.FindName("SourceDetailsText")).Text,
                ((TextBlock)window.FindName("SourceEvidenceText")).ToolTip);
        }
        finally { window.CloseForApplication(); }
    });

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public Task NonFiniteValueCannotBecomeZeroOrTargetMet(double value) => Sta(() =>
    {
        var metric = new StatsMetricView("스턴", value, 0, "");
        metric.SetTargetsKnown(true);
        Assert.False(metric.TargetsKnown);
        Assert.Equal("?", Current(metric).Text);
        Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(metric));
        metric.SetObservationKnown(true, partialValue: true, referenceValue: true);
        metric.SetTargetsKnown(true);
        Assert.False(metric.TargetsKnown);
        Assert.Equal("?", Current(metric).Text);
    });

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public Task ThreeColumnsFitAtResponsiveScales(double scale) => Sta(() =>
    {
        var panel = new UniformGrid { Columns = 3, Rows = 1 };
        foreach (var label in new[] { "스턴", "이감", "방깎" })
            panel.Children.Add(new StatsMetricView(label, 123456.78, 123456.78, "조건은 펼친 상세에 표시"));
        var width = 298 * scale;
        panel.Measure(new Size(width, 300));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        Assert.All(panel.Children.OfType<StatsMetricView>(), metric =>
        {
            Assert.InRange(metric.ActualWidth, 0, width / 3 + 0.01);
            Assert.IsType<Viewbox>(Descendants(metric).Single(d => d is Viewbox));
        });
    });

    [Fact]
    public Task ProgressAndMintGapRequireAnActualKnownTarget() => Sta(() =>
    {
        var metric = new StatsMetricView("스턴", 0.5, 1, "표기 합계");
        var progress = Descendants(metric).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "stats-progress");
        var fill = (Border)progress.Child;
        var priority = Descendants(metric).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "stats-priority-line");
        Assert.Equal(Visibility.Collapsed, priority.Visibility);
        Assert.Equal(Visibility.Collapsed, fill.Visibility);
        metric.SetTargetsKnown(true);
        Assert.DoesNotContain("below", AutomationProperties.GetItemStatus(metric));
        Assert.Equal(OverlayTheme.Num(0.5), AutomationProperties.GetItemStatus(progress));
        Assert.Equal(Visibility.Visible, priority.Visibility);
        Assert.Equal(Visibility.Visible, fill.Visibility);
        metric.SetObservationKnown(false);
        Assert.Equal(Visibility.Collapsed, priority.Visibility);
        Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(metric));
        Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(progress));
        Assert.Equal(Visibility.Collapsed, fill.Visibility);
        var pill = Descendants(metric).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "stats-status-pill");
        Assert.Equal("미확인", ((TextBlock)pill.Child).Text);
        Assert.Equal("?", Current(metric).Text);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(0.9)]
    public Task DiagnosticReferenceShowsCombatRowsWithoutCoachAuthorityAndClearsOnLoss(double subtotal) => Sta(() =>
    {
        var catalog = new DataCatalog(); catalog.Load(mapVersion: "2.320");
        var now = DateTimeOffset.UtcNow;
        var observation = DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, new string('A', 64), 1, 0, now.AddMilliseconds(-10), now,
            TimeSpan.FromMilliseconds(10), [new() { UnitId = "rawcode:I10h", Count = 2 }], new string('C', 64), new string('B', 64));
        var window = new StatsOverlayWindow();
        var current = true;
        try
        {
            var metric = new StatsMetricView("스턴", subtotal, 1.4, "표기 합계 참고");
            ((UniformGrid)window.FindName("CoreKpiPanel")).Children.Add(metric);
            var value = new TextBlock(); var row = new Border { Child = value };
            window.RegisterStatValue(row, value, "2");
            window.SetDiagnosticReference(observation, () => current);
            window.SetStatSource(Stats() with { IsLegacyReferenceForSelectedMap = true });
            Assert.Equal(Visibility.Visible, ((ScrollViewer)window.FindName("StatsScroll")).Visibility);
            Assert.Equal(OverlayTheme.Num(subtotal), Current(metric).Text);
            Assert.Equal("2", value.Text);
            Assert.False(window.HasCurrentObservation);
            Assert.False(window.TargetsKnown);
            Assert.False(metric.TargetsKnown);
            Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(metric));
            Assert.Equal(((TextBlock)window.FindName("SourceDetailsText")).Text,
                ((TextBlock)window.FindName("SourceEvidenceText")).ToolTip);
            window.SetStatSource(Stats() with { UnknownValueUnitCount = 1, IsLegacyReferenceForSelectedMap = true });
            Assert.Equal(OverlayTheme.Num(subtotal), Current(metric).Text);
            Assert.Equal("2", value.Text);
            Assert.False(metric.TargetsKnown);
            Assert.False(window.TargetsKnown);
            window.SetStatSource(Stats() with { IsLegacyReferenceForSelectedMap = true });
            Assert.Equal(OverlayTheme.Num(subtotal), Current(metric).Text);
            current = false; window.SetDiagnosticReference(observation, () => current);
            Assert.Equal(Visibility.Visible, ((ScrollViewer)window.FindName("StatsScroll")).Visibility);
            Assert.DoesNotContain("diagnostic-reference-unverified", AutomationProperties.GetItemStatus(
                (TextBlock)window.FindName("ObservationText")));
            Assert.Matches("[가-힣]", AutomationProperties.GetItemStatus(
                (TextBlock)window.FindName("ObservationText")));
            Assert.False(window.HasCurrentObservation);
            Assert.False(window.TargetsKnown);
            window.ClearDiagnosticReference();
            Assert.Equal(Visibility.Collapsed, ((ScrollViewer)window.FindName("StatsScroll")).Visibility);
            Assert.Equal("?", Current(metric).Text);
            Assert.Equal("?", value.Text);
            Assert.False(window.HasCurrentObservation);
            Assert.False(window.TargetsKnown);
            Assert.Equal(Visibility.Collapsed, ((ScrollViewer)window.FindName("StatsScroll")).Visibility);
        }
        finally { window.CloseForApplication(); }
    });

    private static InventoryStatSummary Stats() => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static CoachDecision Decision(string id) => new(CoachActionKind.Waiting, id, "", "", "", "", "");
    private static CoachFrame Frame(bool current) => new()
    {
        MatchGeneration = 1, Revision = 1, Round = 1, CompletedStoryStage = 0, IsCurrent = current,
        Inventory = ImmutableDictionary<string, int>.Empty, Difficulty = "신", GoalId = "test-goal"
    };
    private static TextBlock Current(StatsMetricView metric) => Descendants(metric).OfType<TextBlock>()
        .Single(t => AutomationProperties.GetAutomationId(t) == "stats-current");
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static async Task Sta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); done.SetResult(); } catch (Exception e) { done.SetException(e); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
