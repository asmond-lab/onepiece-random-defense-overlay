using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private readonly WrapPanel _progressControls = new();
    private readonly TextBlock _anchorText = Text("", 10, Secondary);
    private readonly ComboBox _anchorPicker = new() { MinWidth = 148, MaxWidth = 240, DisplayMemberPath = "Name" };
    private Button? _followProgress;
    private bool _movementExpanded;

    private static string StageTitle(NormalCandidateStage stage) => stage switch
    {
        NormalCandidateStage.Rare => "첫 희귀 유닛을 찾는 중",
        NormalCandidateStage.Legend => "첫 전설·히든 유닛을 찾는 중",
        NormalCandidateStage.Upper => "첫 상위 유닛을 찾는 중",
        _ => "주력에 필요한 지원 찾기"
    };
    internal static string DamageLabel(string direction) => direction switch
    {
        "physical" => "물리", "magical" => "마법", "both" => "물리·마법 복합", _ => "유형 미확인"
    };
    private void RenderProgressControls()
    {
        if (_model is null) return;
        if (_followProgress is null)
        {
            _followProgress = Action("현재 단계로", () => _model?.ResumeProgress());
            AutomationProperties.SetAutomationId(_followProgress, "normal-follow-progress");
            AutomationProperties.SetAutomationId(_anchorText, "normal-first-upper");
            AutomationProperties.SetAutomationId(_anchorPicker, "normal-first-upper-picker");
            AutomationProperties.SetName(_anchorPicker, "먼저 만든 상위 유닛 선택");
            _anchorPicker.SelectionChanged += (_, _) =>
            {
                if (!_painting && _anchorPicker.SelectedItem is UnitDefinition unit) _model?.SelectFirstUpper(unit.Id);
            };
            _progressControls.Children.Add(_followProgress);
            _progressControls.Children.Add(_anchorText);
            _progressControls.Children.Add(_anchorPicker);
        }
        if (!_header.Children.Contains(_progressControls)) _header.Children.Add(_progressControls);
        _followProgress.Visibility = _model.FollowingProgress ? Visibility.Collapsed : Visibility.Visible;
        var hasAnchor = _model.FirstUpperId is not null;
        var needsAnchor = !hasAnchor && _model.FirstUpperChoices.Count > 1;
        _anchorText.Text = hasAnchor
            ? "주력 · " + _model.Units.First(u => u.Id == _model.FirstUpperId).Name + " / " + DamageLabel(_model.FirstUpperDirection)
            : needsAnchor ? "먼저 만든 상위를 골라 주세요" : "";
        _anchorText.ToolTip = needsAnchor ? "상위 유닛이 여러 개라 어떤 유닛을 먼저 만들었는지 알 수 없어요. 먼저 만든 유닛을 골라 주세요." : "이번 판에 처음 확인된 상위를 기준으로 지원을 추천해요.";
        _anchorText.Visibility = IsMainWorkspace && hasAnchor ? Visibility.Collapsed : Visibility.Visible;
        if (IsMainWorkspace && hasAnchor) _bannerTitle.Text += " · " + _anchorText.Text;
        _anchorPicker.Visibility = needsAnchor ? Visibility.Visible : Visibility.Collapsed;
        _anchorPicker.ItemsSource = needsAnchor ? _model.FirstUpperChoices : null;
        _anchorPicker.SelectedItem = null;
        _progressControls.Visibility = !_model.FollowingProgress || (!IsMainWorkspace && hasAnchor) || needsAnchor ? Visibility.Visible : Visibility.Collapsed;
    }
    private static void AddCandidateGuide(Panel body, NormalCandidate candidate, bool compact = false)
    {
        if (candidate.StoryFast)
        {
            var flag = Text("스토리 빠름", 9, Gold);
            flag.TextAlignment = TextAlignment.Center;
            AutomationProperties.SetAutomationId(flag, "normal-story-" + candidate.Unit.Id);
            flag.ToolTip = "스토리를 빠르게 진행하는 데 도움이 되는 유닛으로 소개돼요. 재료가 가까운 순서와는 달라요.";
            body.Children.Add(flag);
        }
        if (NormalCandidateBrowser.IsUpper(candidate.Unit))
        {
            var damage = Text(DamageLabel(candidate.DamageType), 9, Secondary);
            damage.TextAlignment = TextAlignment.Center;
            AutomationProperties.SetAutomationId(damage, "normal-damage-" + candidate.Unit.Id);
            body.Children.Add(damage);
        }
        if (candidate.UsefulSupport && !string.IsNullOrWhiteSpace(candidate.RecommendationReason))
        {
            var reason = Text(compact ? candidate.RecommendationReason.Replace(" · ", "\n", StringComparison.Ordinal) : candidate.RecommendationReason, 9, Mint);
            AutomationProperties.SetName(reason, candidate.RecommendationReason);
            reason.TextAlignment = TextAlignment.Center;
            AutomationProperties.SetAutomationId(reason, "normal-fit-" + candidate.Unit.Id);
            body.Children.Add(reason);
        }
    }
    private void RenderMovementCoverage()
    {
        if (_model?.ProgressStage != NormalCandidateStage.Utility) return;
        var entries = new StackPanel();
        var names = _model.Units.ToDictionary(u => u.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var role in _model.MovementCoverage)
        {
            var providers = string.Join(", ", role.ObservedUnitIds.Select(id => names.GetValueOrDefault(id)?.Name ?? "이름을 확인할 수 없는 유닛"));
            var row = FreshnessText(role.Role + " · " + (role.Covered ? providers : "이 능력을 가진 유닛을 인식하지 못했어요"), role.Role + " · 다시 확인 중", 10, Secondary);
            AutomationProperties.SetAutomationId(row, "normal-movement-" + role.Role);
            entries.Children.Add(row);
        }
        var available = _model.MovementCoverage.Where(r => r.Covered).Select(r => r.Role).ToArray();
        var header = "맵 이동 · " + (!_model.Snapshot.IsCurrent ? "다시 확인 중" : available.Length > 0 ? string.Join(" · ", available) : "이동 능력을 가진 유닛을 확인해 주세요");
        var expander = new Expander { Header = FreshnessText(header, "맵 이동 · 다시 확인 중", 10, Secondary), Content = entries, IsExpanded = _movementExpanded, Foreground = Secondary, Margin = new(0, 0, 0, 4) };
        AutomationProperties.SetAutomationId(expander, "normal-movement-coverage");
        expander.Expanded += (_, _) => _movementExpanded = true;
        expander.Collapsed += (_, _) => _movementExpanded = false;
        _groups.Children.Add(expander);
    }
}
