using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace OrandOverlay;

public partial class BeginnerCoachView : UserControl
{
    private CoachDecision? _decision;
    private CoachFrame? _renderedFrame;
    private CoachPresentation? _presentation;
    public event Action<QueenConversionInput, long, long>? QueenConditionRequested;
    private bool _compact;
    private DataCatalog? _planCatalog;
    private long _noticeGeneration = -1;
    private readonly List<string> _notices = [];
    public bool IsPlanWorkspace { get; set; }
    public CoachPlanProjection? Plan { get; private set; }
    public void ConfigurePlanCatalog(DataCatalog catalog) => _planCatalog = catalog;
    private bool _renderingGuide;
    public event Action<int>? GuideSelectionRequested;
    public event Action? AdvancedRequested;
    public event Action<string>? ConfirmationRequested;
    public event Action? PauseRequested;
    public event Action? ReviewRequested;

    public BeginnerCoachView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ArrangeWorkspace();
        GuideChoice.ItemsSource = GuideCatalog.Options;
        NavigationChoice.ItemsSource = NavigationProfiles.Categories
            .SelectMany(category => NavigationProfiles.ForCategory(category.Id))
            .DistinctBy(option => option.Id).ToArray();
    }

    public void Render(CoachDecision decision, CoachFrame frame, UnitDefinition? unit)
    {
        var changed = _decision?.Id != decision.Id;
        _decision = decision;
        _renderedFrame = frame;
        var presentation = _presentation = CoachPresentation.Create(decision, frame);
        Plan = CoachPlanProjection.Create(decision, frame, _planCatalog);
        if (IsPlanWorkspace) EnsureMainPresentation();
        PlanView.Render(Plan, IsPlanWorkspace);
        var craft = Plan.CraftProgress;
        CraftCountPanel.Visibility = craft is null ? Visibility.Collapsed : Visibility.Visible;
        if (craft is not null)
        {
            Set(CraftCountText, $"완료 {craft.CompletedCount} / {craft.RequiredCount} · {craft.RemainingCount}개 남음" +
                (craft.AwaitingRecognition ? " · 결과 확인 중" : ""));
            CraftCountBar.Maximum = craft.RequiredCount;
            CraftCountBar.Value = craft.CompletedCount;
            AutomationProperties.SetItemStatus(CraftCountBar, $"{craft.CompletedCount}/{craft.RequiredCount}");
        }
        Set(NextTargetText, Plan.IsCurrent && !string.IsNullOrEmpty(Plan.NextTarget) ? "다음 목표 · " + Plan.NextTarget : "");
        if (_noticeGeneration != frame.MatchGeneration || presentation.IsStart)
        {
            _notices.Clear();
            _noticeGeneration = frame.MatchGeneration;
        }
        if (Plan.IsCurrent)
            foreach (var notice in new[] { decision.CompletionNotice, decision.ChangeReason }.Where(value => !string.IsNullOrEmpty(value)))
            {
                var entry = $"{frame.Round}라 · {notice}";
                if (!_notices.Contains(entry)) _notices.Add(entry);
            }
        Set(RecentText, string.Join(Environment.NewLine + Environment.NewLine, _notices.TakeLast(3).Reverse()));
        RecentPanel.Visibility = IsPlanWorkspace && _notices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ArrangeWorkspace();
        Set(StatusText, presentation.Status);
        Set(NavigationAlertText, presentation.NavigationAlert);
        var displayReason = CoachPresentation.DisplayReason(decision, frame);
        Set(DecisionDetailsText, string.Join("\n\n", new[] { decision.Title, decision.Controls,
            displayReason, decision.Confirmation }.Where(value => !string.IsNullOrEmpty(value))));
        _renderingGuide = true;
        GuideChoice.Visibility = frame.Mode == PlayMode.Guide ? Visibility.Visible : Visibility.Collapsed;
        GuideChoice.SelectedItem = GuideCatalog.Find(frame.GuideNumber);
        QueenCondition.Visibility = frame.Mode == PlayMode.Guide && frame.GuideNumber == 1 &&
            frame.Inventory.GetValueOrDefault("rawcode:HA0h") > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueenCondition.IsEnabled = frame.IsCurrent && frame.HasKnownDifficulty && !frame.Paused &&
            frame.Outcome is not ("clear" or "fail");
        QueenCondition.SelectedValue = frame.GuidePlan?.QueenInput ?? QueenConversionInput.Unknown;
        _renderingGuide = false;
        Set(ModeTitle, PlayModes.Options.Single(option => option.Mode == frame.Mode).Name);
        Set(StageText, $"{frame.DifficultyLabel} · {frame.Round}라 · {decision.GoalLabel}");
        Set(NavigationSummary, NativeNavigationPresentation.Describe(frame.NativeNavigation, frame.ConfirmedNavigation) +
            (frame.SuggestedNavigation is { } suggested ? $"\n자동 추천 · {NavigationProfiles.Find(suggested).Name}" : ""));
        Set(NavigationTiming, frame.Round < 21
            ? "항법 선택은 20라 종료 후 21라 시작 즉시 가능 · 23라까지 선택"
            : frame.Round <= 23 ? "21라 시작부터 23라까지 게임에서 직접 항법 선택"
                : "항법 선택 시간이 지났습니다. 게임에서 선택된 항법을 확인해 주세요.");
        Set(BountyPreparationText, decision.ShowBountyHunterPreparationTip
            ? "바운티헌터를 고를 예정이라면\n" +
              "안전하게 버틸 수 있을 때만 19라 몬스터를 조금 남겨둘 수 있습니다. " +
              "20라가 끝나고 21라가 시작되면 게임에서 바운티헌터를 직접 선택한 뒤 남은 몬스터를 잡으세요.\n" +
              "필수 행동이 아닙니다. 안전을 확신할 수 없거나 라인·전투가 급하면 남기지 말고 즉시 처리하세요. 추천은 실제 선택 확인이 아닙니다."
            : "");
        if (presentation.IsStart)
        {
            Set(ActionText, presentation.Title);
            Set(ControlsText, presentation.Controls);
        }
        else
        {
            Set(ActionText, CoachPresentation.ActionTitle(decision, unit));
            Set(ControlsText, decision.Controls);
        }
        Set(CraftRecipeText, decision.CraftRecipe is { } recipe ? "필요 재료\n" + Materials(recipe) : "");
        Set(RecipePreviewText, string.Join("\n\n", decision.RecipePreview.Select((item, index) =>
            $"{index + 1}. {RecommendationPresentation.CoachUnitName(item.Name, item.Tier)} ×{item.MissingCount}\n" + Materials(item))));
        RecipePreviewLabel.Visibility = RecipePreviewText.Visibility;
        Set(ConstraintText, decision.Constraint);
        Set(ReasonText, displayReason);
        Set(ConfirmationText, decision.Confirmation);
        Set(CompletedText, decision.CompletionNotice);
        Set(MilestoneText, decision.Milestone);
        Set(ManualSummaryText, frame.ManualGoalSummary);
        Set(PreserveText, presentation.IsStart ? "" : decision.PreservedMaterials);
        Set(AlternativeText, decision.Alternative);
        Set(ChangeText, decision.ChangeReason);
        var clearRewards = frame.IsCurrent && decision.Kind != CoachActionKind.Finished
            ? LoginClearRewards.ForCount(frame.LoadedClearCount) : null;
        Set(ClearRewardsText, clearRewards?.Describe(frame.Round) ?? "");
        ClearRewardsPanel.Visibility = ClearRewardsText.Visibility;
        Set(OperationText, "유닛 배치, 공격 대상, 스킬 사용은 게임에서 확인해 주세요.\n" +
            "조합 후: 새 패 반영을 기다리세요.");
        Set(UnknownText, string.IsNullOrEmpty(decision.UnknownSignals)
            ? "" : "게임에서 확인:\n" + decision.UnknownSignals);
        Set(MaterialText, decision.MaterialCompletion is { } progress
            ? presentation.RemainingGuideCrafts is > 0 and var remaining
                ? $"재료 준비 {progress:P0} · {presentation.GuideCraftTargetName} 완성까지 조합 {remaining}회 남음\n아래 재료부터 차례로 조합합니다. 재료 준비율은 완성률이 아닙니다."
                : presentation.RemainingGuideCrafts == 0
                    ? $"{presentation.GuideCraftTargetName} 보유 확인"
                    : $"재료 준비 {progress:P0} · 조합 완료와는 다른 값입니다.\n골드·목재와 전투 준비는 따로 확인하세요."
            : "");
        var readiness = frame.HasKnownDifficulty && frame.GoalId is not null
            ? frame.Recommendations.FirstOrDefault()?.CombatReadiness : null;
        Set(ReadinessSummary, frame.Mode == PlayMode.Guide && frame.GuidePlan is not null
            ? BulletGuideAdvice.SupportSummary(frame) : readiness is null ? "" :
            RecommendationPresentation.ReadinessLine(readiness) + "\n" +
            "수치를 채워도 라인·보스 클리어를 보장하지는 않습니다.");
        ConfirmButton.Visibility = decision.RequiresUserConfirmation ? Visibility.Visible : Visibility.Collapsed;
        NavigationChoice.Visibility = decision.RequiresNavigationChoice ? Visibility.Visible : Visibility.Collapsed;
        if (changed) NavigationChoice.SelectedItem = null;
        ConfirmButton.IsEnabled = !decision.RequiresNavigationChoice || NavigationChoice.SelectedItem is NavigationOption;
        PauseButton.Content = frame.Paused ? "안내 재개" : "안내 일시정지";
        PauseButton.Visibility =
            decision.Kind == CoachActionKind.Finished ? Visibility.Collapsed : Visibility.Visible;
        PreserveLabel.Visibility = PreserveText.Visibility;
        if (decision.Kind == CoachActionKind.Finished)
            PreserveText.Visibility = ReadinessSummary.Visibility =
                NavigationTiming.Visibility = OperationText.Visibility = Visibility.Collapsed;
        ActionIcon.Content = unit is null ? null :
            UnitImageFactory.Create(unit.Image, unit.Name, OverlayTheme.CoachIconSize, unit.Id);
        ActionIcon.Visibility = unit is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetItemStatus(this, CoachPresentation.ActionTitle(decision, unit));
        ApplyDensity();
        if (IsPlanWorkspace) RenderMainPresentation(decision, frame);
        if (changed) CoachScroll.ScrollToHome();
    }

    private void ArrangeWorkspace()
    {
        var idle = Plan?.IsCurrent != true;
        var split = IsPlanWorkspace && !idle && ActualWidth >= MainPlanTheme.SplitWidth;
        PlanColumn.Width = new GridLength(split ? 1.2 : 1, GridUnitType.Star);
        ActionColumn.Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(ActionBody, split ? 1 : 0);
        Grid.SetRow(ActionBody, split || IsPlanWorkspace ? 0 : 1);
        Grid.SetRow(PlanView, IsPlanWorkspace && !split ? 1 : 0);
        PlanView.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        PlanView.Margin = split ? MainPlanTheme.PanelGap : OverlayTheme.CoachSectionSpacing;
    }

    public void ShowPlan()
    {
        UpdateLayout();
        PlanView.BringIntoView();
    }

    public void SetCompact(bool compact)
    {
        _compact = compact;
        PresentationButton.Content = compact ? "설명 자세히" : "설명 간단히";
        ApplyDensity();
    }

    private void ApplyDensity()
    {
        if (_presentation is not { } presentation || _decision is not { } decision) return;
        var reason = presentation.ShowEssentialReason || (IsPlanWorkspace && !_compact && !presentation.IsStart);
        ReasonText.Visibility = reason && !string.IsNullOrEmpty(decision.Reason) ? Visibility.Visible : Visibility.Collapsed;
        ReasonLabel.Visibility = _compact ? Visibility.Collapsed : ReasonText.Visibility;
        ConfirmationText.Visibility = (presentation.ShowConfirmation || (IsPlanWorkspace && !_compact && !presentation.IsStart)) &&
            !string.IsNullOrEmpty(decision.Confirmation) ? Visibility.Visible : Visibility.Collapsed;
        ConfirmationLabel.Visibility = _compact ? Visibility.Collapsed : ConfirmationText.Visibility;
        if (presentation.IsStart)
            DetailsPanel.Visibility = GuideChoice.Visibility = Visibility.Collapsed;
        else DetailsPanel.Visibility = Visibility.Visible;
    }
    public void SetInputHint(string value) => Set(InteractionHint, value);

    private static string Materials(RecipeCraftStep recipe) => string.Join("\n+ ", recipe.Ingredients.Select(item =>
        $"{RecommendationPresentation.CoachUnitName(item.Name, item.Tier)} ×{item.RequiredCount}"));

    private static void Set(TextBlock block, string value)
    {
        block.Text = value;
        block.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(block, value);
        AutomationProperties.SetItemStatus(block, value);
    }

    private void Advanced_OnClick(object sender, RoutedEventArgs e) => AdvancedRequested?.Invoke();
    private void GuideChoice_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_renderingGuide && GuideChoice.SelectedItem is GuideOption option)
            GuideSelectionRequested?.Invoke(option.Number);
    }
    private void QueenCondition_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_renderingGuide && QueenCondition.IsEnabled && _renderedFrame is { } frame &&
            QueenCondition.SelectedValue is QueenConversionInput input)
            QueenConditionRequested?.Invoke(input, frame.MatchGeneration, frame.Revision);
    }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        var option = _decision?.RequiresNavigationChoice == true
            ? (NavigationChoice.SelectedItem as NavigationOption)?.Id : _decision?.NavigationOptionId;
        if (_decision?.RequiresUserConfirmation == true && option is not null)
            ConfirmationRequested?.Invoke(option);
    }
    private void NavigationChoice_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_decision?.RequiresNavigationChoice == true)
            ConfirmButton.IsEnabled = NavigationChoice.SelectedItem is NavigationOption;
    }
    private void Pause_OnClick(object sender, RoutedEventArgs e) => PauseRequested?.Invoke();
    private void Review_OnClick(object sender, RoutedEventArgs e) => ReviewRequested?.Invoke();
}
