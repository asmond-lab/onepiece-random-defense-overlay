using System.Windows;

namespace OrandOverlay;

public partial class MainWindow
{
    private NormalCraftWindow? _craftWindow;
    internal NormalCraftWindow CraftWindow => _craftWindow ?? throw new InvalidOperationException("Craft window is not initialized.");
    internal FrameworkElement CraftWorkspace => _overlay.NormalView.CraftWorkspace;

    private void InitializeCraftWindow()
    {
        NormalBrowserView.DetachCraftWorkspace(false);
        var workspace = _overlay.NormalView.DetachCraftWorkspace(true);
        _overlay.EnableDetachedCraftLayout();
        NormalBrowserView.CraftWindowRequested += OpenCraftWindow;
        _overlay.NormalView.CraftWindowRequested += OpenCraftWindow;
        Loaded += (_, _) =>
        {
            _craftWindow ??= new NormalCraftWindow(this, _overlay.Stats, workspace, _runtimeEffects,
                _execution.CraftWindowGeometryPath);
            WatchActivityWindow(_craftWindow);
        };
    }

    private void OpenCraftWindow()
    {
        if (!IsLoaded || _craftWindow is null) return;
        if (!_overlay.NormalView.HasCraftSelection)
        {
            _craftWindow.ResetWorkspaceVisibility();
            return;
        }
        if (_settings.OverlayDisplayMode == OverlayDisplayMode.Hidden)
            ApplyOverlayDisplayState(OverlayDisplayPolicy.Toggle(CurrentOverlayDisplayState()), save: true);
        _craftWindow.SetDisplayAllowed(true);
        _craftWindow.ShowWorkspace();
    }
}
