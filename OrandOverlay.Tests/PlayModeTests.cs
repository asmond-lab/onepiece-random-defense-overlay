using Xunit;

namespace OrandOverlay.Tests;

public sealed class PlayModeTests
{
    [Theory]
    [InlineData(PlayMode.Beginner)]
    [InlineData(PlayMode.Normal)]
    [InlineData(PlayMode.Guide)]
    [InlineData(PlayMode.Manual)]
    public void NavigationIsAlwaysAutomaticWhenSettingsAreLoaded(PlayMode mode)
    {
        var directory = Directory.CreateTempSubdirectory("orand-modes-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            SettingsStore.Save(new AppSettings
            {
                Mode = mode, AutoRecommendNavigation = false,
                GoalUnitId = "rawcode:F40h", SecondaryGoalUnitId = "rawcode:H90H"
            }, path);
            var settings = SettingsStore.Load(path);
            Assert.Equal(mode, PlayModes.Current(settings));
            Assert.True(settings.AutoRecommendNavigation);
            Assert.Equal("rawcode:H90H", settings.SecondaryGoalUnitId);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(PlayMode.Beginner, true)]
    [InlineData(PlayMode.Normal, true)]
    [InlineData(PlayMode.Guide, false)]
    [InlineData(PlayMode.Manual, false)]
    public void OnlyPresentationDiffersBetweenAutomaticModes(PlayMode mode, bool automatic)
    {
        Assert.Equal(automatic, PlayModes.AutomaticGoals(mode));
    }
}
