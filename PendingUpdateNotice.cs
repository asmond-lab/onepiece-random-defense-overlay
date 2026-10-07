using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace OrandOverlay;

public sealed class PendingUpdateNotice : StackPanel
{
    private readonly Button _button;
    private readonly TextBlock _caption;
    public event Action? Requested;
    private string? _state;

    public PendingUpdateNotice()
    {
        Visibility = Visibility.Collapsed;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        _button = new Button { Content = "↓ 새 버전 알림", FontSize = 12, Padding = new(8, 4, 8, 4),
            MinHeight = 32, Margin = new(0), Background = OverlayTheme.PlanRaised, Foreground = OverlayTheme.PlanWarning,
            BorderBrush = OverlayTheme.PlanLine, BorderThickness = new(1) };
        _caption = new TextBlock { FontSize = 12, Foreground = OverlayTheme.PlanSecondary,
            VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(_button, "pending-update-button");
        AutomationProperties.SetName(_button, "새 버전이 있어요");
        _button.Click += (_, _) => Requested?.Invoke();
        Children.Add(_button); Children.Add(_caption);
    }

    internal void Present(string? tag, bool ready, bool busy)
    {
        var state = tag + "|" + ready + "|" + busy;
        if (state == _state) return;
        _state = state; Visibility = tag is null ? Visibility.Collapsed : Visibility.Visible;
        _button.IsEnabled = !busy;
        _button.Foreground = ready ? OverlayTheme.PlanAccent : OverlayTheme.PlanWarning;
        _button.BorderBrush = ready ? OverlayTheme.PlanAccent : OverlayTheme.PlanLine;
        _caption.Text = busy ? "설치 중이에요" : ready ? "눌러서 설치하기" : "게임을 종료한 뒤 설치하기";
        _button.ToolTip = RandyPickBrand.UpdateLabel(tag).Replace("BETA ", "베타 ", StringComparison.Ordinal) + " · " + _caption.Text;
        AutomationProperties.SetItemStatus(_button, _caption.Text);
    }
}
