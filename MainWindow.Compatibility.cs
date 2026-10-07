using System.Windows;

namespace OrandOverlay;

public partial class MainWindow
{
    private string? _lastCompatibilityInfo;
    private DateTimeOffset _lastCompatibilityRefresh = DateTimeOffset.MinValue;

    private void UpdateCompatibilityInfo(RecognitionResult result)
    {
        _lastCompatibilityInfo = WarcraftCompatibilityInfo.Format(result);
        CompatibilityCopyButton.IsEnabled = true;
        CompatibilityCopyButton.ToolTip = "오류를 제보할 때 붙여 넣을 기술 정보를 복사해요. 화면에 보이는 안내보다 자세한 내용이 들어 있지만 개인 경로와 플레이어 정보는 포함되지 않아요.";
        if (_execution.RuntimeEnabled && result.State is RecognitionState.Unsupported or RecognitionState.UnverifiedProfile &&
            DateTimeOffset.UtcNow - _lastCompatibilityRefresh >= TimeSpan.FromMinutes(5))
        {
            _lastCompatibilityRefresh = DateTimeOffset.UtcNow;
            _ = RefreshCompatibilityProfilesAsync();
        }
    }

    private async Task RefreshCompatibilityProfilesAsync()
    {
        try { await _execution.RefreshProfilesNowAsync(); }
        catch (InvalidOperationException) { /* Revoked consent cannot start a refresh. */ }
    }

    private void CopyCompatibilityInfo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lastCompatibilityInfo is null) return;
        try
        {
            Clipboard.SetText(_lastCompatibilityInfo);
            FooterStatus.Text = "오류 제보에 필요한 기술 정보를 복사했어요. 제보할 때 붙여 넣어 주세요.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            FooterStatus.Text = "클립보드를 사용할 수 없습니다. 잠시 후 다시 복사해 주세요.";
        }
    }
}
