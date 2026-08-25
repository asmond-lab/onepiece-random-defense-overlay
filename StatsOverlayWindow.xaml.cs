using System.Windows;
namespace OrandOverlay;

/// <summary>
/// 내 패 상태(수치·정리 안내) 전용 오버레이.
/// 추천 창과 분리해 각자 원하는 위치에 놓을 수 있게 한다.
/// 패널 채우기는 추천 창의 렌더링 코드가 이 창의 패널을 그대로 쓴다.
/// </summary>
public partial class StatsOverlayWindow : OverlayWindowBase
{
    public StatsOverlayWindow() => InitializeComponent();

    private OverlayDisplayMode _mode = OverlayDisplayMode.Full;

    protected override double DesignWidth =>
        OverlayLayoutPolicy.StatsLayout(_mode).Width;
    protected override double DesignHeight =>
        OverlayLayoutPolicy.StatsLayout(_mode).Height;
    protected override UIElement? ClickThroughIndicator => ClickThroughBadge;

    public void SetDisplayMode(OverlayDisplayMode mode)
    {
        _mode = mode;
        var layout = OverlayLayoutPolicy.StatsLayout(mode);
        NonCoreSectionsPanel.Visibility = layout.NonCoreVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyResolutionScale();
    }

    public void SetReadiness(string? text, bool ready)
    {
        ReadinessSummaryText.Text = text ?? "";
        ReadinessSummaryText.Foreground = ready
            ? OverlayTheme.OkBrush
            : OverlayTheme.WarnBrush;
        ReadinessSummaryText.Visibility = string.IsNullOrWhiteSpace(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
