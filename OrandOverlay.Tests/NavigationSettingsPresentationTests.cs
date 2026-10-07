using System.Windows;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NavigationSettingsPresentationTests
{
    [Fact]
    public void ManualRecommendationSelectorsAreNeverShown()
    {
        Assert.Equal(Visibility.Collapsed, MainWindow.NavigationSelectionVisibility(true));
        Assert.Equal(Visibility.Collapsed, MainWindow.NavigationSelectionVisibility(false));
    }
}
