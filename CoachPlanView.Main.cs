using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrandOverlay;

public sealed partial class CoachPlanView
{
    internal static TextBlock MainText(string text, double size = MainPlanTheme.BodySize, Brush? brush = null) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        LineHeight = size * MainPlanTheme.LineHeightFactor, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        Foreground = brush ?? OverlayTheme.PlanText,
        FontWeight = size >= MainPlanTheme.HeadingSize ? FontWeights.Bold : FontWeights.Normal
    };

    internal static Border MainPanel(UIElement child) => new()
    {
        Background = MainPlanTheme.Panel, BorderBrush = OverlayTheme.PlanLine,
        BorderThickness = OverlayTheme.WellBorderThickness, CornerRadius = OverlayTheme.WellCornerRadius,
        Padding = MainPlanTheme.PanelInset, Child = child
    };

    internal static UIElement MainMetrics(CoachPlanProjection plan)
    {
        var grid = new Grid { Margin = MainPlanTheme.SectionGap };
        for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new()
            { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(MainPlanTheme.MetricGap.Right) });
        var labels = new[] { "현재 라운드", "스토리", plan.GoalName + " 재료", "선택위습" };
        var values = new[] { plan.Round?.ToString() ?? "미확인", plan.Story?.ToString() ?? "미확인",
            plan.Components.Count == 0 ? "미확인" : $"{plan.Components.Count(item => item.IsOwned)} / {plan.Components.Count}", plan.SelectionWisps?.ToString() ?? "미확인" };
        var units = new[] { "라운드", "완료", "보유", "개 보관" };
        var ids = new[] { "plan-round", "plan-story", "plan-owned", "plan-wisps" };
        for (var i = 0; i < 4; i++)
        {
            var body = new StackPanel();
            body.Children.Add(MainText(labels[i], MainPlanTheme.MetaSize, OverlayTheme.PlanSecondary));
            var number = new StackPanel { Orientation = Orientation.Horizontal };
            var value = MainText(values[i], MainPlanTheme.MetricSize);
            AutomationProperties.SetAutomationId(value, ids[i]); AutomationProperties.SetItemStatus(value, values[i]);
            number.Children.Add(value);
            number.Children.Add(new TextBlock { Text = units[i], FontSize = MainPlanTheme.MetaSize,
                Foreground = OverlayTheme.PlanSecondary, Margin = MainPlanTheme.NumberUnitGap, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(number);
            var card = new Border { Background = OverlayTheme.PlanRaised, BorderBrush = OverlayTheme.PlanLine,
                BorderThickness = OverlayTheme.WellBorderThickness, CornerRadius = MainPlanTheme.CardRadius,
                Padding = MainPlanTheme.CompactInset,
                Child = body };
            AutomationProperties.SetAutomationId(card, "main-metric-tile:" + ids[i]);
            Grid.SetColumn(card, i * 2); grid.Children.Add(card);
        }
        AutomationProperties.SetAutomationId(grid, "main-plan-metrics");
        return grid;
    }

    private void RenderMain(CoachPlanProjection plan)
    {
        var body = new StackPanel();
        var heading = new Grid { Margin = MainPlanTheme.PanelHeaderGap };
        heading.Children.Add(MainText(plan.GoalName + "까지의 조합 순서", MainPlanTheme.HeadingSize));
        body.Children.Add(heading);
        foreach (var component in plan.Components) body.Children.Add(MainComponent(component));
        var missing = plan.Components.FirstOrDefault(item => !item.IsOwned);
        if (missing is not null)
        {
            var path = new StackPanel();
            var current = plan.MainPath.FirstOrDefault(item => item.UnitId != missing.UnitId) ?? missing.Children.FirstOrDefault();
            if (current is not null) path.Children.Add(MainPathRow(current,
                plan.CraftProgress is { } progress && progress.UnitId == current.UnitId
                    ? $"추가 {progress.RequiredCount}개 중 {progress.CompletedCount}개 완료" : $"보유 {current.OwnedCount} / {current.RequiredCount}"));
            var details = new StackPanel();
            foreach (var child in missing.Children) details.Children.Add(Component(child, true));
            var disclosure = new Expander { Header = MainPathRow(missing, string.Join(" · ", missing.Children.Select(item => $"{item.Name} {item.RequiredCount}"))), Content = details,
                Foreground = OverlayTheme.PlanSecondary, IsExpanded = _expandedBranches.Contains(missing.UnitId) };
            AutomationProperties.SetAutomationId(disclosure, "plan-branch:" + missing.UnitId);
            disclosure.Expanded += (_, e) => { if (e.OriginalSource == disclosure) _expandedBranches.Add(missing.UnitId); };
            disclosure.Collapsed += (_, e) => { if (e.OriginalSource == disclosure) _expandedBranches.Remove(missing.UnitId); };
            path.Children.Add(disclosure);
            body.Children.Add(new Border { BorderBrush = MainPlanTheme.PathLine, BorderThickness = MainPlanTheme.LeftLine,
                Margin = MainPlanTheme.PathMargin, Padding = MainPlanTheme.PathInset, Child = path });
        }
        var goal = (Grid)MainPathRow(new(plan.GoalId ?? "", plan.MainGoalLabel, plan.GoalImage, 1, null, []),
            plan.Components.Count == 0 ? "재료 계획 확인 대기" : $"목표 재료 {plan.Components.Count(item => item.IsOwned)} / {plan.Components.Count} 보유", MainPlanTheme.GoalIconSize);
        body.Children.Add(new Border { Background = MainPlanTheme.Goal, BorderBrush = MainPlanTheme.GoalLine,
            BorderThickness = OverlayTheme.WellBorderThickness, CornerRadius = MainPlanTheme.ButtonRadius,
            Padding = MainPlanTheme.GoalInset, Child = goal });
        var panel = MainPanel(body); AutomationProperties.SetAutomationId(panel, "main-route-panel"); Children.Add(panel);
    }

    private static UIElement MainComponent(CoachPlanComponent item)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var icon = UnitImageFactory.Create(item.Image, item.Name, MainPlanTheme.ComponentIconSize, item.UnitId);
        icon.Margin = OverlayTheme.PlanIconGap; row.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(MainText(item.Name, MainPlanTheme.HeadingSize));
        text.Children.Add(MainText($"보유 {item.OwnedCount} / {item.RequiredCount} · " +
            (item.IsOwned ? "실제 보유 확인" : "부족한 재료를 더 모으세요"), MainPlanTheme.MetaSize, OverlayTheme.PlanSecondary));
        Grid.SetColumn(text, 1); row.Children.Add(text);
        var badge = new Border { Background = item.IsOwned ? MainPlanTheme.SuccessPill : MainPlanTheme.MissingPill,
            CornerRadius = MainPlanTheme.PillRadius, Padding = MainPlanTheme.PillInset, VerticalAlignment = VerticalAlignment.Center,
            Child = MainText(item.IsOwned ? "완료" : "준비 중", MainPlanTheme.CaptionSize, item.IsOwned ? OverlayTheme.PlanSuccess : OverlayTheme.PlanWarning) };
        Grid.SetColumn(badge, 2); row.Children.Add(badge);
        var shell = new Border { Background = item.IsOwned ? OverlayTheme.PlanRaised : MainPlanTheme.Missing,
            BorderBrush = item.IsOwned ? OverlayTheme.PlanLine : MainPlanTheme.MissingLine,
            BorderThickness = OverlayTheme.WellBorderThickness, CornerRadius = MainPlanTheme.CardRadius,
            Padding = MainPlanTheme.CompactInset, Margin = MainPlanTheme.CardGap, Child = row };
        AutomationProperties.SetAutomationId(shell, "plan-component:" + item.UnitId);
        AutomationProperties.SetItemStatus(shell, $"{item.OwnedCount}/{item.RequiredCount}");
        return shell;
    }

    private static UIElement MainPathRow(CoachPlanComponent item, string subtitle, double iconSize = MainPlanTheme.PathIconSize)
    {
        var row = new Grid { Margin = MainPlanTheme.PathRowInset };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
        var icon = UnitImageFactory.Create(item.Image, item.Name, iconSize, item.UnitId);
        icon.Margin = OverlayTheme.PlanIconGap; icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon);
        var words = new StackPanel();
        var title = MainText(item.Name); title.FontWeight = FontWeights.Bold; words.Children.Add(title);
        words.Children.Add(MainText(subtitle, MainPlanTheme.CaptionSize, OverlayTheme.PlanSecondary));
        Grid.SetColumn(words, 1); row.Children.Add(words);
        return row;
    }
}
