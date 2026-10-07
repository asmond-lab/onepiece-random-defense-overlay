using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OrandOverlay;

internal static partial class DiagnosticCapture
{
    private static void SelectCraftTarget(MainWindow main, NormalCandidateBrowser model, string id)
    {
        model.Select(id); Drain();
        var button = Visuals((NormalCandidateView)main.FindName("NormalBrowserView")).OfType<Button>()
            .Single(e => AutomationProperties.GetAutomationId(e) == "normal-open-craft-window");
        if (!button.IsEnabled) throw new InvalidOperationException("Selected craft target cannot open its window.");
        Click(button);
    }

    private static void CaptureDetachedCraft(MainWindow main, OverlayWindow overlay, NormalCandidateBrowser model,
        Action sameHand, Action<string, Window, string> capture, Action<bool, string> check)
    {
        var window = main.CraftWindow;
        var workspace = main.CraftWorkspace;
        var normal = (NormalCandidateView)main.FindName("NormalBrowserView");
        check(window.HostWindow == main && window.Owner is null && window.IsVisible && main.IsEnabled, "detached-nonmodal-window-bound-to-app-lifetime");
        foreach (var view in new[] { normal, overlay.NormalView })
            check(!Visuals(view).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) == "normal-selected-detail"),
                "detached-no-inline-craft-duplicate");
        check(Application.Current.Windows.OfType<NormalCraftWindow>().Count() == 1, "detached-exactly-one-window");
        window.Topmost = false;
        var reopen = Visuals(normal).OfType<Button>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-open-craft-window");
        Click(reopen);
        check(window.Topmost && window.IsVisible, "detached-reopen-restores-always-on-top");
        var scroll = Visuals(workspace).OfType<ScrollViewer>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-scroll");
        scroll.ScrollToEnd(); Drain();
        var offset = scroll.VerticalOffset;
        var selected = model.SelectedUnitId;
        var bounds = new Rect(window.Left, window.Top, window.Width, window.Height);
        main.WindowState = WindowState.Minimized; Drain();
        check(window.IsVisible, "detached-main-minimize-keeps-craft-visible");
        main.WindowState = WindowState.Normal; Drain();
        window.Close(); Drain();
        check(!window.IsVisible && main.IsVisible && overlay.IsVisible && overlay.Stats.IsVisible, "detached-native-close-hides-only-craft");
        sameHand();
        check(!window.IsVisible && window.Topmost && model.SelectedUnitId == selected, "detached-same-hand-keeps-dismissed-craft-hidden");
        foreach (var view in new[] { normal, overlay.NormalView })
        {
            Click(Visuals(view).OfType<Button>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-open-craft-window"));
            check(window.IsVisible && ReferenceEquals(main.CraftWindow, window) && Math.Abs(scroll.VerticalOffset - offset) < 1,
                "detached-reopen-same-content-and-scroll");
            check(new Rect(window.Left, window.Top, window.Width, window.Height) == bounds, "detached-reopen-preserves-position-size");
            window.Close(); Drain();
        }
        var candidate = Visuals(normal).OfType<Button>().First(e => AutomationProperties.GetAutomationId(e).StartsWith("normal-candidate-", StringComparison.Ordinal));
        var target = AutomationProperties.GetAutomationId(candidate)["normal-candidate-".Length..];
        Click(candidate);
        if (selected == target)
        {
            check(!window.IsVisible && model.SelectedUnitId is null, "detached-candidate-second-click-unpins-same-target");
            Click(candidate); Drain();
        }
        check(window.IsVisible && model.SelectedUnitId == target, "detached-candidate-click-reopens-selected-target");
        Click(candidate); Drain();
        check(!window.IsVisible && model.SelectedUnitId is null, "detached-candidate-second-click-unpins");
        model.Select(selected!); Drain();
        check(!Visuals(window).OfType<CheckBox>().Any(), "detached-no-topmost-checkbox");
        check(window.WindowStyle == WindowStyle.None && window.Topmost, "detached-borderless-fixed-topmost");
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
        check(chrome is { UseAeroCaptionButtons: false } && chrome.CaptionHeight >= 28 && chrome.ResizeBorderThickness.Left > 0, "detached-drag-resize-without-caption-buttons");
        ValidateCraftHudChrome(window, check);
        window.Width = window.MinWidth; window.Height = window.MinHeight; scroll.ScrollToHome(); Drain();
        ValidateCraftSplit(workspace, "craft-minimum", check);
        capture("minimum", window, "craft");
        window.Width = NormalCraftWindow.PreferredWidth; window.Height = NormalCraftWindow.PreferredHeight; Drain();
        capture("detached", main, "main"); capture("detached", overlay, "overlay"); capture("detached", window, "craft");
        var owner = new Window { Width = 300, Height = 200, Left = -6000, Top = 0, ShowInTaskbar = false, ShowActivated = false };
        NormalCraftWindow? child = null;
        try
        {
            owner.Show();
            child = new NormalCraftWindow(owner, owner, new Border(), false); child.ShowWorkspace(); Drain();
            System.ComponentModel.CancelEventHandler cancel = (_, e) => e.Cancel = true;
            owner.Closing += cancel; owner.Close(); Drain();
            check(owner.IsVisible && child.IsVisible, "detached-cancelled-owner-close-preserves-child");
            owner.Closing -= cancel; owner.Close(); Drain();
            check(!Application.Current.Windows.Cast<Window>().Contains(child), "detached-owner-close-releases-child");
        }
        finally { child?.CloseForApplication(); owner.Close(); }
    }

    private static void ValidateCraftSplit(FrameworkElement view, string role, Action<bool, string> check)
    {
        var elements = Visuals(view).OfType<FrameworkElement>().ToArray();
        var well = elements.OfType<Border>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-selected-detail");
        check(well.ActualWidth <= view.ActualWidth && well.Padding == new Thickness(8), "density-panel-width-and-insets-" + role);
        var panes = elements.OfType<Grid>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-panes");
        var missing = elements.OfType<ScrollViewer>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-missing-scroll");
        var recipes = elements.OfType<ScrollViewer>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-scroll");
        var first = Visuals(recipes).OfType<Border>().First(e => AutomationProperties.GetAutomationId(e).StartsWith("normal-craft-step-", StringComparison.Ordinal));
        var a = missing.TranslatePoint(new Point(0, 0), panes);
        var b = recipes.TranslatePoint(new Point(0, 0), panes);
        check(a.Y + missing.ActualHeight <= b.Y + 1 && Math.Abs(a.X - b.X) < 1,
            "e-layout-materials-above-craft-" + role);
        check(missing.ViewportHeight > 0 && recipes.ViewportHeight > 0 && first.TranslatePoint(new Point(0, 0), recipes).Y < recipes.ViewportHeight,
            "e-layout-first-step-visible-with-many-materials-" + role);
        var missingGrid = elements.OfType<Panel>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == "normal-missing-grid");
        var slots = missingGrid?.Children.OfType<FrameworkElement>().Take(2).ToArray();
        if (slots is { Length: 2 })
            check(Math.Abs(slots[0].TranslatePoint(new Point(), missingGrid!).Y - slots[1].TranslatePoint(new Point(), missingGrid!).Y) < 1, "e-missing-materials-share-compact-row-" + role);
        var craftOffset = recipes.VerticalOffset;
        var firstY = first.TranslatePoint(new Point(0, 0), recipes).Y;
        missing.ScrollToEnd(); Drain();
        check(Math.Abs(recipes.VerticalOffset - craftOffset) < 1 && Math.Abs(first.TranslatePoint(new Point(0, 0), recipes).Y - firstY) < 1,
            "e-layout-material-scroll-does-not-move-craft-" + role);
        if (missing.ExtentHeight > missing.ViewportHeight)
            check(missing.VerticalOffset > 0, "e-layout-last-missing-material-reachable-" + role);
        var materialOffset = missing.VerticalOffset;
        recipes.ScrollToEnd(); Drain();
        check(Math.Abs(missing.VerticalOffset - materialOffset) < 1, "e-layout-craft-scroll-does-not-move-materials-" + role);
        recipes.ScrollToHome(); missing.ScrollToHome(); Drain();
        foreach (var item in Visuals(missing).OfType<Border>().Where(e => AutomationProperties.GetAutomationId(e).StartsWith("normal-craft-material-missing/", StringComparison.Ordinal)))
        {
            var origin = item.TranslatePoint(new Point(0, 0), missing);
            check(origin.X >= 0 && origin.X + item.ActualWidth <= missing.ViewportWidth + 1, "e-layout-material-row-width-" + role);
        }
    }
}
