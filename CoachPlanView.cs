using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OrandOverlay;

public sealed partial class CoachPlanView : StackPanel
{
    private readonly HashSet<string> _expandedBranches = [];
    private string? _goalId;
    public void Render(CoachPlanProjection plan, bool full)
    {
        if (_goalId != plan.GoalId || !plan.IsCurrent) _expandedBranches.Clear();
        _goalId = plan.GoalId;
        Children.Clear();
        if (full) { RenderMain(plan); return; }
        Children.Add(Label(plan.GoalName, "plan-goal", OverlayTheme.PlanHeadingSize));
        if (!plan.IsCurrent)
        {
            Children.Add(Label("현재 패가 확인되면 목표와 필요한 재료를 보여 줍니다.", "plan-unconfirmed"));
            return;
        }
        var metrics = new UniformGrid { Columns = 4, Margin = OverlayTheme.CoachSectionSpacing };
        metrics.Children.Add(Metric("현재 라운드", plan.Round?.ToString() ?? "미확인", "plan-round"));
        metrics.Children.Add(Metric("스토리 완료", plan.Story?.ToString() ?? "미확인", "plan-story"));
        metrics.Children.Add(Metric("목표 재료 보유", plan.Components.Count == 0 ? "미확인" : $"{plan.Components.Count(item => item.IsOwned)} / {plan.Components.Count}", "plan-owned"));
        metrics.Children.Add(Metric("선택위습 보관", plan.SelectionWisps?.ToString() ?? "미확인", "plan-wisps"));
        if (full) Children.Add(metrics);
        else Children.Add(Label($"{plan.Round}라 · 스토리 {plan.Story} 완료 · 선택위습 {plan.SelectionWisps}개", "plan-observation"));
        if (full) Children.Add(Label("목표까지의 조합 순서", "plan-route-heading", OverlayTheme.PlanHeadingSize));
        if (full)
        {
            foreach (var component in plan.Components) Children.Add(Component(component, true));
            Children.Add(new Border { Background = OverlayTheme.PlanSurface, BorderBrush = OverlayTheme.OutlineBrush,
                BorderThickness = OverlayTheme.WellBorderThickness, CornerRadius = OverlayTheme.WellCornerRadius,
                Padding = OverlayTheme.CoachPanelPadding, Child = Label("최종 목표 · " + plan.GoalName, "plan-final-goal", OverlayTheme.PlanHeadingSize) });
        }
        else
        {
            var strip = new UniformGrid { Columns = Math.Max(1, Math.Min(3, plan.Components.Count)) };
            foreach (var component in plan.Components) strip.Children.Add(Component(component, false));
            Children.Add(strip);
        }
    }

    private UIElement Component(CoachPlanComponent item, bool full)
    {
        var content = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = UnitImageFactory.Create(item.Image, item.Name,
            full ? OverlayTheme.CoachIconSize : OverlayTheme.ControlMinHitHeight, item.UnitId);
        icon.Margin = OverlayTheme.PlanIconGap;
        row.Children.Add(icon);
        var words = new StackPanel();
        words.Children.Add(Label(item.Name, "plan-component-name", full ? OverlayTheme.CoachBodyTypeSize : OverlayTheme.CoachMetaTypeSize));
        var status = Label(item.OwnedCount is { } count
            ? $"보유 {count} / {item.RequiredCount}" + (item.IsOwned ? " · 준비됨" : $" · {item.RemainingCount} 남음")
            : "보유 미확인", "plan-component-count");
        status.Foreground = item.IsOwned ? OverlayTheme.PlanSuccess : OverlayTheme.PlanWarning;
        words.Children.Add(status);
        if (full) { Grid.SetColumn(words, 1); row.Children.Add(words); content.Children.Add(row); }
        else { row.Children.Remove(icon); content.Children.Add(icon); content.Children.Add(words); }
        var shell = new Border
        {
            Background = OverlayTheme.PlanRaised, BorderBrush = OverlayTheme.PlanLine,
            BorderThickness = OverlayTheme.WellBorderThickness, CornerRadius = OverlayTheme.WellCornerRadius,
            Padding = full ? OverlayTheme.CoachPanelPadding : OverlayTheme.PlannerBlockPadding,
            Margin = OverlayTheme.PlanTileGap, Child = content
        };
        AutomationProperties.SetAutomationId(shell, "plan-component:" + item.UnitId);
        AutomationProperties.SetItemStatus(shell, item.OwnedCount is { } owned ? $"{owned}/{item.RequiredCount}" : "unknown");
        if (!full || item.Children.Count == 0) return shell;
        var group = new StackPanel();
        group.Children.Add(shell);
        var children = new StackPanel { Margin = OverlayTheme.PlanDetailInset };
        foreach (var child in item.Children) children.Children.Add(Component(child, true));
        var branch = new Expander { Header = "필요한 재료 · " + item.Name,
            Foreground = OverlayTheme.PlanSecondary, Content = children,
            IsExpanded = _expandedBranches.Contains(item.UnitId), Margin = OverlayTheme.CoachSectionSpacing };
        AutomationProperties.SetAutomationId(branch, "plan-branch:" + item.UnitId);
        branch.Expanded += (_, e) => { if (e.OriginalSource == branch) _expandedBranches.Add(item.UnitId); };
        branch.Collapsed += (_, e) => { if (e.OriginalSource == branch) _expandedBranches.Remove(item.UnitId); };
        group.Children.Add(branch);
        return group;
    }

    private static UIElement Metric(string label, string value, string id)
    {
        var body = new StackPanel();
        body.Children.Add(Label(label, id + "-label"));
        body.Children.Add(Label(value, id, OverlayTheme.PlanTitleSize));
        return new Border { Background = OverlayTheme.PlanRaised, Padding = OverlayTheme.CoachPanelPadding,
            CornerRadius = OverlayTheme.WellCornerRadius, Margin = OverlayTheme.PlanTileGap, Child = body };
    }

    private static TextBlock Label(string text, string id, double size = OverlayTheme.CoachMetaTypeSize)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
            FontSize = size, Foreground = size > OverlayTheme.CoachMetaTypeSize ? OverlayTheme.PlanText : OverlayTheme.PlanSecondary,
            FontWeight = size > OverlayTheme.CoachMetaTypeSize ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = OverlayTheme.PlannerBlockMargin };
        AutomationProperties.SetAutomationId(block, id);
        AutomationProperties.SetName(block, text);
        AutomationProperties.SetItemStatus(block, text);
        return block;
    }
}
