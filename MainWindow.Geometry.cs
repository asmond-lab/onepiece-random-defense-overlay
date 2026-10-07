using System.Windows;

namespace OrandOverlay;

public static class MainWindowGeometry
{
    public const double MinimumWidth = 800;
    public const double MinimumHeight = 560;

    /// <summary>
    /// 저장된 창 사각형을 현재 작업 영역 안으로 맞춘다. 모니터가 바뀌어 창이 화면 밖에 있거나
    /// 작업 영역보다 크면 크기를 줄이고 위치를 당긴다. 저장값이 비정상이면 null.
    /// </summary>
    public static Rect? Restore(double? left, double? top, double? width, double? height,
        Rect workArea, double minWidth, double minHeight)
    {
        if (left is not double l || top is not double t || width is not double w || height is not double h) return null;
        if (!double.IsFinite(l) || !double.IsFinite(t) || !double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0) return null;
        w = Math.Clamp(w, Math.Min(minWidth, workArea.Width), workArea.Width);
        h = Math.Clamp(h, Math.Min(minHeight, workArea.Height), workArea.Height);
        l = Math.Clamp(l, workArea.Left, workArea.Right - w);
        t = Math.Clamp(t, workArea.Top, workArea.Bottom - h);
        return new Rect(l, t, w, h);
    }
}

public partial class MainWindow
{
    private bool _mainWindowSizedByUser;

    private void RestoreMainWindowGeometry()
    {
        var rect = MainWindowGeometry.Restore(_settings.MainWindowLeft, _settings.MainWindowTop,
            _settings.MainWindowWidth, _settings.MainWindowHeight, new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight), MinWidth, MinHeight);
        _mainWindowSizedByUser = true;
        if (rect is not { } r) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height;
        if (_settings.MainWindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveMainWindowGeometry()
    {
        if (!_persistSettings || !IsLoaded) return;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty) return;
        _settings.MainWindowLeft = bounds.Left;
        _settings.MainWindowTop = bounds.Top;
        _settings.MainWindowWidth = bounds.Width;
        _settings.MainWindowHeight = bounds.Height;
        _settings.MainWindowMaximized = WindowState == WindowState.Maximized;
        _execution.SaveSettings(_settings);
    }
}
