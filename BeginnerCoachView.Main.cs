using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;

namespace OrandOverlay;

public partial class BeginnerCoachView
{
    private bool _mainPresentationReady;
    private ContentControl _mainMetrics = null!;
    private TextBlock _mainTitle = null!;
    private TextBlock _mainMode = null!;
    private TextBlock _mainState = null!;
    private TextBlock _mainActionSubtitle = null!;
    private Border _mainRecords = null!;
    private StackPanel _mainRecordRows = null!;
    private TextBlock _mainRemaining = null!;
    private TextBlock _mainKey = null!;
    private Border _mainKeycap = null!;

    private void EnsureMainPresentation()
    {
        if (_mainPresentationReady) return;
        _mainPresentationReady = true;
        var shell = (Grid)Content;
        var oldHeader = (StackPanel)ModeTitle.Parent;
        oldHeader.Visibility = Visibility.Collapsed;
        var detailContent = (StackPanel)DetailsPanel.Content;
        // Normal mode can keep this view collapsed while the main Profile pane
        // has already taken ownership of GuideChoice. Do not reparent it twice.
        if (ReferenceEquals(GuideChoice.Parent, oldHeader))
        {
            oldHeader.Children.Remove(GuideChoice);
            detailContent.Children.Insert(0, GuideChoice);
        }
        var footer = (WrapPanel)PauseButton.Parent;
        shell.Children.Remove(footer); detailContent.Children.Add(footer);

        var mainHeader = new Grid { Margin = new Thickness(), Visibility = Visibility.Collapsed };
        mainHeader.ColumnDefinitions.Add(new()); mainHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var heading = new StackPanel();
        _mainMode = CoachPlanView.MainText("", MainPlanTheme.CaptionSize, OverlayTheme.PlanAccent);
        _mainTitle = CoachPlanView.MainText("", MainPlanTheme.HeaderSize);
        _mainTitle.Margin = OverlayTheme.PlannerRowMargin;
        heading.Children.Add(_mainMode); heading.Children.Add(_mainTitle);
        mainHeader.Children.Add(heading);
        _mainState = CoachPlanView.MainText("", MainPlanTheme.MetaSize, OverlayTheme.PlanSuccess);
        var badge = new Border { BorderBrush = OverlayTheme.PlanLine, BorderThickness = OverlayTheme.WellBorderThickness,
            CornerRadius = MainPlanTheme.BadgeRadius, Padding = MainPlanTheme.BadgeInset, Child = _mainState,
            VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(badge, 1); mainHeader.Children.Add(badge);
        AutomationProperties.SetAutomationId(mainHeader, "main-plan-heading");
        _mainMetrics = new ContentControl();
        detailContent.Children.Add(_mainMetrics);
        CoachScroll.Content = null;
        var scrollContent = new StackPanel(); scrollContent.Children.Add(mainHeader);
        scrollContent.Children.Add(WorkspaceBody); CoachScroll.Content = scrollContent;

        var titleRow = (Grid)ActionText.Parent;
        var actionContents = (StackPanel)titleRow.Parent;
        var actionShell = (Border)actionContents.Parent;
        actionShell.WithOverlayBackground(OverlayChrome.WellKey); actionShell.Padding = new Thickness(20);
        actionShell.BorderThickness = new Thickness(1); actionShell.BorderBrush = RandyPickTheme.Border;
        actionShell.CornerRadius = new CornerRadius(10);
        if (actionContents.Children[0] is TextBlock actionLabel) { actionLabel.Text = "지금 할 일"; actionLabel.FontSize = 10; actionLabel.Foreground = RandyPickTheme.Accent; actionLabel.Margin = new Thickness(0,0,0,14); }
        ConfirmButton.Background = RandyPickTheme.Accent; ConfirmButton.Foreground = RandyPickTheme.Canvas; ConfirmButton.HorizontalAlignment = HorizontalAlignment.Stretch; ConfirmButton.Padding = new Thickness(14,12,14,12);
        var index = ActionBody.Children.IndexOf(actionShell); ActionBody.Children.Remove(actionShell);
        ActionBody.Children.Insert(index, new Border { BorderBrush = OverlayTheme.OutlineBrush, BorderThickness = new Thickness(),
            CornerRadius = OverlayTheme.WellCornerRadius, Child = actionShell, Margin = MainPlanTheme.SmallSectionGap });
        AutomationProperties.SetAutomationId(ActionBody.Children[index], "main-action-panel");
        actionShell.Margin = new Thickness();
        ActionText.FontSize = 23;
        titleRow.Margin = OverlayTheme.PlanTopGap;
        _mainActionSubtitle = CoachPlanView.MainText("이미 가진 재료는 제외한 수량", MainPlanTheme.MetaSize, OverlayTheme.PlanSecondary);
        titleRow.Children.Remove(ActionText);
        var titleWords = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleWords.Children.Add(ActionText); titleWords.Children.Add(_mainActionSubtitle);
        Grid.SetColumn(titleWords, 1); titleRow.Children.Add(titleWords);
        CraftCountPanel.Margin = MainPlanTheme.ActionCountGap; CraftCountText.FontSize = MainPlanTheme.BodySize;
        CraftCountPanel.Children.Remove(CraftCountText);
        var counts = new Grid { Margin = OverlayTheme.PlannerBlockMargin };
        counts.ColumnDefinitions.Add(new()); counts.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        CraftCountText.Margin = new Thickness(); counts.Children.Add(CraftCountText);
        _mainRemaining = CoachPlanView.MainText("", MainPlanTheme.BodySize, OverlayTheme.PlanAccent);
        Grid.SetColumn(_mainRemaining, 1); counts.Children.Add(_mainRemaining); CraftCountPanel.Children.Insert(0, counts);
        CraftCountBar.Height = MainPlanTheme.ProgressHeight;
        CraftRecipeText.FontSize = MainPlanTheme.RecipeSize; CraftRecipeText.Foreground = OverlayTheme.PlanSecondary;
        CraftRecipeText.Margin = MainPlanTheme.RecipeSpacing;
        var controlIndex = actionContents.Children.IndexOf(ControlsText); actionContents.Children.Remove(ControlsText);
        ControlsText.Margin = new Thickness(); ControlsText.FontSize = MainPlanTheme.RecipeSize;
        ControlsText.VerticalAlignment = VerticalAlignment.Center;
        var command = new Grid(); command.ColumnDefinitions.Add(new()); command.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        command.Children.Add(ControlsText);
        _mainKey = CoachPlanView.MainText("", MainPlanTheme.BodySize, MainPlanTheme.KeyText);
        _mainKey.FontFamily = new FontFamily("Consolas"); _mainKey.FontWeight = FontWeights.Bold;
        _mainKeycap = new Border { Background = MainPlanTheme.KeySurface, CornerRadius = MainPlanTheme.KeyRadius,
            Padding = MainPlanTheme.KeyInset, Child = _mainKey, Margin = MainPlanTheme.NumberUnitGap };
        Grid.SetColumn(_mainKeycap, 1); command.Children.Add(_mainKeycap);
        actionContents.Children.Insert(controlIndex, new Border { Background = MainPlanTheme.Command,
            CornerRadius = MainPlanTheme.ButtonRadius, Padding = MainPlanTheme.CommandInset, Margin = MainPlanTheme.CardGap, Child = command });
        ActionBody.Children.Remove(NavigationAlertText); actionContents.Children.Add(NavigationAlertText);

        // Keep the same registered controls and handlers; only main presentation moves them.
        foreach (var element in new FrameworkElement[] { NextTargetText, PreserveLabel, PreserveText,
            AlternativeText, CompletedText, BountyPreparationText })
        {
            ((Panel)element.Parent).Children.Remove(element); detailContent.Children.Add(element);
        }
        var recentParent = (Panel)RecentPanel.Parent; recentParent.Children.Remove(RecentPanel);
        _mainRecordRows = new StackPanel();
        var recentHeading = CoachPlanView.MainText("최근 기록", MainPlanTheme.HeadingSize);
        _mainRecordRows.Children.Add(new Border { Padding = MainPlanTheme.JournalRowInset,
            BorderBrush = OverlayTheme.PlanLine, BorderThickness = MainPlanTheme.BottomLine,
            Margin = MainPlanTheme.PanelHeaderGap, Child = recentHeading });
        RecentPanel.Children.Clear(); RecentPanel.Children.Add(_mainRecordRows);
        RecentPanel.Margin = new Thickness();
        _mainRecords = CoachPlanView.MainPanel(RecentPanel);
        AutomationProperties.SetAutomationId(_mainRecords, "coach-recent-events");
        _mainRecords.Margin = MainPlanTheme.SmallSectionGap;
        ActionBody.Children.Insert(1, _mainRecords);
    }

    private void RestyleMainGoalAndMaterials()
    {
        // Reuse generated plan controls and their disclosure handlers, never regenerate plan decisions.
        if (PlanView.Children.OfType<Border>().FirstOrDefault() is not { Child: StackPanel body } panel) return;
        panel.Background = Brushes.Transparent; panel.BorderThickness = new Thickness(); panel.Padding = new Thickness();
        var goal = body.Children.OfType<Border>().LastOrDefault();
        if (goal is not null)
        {
            body.Children.Remove(goal); body.Children.Insert(0, goal);
            goal.WithOverlayBackground(OverlayChrome.RaisedKey);
            goal.BorderBrush = RandyPickTheme.StrongBorder; goal.BorderThickness = new Thickness(1); goal.CornerRadius = new CornerRadius(10);
            goal.Padding = new Thickness(16); goal.Margin = new Thickness(0,0,0,12);
        }
        if (body.Children.OfType<Grid>().FirstOrDefault() is { } heading && heading.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
        { label.Text = "조합 재료"; label.FontSize = 13; heading.Margin = new Thickness(12,4,0,8); }
        var materialRows = body.Children.OfType<Border>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("plan-component:", StringComparison.Ordinal)).ToArray();
        if (materialRows.Length > 0)
        {
            var materials = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            foreach (var row in materialRows)
            {
                body.Children.Remove(row); row.Background = Brushes.Transparent; row.BorderThickness = new Thickness(); row.Padding = new Thickness(6); row.Margin = new Thickness(0);
                if (row.Child is Grid grid)
                    foreach (var words in grid.Children.OfType<StackPanel>()) foreach (var text in words.Children.OfType<TextBlock>()) text.FontSize = text == words.Children[0] ? 12 : 10;
                materials.Children.Add(row);
            }
            body.Children.Insert(goal is null ? 1 : 2, new Border { Child = materials, BorderBrush = RandyPickTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Margin = new Thickness(0,0,0,12) }.WithOverlayBackground(OverlayChrome.WellKey));
        }
        else
        {
            var pending = CoachPlanView.MainText("재료 계획 확인 대기\n확인된 목표 재료를 이곳에 표시합니다.", 12, RandyPickTheme.Muted);
            body.Children.Add(new Border { Child = pending, BorderBrush = RandyPickTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0,0,0,12) }.WithOverlayBackground(OverlayChrome.WellKey));
        }
    }

    private void RenderMainPresentation(CoachDecision decision, CoachFrame frame)
    {
        var plan = Plan!;
        _mainMode.Text = PlayModes.Options.Single(option => option.Mode == frame.Mode).Name + (frame.HasKnownDifficulty ? " · " + frame.DifficultyLabel : "");
        _mainTitle.Text = plan.MainHeading;
        _mainState.Text = !plan.IsCurrent ? "패 확인 대기" : decision.CraftProgress?.AwaitingRecognition == true ? "결과 확인 중" : "패 확인됨";
        _mainMetrics.Content = plan.IsCurrent ? CoachPlanView.MainMetrics(plan) : null;
        RestyleMainGoalAndMaterials();
        _mainActionSubtitle.Visibility = plan.CraftProgress is null ? Visibility.Collapsed : Visibility.Visible;
        if (plan.CraftProgress is { } progress)
        {
            Set(CraftCountText, $"완료 {progress.CompletedCount} / {progress.RequiredCount}");
            _mainRemaining.Text = progress.RemainingCount + "개 남음";
        }
        var step = decision.Kind == CoachActionKind.Craft && decision.CraftRecipe is not null
            ? frame.CraftSteps.FirstOrDefault(item => item.TargetUnitId == decision.TargetUnitId) : null;
        _mainKeycap.Visibility = step?.Key is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        if (step?.Key is { Length: > 0 } key && _planCatalog is not null)
        {
            Set(ControlsText, _planCatalog.Unit(step.TriggerUnitId).Name + " 선택 →");
            _mainKey.Text = key;
        }
        if (decision.CraftRecipe is { } recipe)
            Set(CraftRecipeText, "1회 재료 · " + string.Join(" + ", recipe.Ingredients.Select(item => item.Name + " " + item.RequiredCount + "개")));
        _mainRecords.Visibility = plan.IsCurrent && _notices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentPanel.Visibility = _mainRecords.Visibility;
        while (_mainRecordRows.Children.Count > 1) _mainRecordRows.Children.RemoveAt(1);
        foreach (var notice in _notices.TakeLast(3).Reverse())
            _mainRecordRows.Children.Add(new Border { Padding = MainPlanTheme.JournalRowInset,
                BorderBrush = OverlayTheme.PlanLine, BorderThickness = MainPlanTheme.BottomLine,
                Child = CoachPlanView.MainText(notice, MainPlanTheme.MetaSize) });
        if (frame.NativeNavigation.Status == NativeNavigationStatus.Selected &&
            (frame.Mode != PlayMode.Guide || frame.NativeNavigation.OptionId == BulletGuidePolicy.NavigationId))
            NavigationAlertText.Visibility = Visibility.Collapsed;
        if (Window.GetWindow(this) is MainWindow main)
            {
            main.PlanNavigationButton.Content = frame.Mode == PlayMode.Guide ? plan.GoalName + " 공략" : "조합 계획";
            main.MainObservationStatus.Text = _mainState.Text;
        }
    }
}
