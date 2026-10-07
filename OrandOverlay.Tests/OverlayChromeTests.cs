using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class OverlayChromeTests
{
    [Fact]
    public void TranslucentShellUsesFixedEightyPercentAlpha()
    {
        var resources = new ResourceDictionary();
        OverlayChrome.ApplyTranslucent(resources);
        var shell = Assert.IsType<SolidColorBrush>(resources[OverlayChrome.ShellKey]);
        Assert.Equal(204, shell.Color.A);
        Assert.Equal(RandyPickTheme.CanvasColor.R, shell.Color.R);
        foreach (var key in new[] { OverlayChrome.WellKey, OverlayChrome.RaisedKey, OverlayChrome.SelectionKey })
            Assert.True(((SolidColorBrush)resources[key]).Color.A < 255, key);
        Assert.Equal(0, ((SolidColorBrush)resources[OverlayChrome.CanvasKey]).Color.A);
    }

    [Fact]
    public void OpaqueDefaultsKeepExistingPalette()
    {
        var resources = new ResourceDictionary();
        OverlayChrome.ApplyOpaque(resources);
        Assert.Same(RandyPickTheme.Canvas, resources[OverlayChrome.ShellKey]);
        Assert.Same(RandyPickTheme.Canvas, resources[OverlayChrome.CanvasKey]);
        Assert.Same(RandyPickTheme.Surface, resources[OverlayChrome.WellKey]);
        Assert.Same(RandyPickTheme.Raised, resources[OverlayChrome.RaisedKey]);
        Assert.Same(RandyPickTheme.Selection, resources[OverlayChrome.SelectionKey]);
    }

    [Fact]
    public Task StatsOverlayShellStaysTranslucentRegardlessOfClickThrough() => Sta(() =>
    {
        var window = new StatsOverlayWindow();
        try
        {
            var chrome = (Border)window.FindName("StatsChrome");
            Assert.Equal(204, ((SolidColorBrush)chrome.Background).Color.A);
            window.SetClickThrough(true);
            Assert.Equal(204, ((SolidColorBrush)chrome.Background).Color.A);
            window.SetClickThrough(false);
            Assert.Equal(204, ((SolidColorBrush)chrome.Background).Color.A);
        }
        finally { window.CloseForApplication(); }
    });

    [Fact]
    public Task SharedViewIsTranslucentOnlyUnderOverlayResources() => Sta(() =>
    {
        var opaqueHost = new Border();
        OverlayChrome.ApplyOpaque(opaqueHost.Resources);
        var mainView = new NormalCandidateView();
        opaqueHost.Child = mainView;
        Assert.Same(RandyPickTheme.Canvas, mainView.Background);

        var overlayHost = new Border();
        OverlayChrome.ApplyTranslucent(overlayHost.Resources);
        var overlayView = new NormalCandidateView();
        overlayHost.Child = overlayView;
        Assert.Equal(0, ((SolidColorBrush)overlayView.Background).Color.A);
    });

    private static async Task Sta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); done.SetResult(); } catch (Exception error) { done.SetException(error); } })
            { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
