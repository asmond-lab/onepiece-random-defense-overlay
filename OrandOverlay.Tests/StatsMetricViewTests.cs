using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StatsMetricViewTests
{
    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 1, true)]
    [InlineData(0.99995, 1, true)]
    [InlineData(0.9998, 1, false)]
    public Task ThresholdUsesExistingBoundaryAndRawValues(double current, double target, bool met) => Sta(() =>
    {
        var metric = new StatsMetricView("metric", current, target, "");
        metric.SetTargetsKnown(true);
        Assert.Equal(current, metric.Current);
        Assert.Equal(target, metric.Target);
        Assert.Equal(met, metric.Current + 0.0001 >= metric.Target);
        Assert.Equal(met, Descendants(metric).OfType<Border>()
            .Single(item => AutomationProperties.GetAutomationId(item) == "stats-priority-line").Visibility == Visibility.Collapsed);
        Assert.Matches("[가-힣]", AutomationProperties.GetItemStatus(metric));
        var values = Descendants(metric).OfType<TextBlock>().ToArray();
        Assert.Equal(OverlayTheme.Num(current), AutomationProperties.GetItemStatus(
            Assert.Single(values, item => AutomationProperties.GetAutomationId(item) == "stats-current")));
        Assert.Equal(OverlayTheme.Num(target), AutomationProperties.GetItemStatus(
            Assert.Single(values, item => AutomationProperties.GetAutomationId(item) == "stats-target")));
    });

    [Fact]
    public Task UnknownTargetDoesNotReplaceKnownZeroOrClaimThresholdSuccess() => Sta(() =>
    {
        var metric = new StatsMetricView("metric", 0, 0, "");
        metric.SetTargetsKnown(false);
        Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(metric));
        Assert.Equal(0, metric.Current);
        Assert.Equal(0, metric.Target);
        var target = Descendants(metric).OfType<TextBlock>().Single(item => AutomationProperties.GetAutomationId(item) == "stats-target");
        Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(target));
        metric.SetTargetsKnown(true);
        Assert.DoesNotContain("met", AutomationProperties.GetItemStatus(metric));
    });

    [Theory]
    [InlineData(0, true, false, "부분합")]
    [InlineData(1.25, true, false, "부분합")]
    [InlineData(0, false, true, "참고")]
    [InlineData(1.25, false, true, "참고")]
    public Task PartialAndReferenceValuesRemainVisibleWithoutTargetAuthority(double current, bool partial, bool reference, string label) => Sta(() =>
    {
        var metric = new StatsMetricView("metric", current, 0, "");
        metric.SetObservationKnown(true, partial, reference);
        metric.SetTargetsKnown(true);
        Assert.False(metric.TargetsKnown);
        Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(metric));
        var elements = Descendants(metric).ToArray();
        var value = elements.OfType<TextBlock>().Single(item => AutomationProperties.GetAutomationId(item) == "stats-current");
        Assert.Equal(OverlayTheme.Num(current), value.Text);
        Assert.NotEmpty(AutomationProperties.GetHelpText(value));
        var pill = elements.OfType<Border>().Single(item => AutomationProperties.GetAutomationId(item) == "stats-status-pill");
        Assert.Equal(label, ((TextBlock)pill.Child).Text);
        Assert.Equal(Visibility.Collapsed, pill.Visibility);
        var target = elements.OfType<TextBlock>().Single(item => AutomationProperties.GetAutomationId(item) == "stats-target");
        Assert.Equal(Visibility.Collapsed, target.Visibility);
        var progress = elements.OfType<Border>().Single(item => AutomationProperties.GetAutomationId(item) == "stats-progress");
        Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(progress));
        Assert.Equal(Visibility.Collapsed, ((Border)progress.Child).Visibility);
        Assert.Equal(Visibility.Collapsed, progress.Visibility);
        metric.Measure(new Size(96, double.PositiveInfinity));
        Assert.InRange(metric.DesiredSize.Height, 60, 80);
        metric.SetObservationKnown(false, partial, reference);
        Assert.Equal("?", value.Text);
        metric.SetObservationKnown(true);
        metric.SetTargetsKnown(true);
        Assert.True(metric.TargetsKnown);
        Assert.DoesNotContain("met", AutomationProperties.GetItemStatus(metric));
        Assert.Equal(Visibility.Visible, pill.Visibility);
        Assert.Equal(Visibility.Visible, target.Visibility);
    });

    [Fact]
    public Task PromotedSupportValueWithoutThresholdNeverClaimsZeroTargetMet() => Sta(() =>
    {
        var metric = new StatsMetricView("마젠", 2, double.NaN, "표기 합계 참고");
        metric.SetObservationKnown(true);
        metric.SetTargetsKnown(true);
        Assert.False(metric.TargetsKnown);
        Assert.DoesNotContain("unknown", AutomationProperties.GetItemStatus(metric));
        var elements = Descendants(metric).ToArray();
        Assert.Equal("2", elements.OfType<TextBlock>().Single(item => AutomationProperties.GetAutomationId(item) == "stats-current").Text);
        Assert.Equal(Visibility.Collapsed, elements.OfType<TextBlock>().Single(item => AutomationProperties.GetAutomationId(item) == "stats-target").Visibility);
    });

    private static async Task Sta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); done.SetResult(); } catch (Exception error) { done.SetException(error); } })
            { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
