using System.Windows;
using System.Windows.Input;

namespace OrandOverlay;

public partial class TelemetryConsentWindow : Window
{
    public TelemetryConsentWindow()
    {
        InitializeComponent();
    }

    private void Agree_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Decline_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        DialogResult = false;
    }
}
