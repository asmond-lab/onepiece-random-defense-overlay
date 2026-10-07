using Xunit;

namespace OrandOverlay.Tests;

public sealed class BeginnerCoachSettingsTests
{
    [Fact]
    public void ReturningToAutomaticGoalDoesNotEraseManualNavigation()
    {
        var coordinator = new AdaptivePlanningCoordinator();
        coordinator.LatchManualGoalOverride();
        coordinator.LatchManualNavigationOverride();
        coordinator.ClearManualGoalOverride();
        Assert.False(coordinator.ManualLatches.GoalOverride);
        Assert.True(coordinator.ManualLatches.NavigationOverride);
    }

    [Fact]
    public void NewSettingsStartWithBeginnerGuidance()
    {
        Assert.True(new AppSettings().BeginnerCoachEnabled);
    }

    [Fact]
    public void ExpertModeAndFixedGoalSurviveSaving()
    {
        var directory = Directory.CreateTempSubdirectory("orand-coach-settings-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            SettingsStore.Save(new AppSettings
            {
                BeginnerCoachEnabled = false, GoalUnitId = "rawcode:F40h",
                ManualGoalUnitId = "rawcode:F40h", AutoStartGoal = false
            }, path);
            var loaded = SettingsStore.Load(path);
            Assert.False(loaded.BeginnerCoachEnabled);
            Assert.False(loaded.AutoStartGoal);
            Assert.Equal("rawcode:F40h", loaded.GoalUnitId);
            Assert.Equal("rawcode:F40h", loaded.ManualGoalUnitId);
        }
        finally { directory.Delete(true); }
    }
}
