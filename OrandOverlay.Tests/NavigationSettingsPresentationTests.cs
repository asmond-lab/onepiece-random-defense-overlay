using System.Windows;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NavigationSettingsPresentationTests
{
    [Fact]
    public void AutomaticRecommendationHidesOnlyTheManualSelectors()
    {
        Assert.Equal(Visibility.Collapsed, MainWindow.NavigationSelectionVisibility(true));
        Assert.Equal(Visibility.Visible, MainWindow.NavigationSelectionVisibility(false));
    }
}
