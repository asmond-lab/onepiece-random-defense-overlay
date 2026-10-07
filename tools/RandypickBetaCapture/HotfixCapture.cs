using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OrandOverlay;

internal static partial class DiagnosticCapture
{
    private static void CaptureHotfixStates(MainWindow main, OverlayWindow overlay, NormalCandidateBrowser model,
        Action fresh, string output, Action<string> capture, Action<bool, string> check)
    {
        void Shot(string name, Window window)
        {
            Drain(); window.UpdateLayout();
            var file = "hotfix-" + name + ".png";
            Save((FrameworkElement)window.Content, System.IO.Path.Combine(output, file)); capture(file);
        }
        foreach (var width in new[] { 1280, 920 })
        {
            main.Width = width; Drain();
            foreach (var ready in new[] { false, true })
            {
                fresh(); main.CapturePendingUpdateNotice("1.0.2-test.12", ready);
                Shot("main-" + width + (ready ? "-ready" : "-playing"), main);
                var button = Visuals(main).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "pending-update-button");
                check(button.IsVisible && button.IsEnabled && button.ActualWidth > 80, "pending-main-button-reachable-" + width + ready);
                Shot("overlay-" + width + (ready ? "-ready" : "-playing"), overlay);
            }
        }
        main.Width = 1280; main.CapturePendingUpdateNotice(null, false); fresh();
        check(main.CraftWindow.IsVisible && Field<bool>(main.CraftWindow, "_requestedVisible"), "craft-remains-open-after-pending-notice-captures");
        var recipe = Visuals(main.CraftWorkspace).OfType<ScrollViewer>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-scroll");
        var content = recipe.Content; var target = model.SelectedUnitId;
        recipe.ScrollToEnd(); Drain(); var offset = recipe.VerticalOffset;
        Invoke(main, "InvalidateDiagnosticInventoryObservation", false); Drain();
        check(ReferenceEquals(content, recipe.Content) && model.SelectedUnitId == target && Math.Abs(recipe.VerticalOffset - offset) < 1,
            "stale-read-preserves-recipe-controls-selection-and-scroll");
        check(!model.Snapshot.IsCurrent && model.CurrentInventory.Count == 0, "stale-reference-not-current-hand");
        Shot("craft-stale", main.CraftWindow); Shot("overlay-stale", overlay);
        Invoke(main, "ToggleOverlayVisibility"); Drain();
        check(!overlay.IsVisible && !overlay.Stats.IsVisible && !main.CraftWindow.IsVisible, "f1-hides-all-windows-during-read-loss");
        Invoke(main, "ApplyDiagnosticOverlayVisibility"); Drain();
        check(!overlay.IsVisible && !main.CraftWindow.IsVisible, "read-refresh-respects-f1-hidden-preference");
        Invoke(main, "ToggleOverlayVisibility"); Drain();
        check(overlay.IsVisible && overlay.Stats.IsVisible && main.CraftWindow.IsVisible, "f1-restores-all-windows-during-read-loss");
        fresh();
        check(ReferenceEquals(content, recipe.Content) && model.SelectedUnitId == target && Math.Abs(recipe.VerticalOffset - offset) < 1,
            "recovery-preserves-recipe-controls-selection-and-scroll");
        recipe.ScrollToHome(); Drain(); Shot("craft-recovered", main.CraftWindow);
        main.CraftWindow.Close(); Drain();
        Invoke(main, "ToggleOverlayVisibility"); Invoke(main, "ToggleOverlayVisibility"); fresh();
        check(!main.CraftWindow.IsVisible, "f1-and-refresh-do-not-reopen-dismissed-craft");
        SelectCraftTarget(main, model, target!);
    }
}
