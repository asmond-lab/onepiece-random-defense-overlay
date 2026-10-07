using Xunit;
namespace OrandOverlay.Tests;

public sealed class BetaPlayModesTests
{
    [Theory]
    [InlineData(PlayMode.Beginner)]
    [InlineData(PlayMode.Guide)]
    [InlineData(PlayMode.Manual)]
    [InlineData((PlayMode)999)]
    public void LockedAndUnknownModesRestoreNormalWithoutDiscardingGoals(PlayMode saved)
    {
        var settings = new AppSettings { Mode = saved, ManualGoalUnitId = "rawcode:F40h",
            GoalUnitId = "rawcode:F40h", SecondaryGoalUnitId = "rawcode:H90H", GuideNumber = 1 };
        BetaPlayModes.Normalize(settings);
        Assert.Equal(PlayMode.Normal, settings.Mode);
        Assert.False(settings.BeginnerCoachEnabled);
        Assert.True(settings.AutoStartGoal);
        Assert.Equal("rawcode:F40h", settings.ManualGoalUnitId);
        Assert.Equal("rawcode:H90H", settings.SecondaryGoalUnitId);
        Assert.Equal(1, settings.GuideNumber);
        Assert.False(BetaPlayModes.IsAvailable(saved));
    }

    [Fact]
    public void FreshLegacyAndNormalSettingsUseTheOnlyOpenMode()
    {
        foreach (var settings in new[] { new AppSettings(), new AppSettings { Mode = null, AutoStartGoal = false }, new AppSettings { Mode = PlayMode.Normal } })
        {
            BetaPlayModes.Normalize(settings);
            Assert.Equal(PlayMode.Normal, settings.Mode);
        }
        Assert.Equal(PlayMode.Normal, Assert.Single(PlayModes.Options.Where(o => o.IsBetaAvailable)).Mode);
    }
}
