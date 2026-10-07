using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ReadinessPresentationTests
{
    [Theory]
    [InlineData(GoalCarryMode.SoloPreferred, "1상위 권장")]
    [InlineData(GoalCarryMode.MultiAllowed, "다상위 가능")]
    [InlineData(GoalCarryMode.MultiRequired, "다상위 필요")]
    [InlineData(GoalCarryMode.Unknown, "판단 보류 · 1상위 우선")]
    public void CarryLabelsAreExplicit(GoalCarryMode mode, string expected) =>
        Assert.Equal(expected, RecommendationPresentation.CarryModeLabel(mode));

    [Fact]
    public void PhysicalReadinessShowsAllCoreNumbers()
    {
        var readiness = new CombatReadiness(ReadinessDamageType.Physical,
            1.3, 1.4, 84, 102, 176, 211, 0, 0);
        var line = RecommendationPresentation.ReadinessLine(readiness);
        var readyLine = RecommendationPresentation.ReadinessLine(readiness with
            { CurrentStun = 1.4, CurrentSlow = 102, CurrentArmorReduction = 211 });
        Assert.NotEqual(readyLine.Split('·')[0], line.Split('·')[0]);
        Assert.Contains("스턴 1.3/1.4", line);
        Assert.Contains("이감 84/102", line);
        Assert.Contains("방깎 176/211", line);
    }

    [Fact]
    public void MagicReadinessShowsSourceCountInsteadOfPhysicalArmor()
    {
        var readiness = new CombatReadiness(ReadinessDamageType.Magic,
            1.4, 1.4, 102, 102, 0, 0, 4, 4);
        var line = RecommendationPresentation.ReadinessLine(readiness);
        var unreadyLine = RecommendationPresentation.ReadinessLine(readiness with { CurrentMagicArmorSources = 0 });
        Assert.NotEqual(unreadyLine.Split('·')[0], line.Split('·')[0]);
        Assert.Contains("4/4", line);
        Assert.DoesNotContain("방깎 0/0", line);
    }
}
