using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private NormalCraftPlanner? _craftPlanner;
    private string? _lastDetailGoal;
    private bool _sourceDetailsExpanded;
    private IReadOnlyDictionary<string, UnitDefinition> _craftUnits = new Dictionary<string, UnitDefinition>();
    public void SetCraftPlanner(NormalCraftPlanner planner) => _craftPlanner = planner;

    private static bool IsYujiroCraft(NormalCraftStep step) => step.UnitId == "rawcode:2C0h";

    private static string CraftMaterialName(string id, string name) =>
        id == "PICK:A800" ? "랜덤 전용 유닛 1기" : id switch
        {
            "rawcode:GOLD" => "골드", "rawcode:LUMBER" => "목재", _ => name
        };

    private static string CraftSelectionName(NormalCraftStep step) =>
        IsYujiroCraft(step) || step.SelectionUnitId is null
            ? "게임에서 사용할 유닛 확인" : step.SelectionName;

    private static string CraftConditionGuidance(NormalCraftStep step) =>
        step.Conditions.IsSatisfied ? "게임에서 자원 보유량을 확인해 주세요"
            : IsYujiroCraft(step) || step.Conditions.Status == RecipeConditionStatus.Unknown
                ? "게임에서 추가 조합 조건을 확인해 주세요"
                : "게임에서 조합에 필요한 조건을 채운 뒤 다시 확인해 주세요";

    private static string CraftPlanGuidance(NormalCraftPlan plan) =>
        plan.Steps.Any(IsYujiroCraft)
            ? "게임에서 추가 조합 조건과 사용할 유닛을 확인해 주세요. 조합 가능 여부와 자원 보유량은 게임에서 확인해 주세요."
            : plan.Caveat.Replace("2.320 단축키·선택 유닛 근거가 없는 단계는 미확인으로 표시합니다.",
                "조합 방법을 확인할 수 없는 단계는 게임에서 확인해 주세요.", StringComparison.Ordinal);

    private void RenderDetail()
    {
        if (!_renderCraft) return;
        var craftKey = string.Join("|", _model?.SessionRevision, _model?.BrowsingRevision, _model?.SelectedUnitId, _compactCraft, _craftDetached);
        if (_craftRenderKey == craftKey) { foreach (var apply in _craftFreshnessLabels) apply(IsFresh); return; }
        var keepScroll = _lastDetailGoal == _model?.SelectedUnitId && _craftExpansionSession == _model?.SessionRevision;
        var detailOffset = keepScroll ? _detailScroll.VerticalOffset : 0;
        var missingOffset = keepScroll ? _missingScroll.VerticalOffset : 0;
        if (_craftExpansionSession != _model?.SessionRevision)
        {
            _craftStepExpansion.Clear();
            _craftExpansionSession = _model?.SessionRevision ?? -1;
        }
        _craftRenderKey = craftKey; _craftFreshnessLabels.Clear();
        if (_lastDetailGoal != _model?.SelectedUnitId)
        {
            _lastDetailGoal = _model?.SelectedUnitId;
            _detailScroll.ScrollToHome();
            _missingScroll.ScrollToHome();
            _missingMaterialsExpanded = true;
        }
        _detail.Children.Clear();
        _craftHeader.Children.Clear(); _craftResources.Children.Clear(); _missingHost.Children.Clear();
        _hasMissingMaterials = false;
        UpdateCraftPaneLayout();
        if (_model?.SelectedUnit is not { } unit)
        {
            _detail.Children.Add(Text("카드를 선택하면 조합 순서를 볼 수 있어요.", CraftMetaSize, OverlayTheme.PlanSecondary));
            return;
        }
        _craftUnits = _model.Units.ToDictionary(u => u.Id, StringComparer.Ordinal);
        var plan = IsFresh || HasLastPlan ? _craftPlanner?.Build(unit.Id, IsFresh ? _model.CurrentInventory : _model.LastKnownInventory) : null;
        _craftHeader.Children.Add(CraftGoalHeader(unit, plan));
        var freshness = FreshnessText("", "마지막 인식 기준 · 현재 패 확인 중", CraftMetaSize, OverlayTheme.PlanWarning, craft: true);
        _craftFreshnessLabels.Add(fresh => freshness.Visibility = fresh ? Visibility.Collapsed : Visibility.Visible);
        _craftHeader.Children.Add(freshness);
        if (!IsFresh && !HasLastPlan)
        {
            _detail.Children.Add(Text("유닛 다시 확인 중 · 선택한 목표는 유지돼요.", CraftBodySize, OverlayTheme.PlanSecondary));
            return;
        }
        if (_model.SelectedCandidate is { Completion: null } unavailable)
        {
            var reason = Text(CandidateCaveatCopy(unavailable.Caveat), CraftMetaSize, OverlayTheme.PlanWarning);
            AutomationProperties.SetAutomationId(reason, "normal-material-unavailable-reason");
            _detail.Children.Add(reason);
        }
        if (plan is not null)
        {
            RenderMissingMaterials(plan);
            UpdateCraftPaneLayout();
            var flow = new StackPanel();
            AutomationProperties.SetAutomationId(flow, "normal-craft-flow");
            if (plan.GoalOwned) flow.Children.Add(FreshnessText("목표 유닛이 인식됐어요.", "이전에 목표 유닛이 인식됐어요 · 현재 미확인", CraftBodySize, OverlayTheme.PlanSuccess, true, craft: true));
            if (_craftDetached && plan.Steps.Count > 0) flow.Children.Add(CompactCraftTableHeader());
            for (var index = 0; index < plan.Steps.Count; index++)
                flow.Children.Add(CraftStepCard(plan.Steps[index], index + 1));
            var note = Text("게임에서 직접 조합 · 패가 바뀌면 다시 계산", CraftMetaSize, OverlayTheme.PlanSecondary);
            note.ToolTip = CraftPlanGuidance(plan);
            flow.Children.Add(note);
            _detail.Children.Add(flow);
        }
        RenderCraftSourceDetails(unit, plan);
        foreach (var apply in _craftFreshnessLabels) apply(IsFresh);
        if (keepScroll)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (_craftRenderKey != craftKey) return;
                _detailScroll.ScrollToVerticalOffset(detailOffset);
                _missingScroll.ScrollToVerticalOffset(missingOffset);
            }));
    }

    private UIElement CraftGoalHeader(UnitDefinition unit, NormalCraftPlan? plan)
    {
        var grid = new Grid { Margin = new(0, 0, 0, CraftSmallGap) };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var portrait = UnitImageFactory.Create(unit.Image, unit.Name, _craftDetached ? 36 : CraftGoalIconSize, unit.Id);
        portrait.Margin = new(0, 0, CraftSmallGap, 0); grid.Children.Add(portrait);
        var words = new StackPanel();
        var name = Text(unit.Name, CraftHeadingSize, OverlayTheme.PlanText, true);
        if (_craftDetached)
        {
            name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = unit.Name;
        }
        words.Children.Add(name);
        if (_craftDetached || !_compactCraft)
        {
            var tier = Text(NormalCandidateBrowser.Tier(unit) + (_craftDetached ? " · 선택한 목표" : ""), _craftDetached ? 11 : CraftMetaSize, OverlayTheme.PlanSecondary);
            if (_craftDetached)
            {
                tier.TextWrapping = TextWrapping.NoWrap; tier.TextTrimming = TextTrimming.CharacterEllipsis;
                tier.ToolTip = tier.Text;
            }
            words.Children.Add(tier);
        }
        var title = words;
        title.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(title, 1); grid.Children.Add(title);
        var count = FreshnessText(plan is null ? "직접 선택" : plan.GoalOwned ? "인식됨" : plan.Steps.Count + "단계", plan?.GoalOwned == true ? "이전 인식" : plan is null ? "직접 선택" : plan.Steps.Count + "단계", CraftMetaSize, OverlayTheme.PlanSecondary, craft: true);
        count.Margin = new(CraftSmallGap, 0, 0, 0); count.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(count, 2); grid.Children.Add(count);
        var summary = unit.Name + " · 조합 순서 / 직접 선택";
        if (plan is not null) summary += " · 부족 재료 " + plan.MissingMaterials.Sum(m => (decimal)m.MissingCount) + "개";
        grid.ToolTip = _craftDetached ? summary + " · 제목을 끌어 창 이동" : summary; AutomationProperties.SetName(grid, summary); return grid;
    }

    private void RenderCraftSourceDetails(UnitDefinition unit, NormalCraftPlan? plan)
    {
        var details = new StackPanel();
        AutomationProperties.SetName(details, "선택한 유닛의 능력과 조합 조건 참고 정보");
        if (plan is not null && !string.IsNullOrWhiteSpace(plan.Caveat)) details.Children.Add(Text(CraftPlanGuidance(plan), CraftMetaSize, OverlayTheme.PlanSecondary));
        foreach (var role in _model!.RolesFor(unit.Id))
            details.Children.Add(Text(role.Category + " · " + RoleDisplayValue(role.Value) +
                (string.IsNullOrWhiteSpace(role.Condition) || role.Condition == "catalog"
                    ? "" : " · 적용 조건은 게임에서 확인해 주세요"), CraftMetaSize, OverlayTheme.PlanSecondary));
        foreach (var ability in unit.OfficialAbilities)
            details.Children.Add(Text(ability.Name + " · " + ability.DisplayValue + " (출처 참고값)", CraftMetaSize, OverlayTheme.PlanSecondary));
        if (_model.SelectedCandidate is { } candidate)
        {
            if (candidate.Completion is null) details.Children.Add(Text(CandidateCaveatCopy(candidate.Caveat), CraftMetaSize, OverlayTheme.PlanSecondary));
            if (candidate.MovementRoles.Count > 0) details.Children.Add(Text("이동 · " + string.Join(" · ", candidate.MovementRoles), CraftMetaSize, OverlayTheme.PlanSecondary));
            if (candidate.RecommendationReason.Length > 0) details.Children.Add(Text(candidate.RecommendationReason, CraftMetaSize, Mint));
        }
        details.Children.Add(Text(plan?.Steps.Any(IsYujiroCraft) == true
            ? "선택한 맵의 조합 재료를 참고했어요. 역할 정보는 참고용이며, 선택과 조합은 게임에서 직접 해 주세요."
            : "적용된 조합 자료를 참고했어요. 역할 정보는 참고용이며, 선택과 조합은 게임에서 직접 해 주세요.", CraftMetaSize, OverlayTheme.PlanSecondary));
        var source = new Expander { Header = "능력·조합 조건과 출처", Content = details,
            Foreground = OverlayTheme.PlanSecondary, IsExpanded = _sourceDetailsExpanded, Margin = new(0, CraftSmallGap, 0, 0) };
        source.Expanded += (_, _) => _sourceDetailsExpanded = true;
        source.Collapsed += (_, _) => _sourceDetailsExpanded = false;
        _detail.Children.Add(source);
    }
}
