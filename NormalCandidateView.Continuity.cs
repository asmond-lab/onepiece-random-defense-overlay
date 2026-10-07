using System.Windows.Controls;
using System.Windows.Media;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private readonly List<Action<bool>> _freshnessLabels = [];
    private string? _browsingRenderKey;
    private string? _craftRenderKey;
    private readonly List<Action<bool>> _craftFreshnessLabels = [];
    private bool IsFresh => _model?.Snapshot.IsCurrent == true;
    private bool HasLastPlan => _model?.IsDiagnosticReference == true && _model.HasLastKnownInventory;

    private TextBlock FreshnessText(string current, string previous, double size, Brush brush, bool bold = false, bool craft = false)
    {
        var text = Text(current, size, brush, bold);
        void Apply(bool fresh) { text.Text = fresh ? current : previous; text.Foreground = fresh ? brush : OverlayTheme.PlanSecondary; }
        (craft ? _craftFreshnessLabels : _freshnessLabels).Add(Apply);
        Apply(IsFresh);
        return text;
    }

    private NormalCandidateSnapshot BrowsingSnapshot => _model!.IsDiagnosticReference && !IsFresh &&
        _model.LastKnownSnapshot is { } last && last.Stage == _model.Stage && last.Direction == _model.Snapshot.Direction
        ? last : _model.Snapshot;

    private bool CanHighlightForBrowsing(NormalCandidate candidate) => (IsFresh || HasLastPlan) &&
        candidate.ObservedCount == 0 && candidate.Allocation is not null &&
        (_model!.Stage != NormalCandidateStage.Utility || candidate.UsefulSupport);

    private void RefreshFreshnessLabels()
    {
        foreach (var apply in _freshnessLabels) apply(IsFresh);
        foreach (var apply in _craftFreshnessLabels) apply(IsFresh);
        if (_model is null) return;
        _status.Text = CandidateStatusCopy(_model.Snapshot.Status);
        if (HasLastPlan && !IsFresh) _status.Text = "이전에 인식한 유닛 정보예요. 지금 유닛은 다시 확인 중이에요.";
        _status.ToolTip = _status.Text;
    }

    private string BrowsingRenderKey() => string.Join("|", _model!.SessionRevision, _model.BrowsingRevision,
        _model.Stage, _model.ProgressStage, _model.FollowingProgress, _model.FirstUpperId, _model.UserDirection,
        _model.SelectedUnitId, _model.IsPaused, _model.IsDiagnosticReference ? "reference" : IsFresh.ToString(), _selectedCategory, Columns, IsMainWorkspace,
        FoldChoices.GetValue(_model, _ => new FoldPresentation()).HasUserChoice,
        string.Join(",", _model.CollapsedCategories.Order(StringComparer.Ordinal)),
        string.Join(",", _visibleCounts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value)));
}
