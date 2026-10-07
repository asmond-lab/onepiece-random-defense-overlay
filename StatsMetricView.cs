using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrandOverlay;

// Presentation only: values, target boundaries and source conditions come from the caller.
public sealed class StatsMetricView : Border
{
    public double Current { get; }
    public double Target { get; }
    public bool TargetsKnown { get; private set; }
    private bool _observationKnown = true;
    private bool _partialValue;
    private bool _referenceValue;
    private readonly string _detail;
    private readonly TextBlock _value, _target, _state, _note;
    private readonly Border _pill, _gap, _progress, _fill;
    private double _ratio;

    public StatsMetricView(string label, double current, double target, string detail)
    {
        Current = current; Target = target; _detail = detail;
        Background = Brushes.Transparent;
        BorderBrush = RandyPickTheme.Border; BorderThickness = new Thickness(0, 0, 1, 0);
        MinWidth = 0; MinHeight = 72;
        var grid = new Grid();
        _gap = new Border { Height = 2, Background = RandyPickTheme.Warning, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(_gap, "stats-priority-line");
        grid.Children.Add(_gap);
        var body = new StackPanel { Margin = new Thickness(9, 9, 9, 8) };
        var head = new WrapPanel();
        head.Children.Add(Text(label switch
        {
            "이감" => "이동 속도 감소", "방깎" => "방어력 감소", "마방깎" => "마법 방어력 감소",
            "공증" => "공격력 증가", "공속" => "공격 속도", "체젠" => "체력 재생",
            "마젠" => "마나 재생", "폭뎀증" => "폭발 피해 증가", "마뎀증" => "마법 피해 증가",
            _ => label
        }, 10, RandyPickTheme.SecondaryHex));
        _state = Text("미확인", 7, RandyPickTheme.MutedHex);
        _pill = new Border { Child = _state, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 1, 3, 1), Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(_pill, "stats-status-pill");
        head.Children.Add(_pill); body.Children.Add(head);
        _value = Text("?", 26, RandyPickTheme.TextHex); _value.FontWeight = FontWeights.SemiBold; _value.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetAutomationId(_value, "stats-current");
        body.Children.Add(new Viewbox { Child = _value, Height = 31, Margin = new Thickness(0, 4, 0, 0),
            Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
        _target = Text("목표 미확인", 9, RandyPickTheme.MutedHex); _target.Margin = new Thickness(0, 5, 0, 0);
        AutomationProperties.SetAutomationId(_target, "stats-target"); body.Children.Add(_target);
        _fill = new Border { Background = RandyPickTheme.Text, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _progress = new Border { Background = RandyPickTheme.Border, Height = 2, Margin = new Thickness(0, 7, 0, 0), Child = _fill };
        _progress.SizeChanged += (_, _) => UpdateProgress();
        AutomationProperties.SetAutomationId(_progress, "stats-progress"); body.Children.Add(_progress);
        _note = Text("값·조건 확인 필요", 9, RandyPickTheme.MutedHex);
        // Shared source details and this metric's tooltip carry the explanation once.
        _note.Visibility = Visibility.Collapsed;
        body.Children.Add(_note); grid.Children.Add(body); Child = grid;
        AutomationProperties.SetAutomationId(this, "stats-metric:" + label); AutomationProperties.SetName(this, ((TextBlock)head.Children[0]).Text);
        SetTargetsKnown(false);
    }

    public void SetObservationKnown(bool known, bool partialValue = false, bool referenceValue = false)
    {
        _observationKnown = known;
        _partialValue = partialValue;
        _referenceValue = referenceValue;
        SetTargetsKnown(TargetsKnown);
    }

    public void SetTargetsKnown(bool known)
    {
        var currentKnown = _observationKnown && double.IsFinite(Current);
        TargetsKnown = known && currentKnown && !_partialValue && !_referenceValue && double.IsFinite(Target);
        var met = Current + 0.0001 >= Target; // Preserve the established comparison boundary.
        _value.Text = currentKnown ? OverlayTheme.Num(Current) : "?";
        var valueContext = _partialValue ? "미확인 효과를 제외한 부분합" : _referenceValue ? "이전 자료 참고값 · 실제 적용량과 다를 수 있음" : "";
        ToolTip = currentKnown ? string.Join("\n", new[] { _detail, valueContext }.Where(text => !string.IsNullOrWhiteSpace(text)))
            : "수치 미확인 · 합계와 목표 판단 보류";
        AutomationProperties.SetItemStatus(_value, currentKnown ? OverlayTheme.Num(Current) : "수치를 확인해 주세요.");
        AutomationProperties.SetHelpText(_value, currentKnown ? valueContext : "수치 미확인");
        _state.Text = TargetsKnown ? met ? "충족" : "부족" : currentKnown && _partialValue ? "부분합" : currentKnown && _referenceValue ? "참고" : "미확인";
        _state.Foreground = TargetsKnown && !met ? RandyPickTheme.Warning : RandyPickTheme.Secondary;
        _pill.WithOverlayBackground(OverlayChrome.WellKey);
        _pill.BorderBrush = RandyPickTheme.Border;
        _pill.Visibility = TargetsKnown ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetItemStatus(_pill, TargetsKnown ? met ? "목표 충족" : "목표 부족" : "목표를 확인할 수 없어요.");
        _target.Text = TargetsKnown ? "목표 " + OverlayTheme.Num(Target) : "목표 미확인";
        _target.Visibility = TargetsKnown ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetItemStatus(_target, TargetsKnown ? OverlayTheme.Num(Target) : "목표를 확인할 수 없어요.");
        AutomationProperties.SetItemStatus(this, TargetsKnown ? met ? "목표 충족" : "목표 부족" : "목표를 확인할 수 없어요.");
        _note.Text = currentKnown ? string.IsNullOrWhiteSpace(_detail) ? "표기 합계 참고" : _detail : "값·조건 확인 필요";
        var progressKnown = TargetsKnown && Target > 0 && Current >= 0;
        _ratio = progressKnown ? Math.Clamp(Current / Target, 0, 1) : 0;
        _fill.Visibility = progressKnown ? Visibility.Visible : Visibility.Collapsed;
        _progress.Visibility = progressKnown ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetItemStatus(_progress, progressKnown ? OverlayTheme.Num(_ratio) : "목표 진행률을 확인할 수 없어요.");
        _gap.Visibility = TargetsKnown && !met ? Visibility.Visible : Visibility.Collapsed;
        if (TargetsKnown && !met) this.WithOverlayBackground(OverlayChrome.WellKey);
        else Background = Brushes.Transparent;
        UpdateProgress();
    }

    private void UpdateProgress() => _fill.Width = Math.Max(0, _progress.ActualWidth * _ratio);
    internal static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private static TextBlock Text(string text, double size, string color) => new()
    { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(color) };
}
