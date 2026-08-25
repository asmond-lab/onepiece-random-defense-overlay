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
        var line = RecommendationPresentation.ReadinessLine(
            new CombatReadiness(ReadinessDamageType.Physical,
                1.3, 1.4, 84, 102, 176, 211, 0, 0));

        Assert.Contains("55라 준비 미달", line);
        Assert.Contains("스턴 1.3/1.4", line);
        Assert.Contains("이감 84/102", line);
        Assert.Contains("방깎 176/211", line);
    }

    [Fact]
    public void MagicReadinessShowsSourceCountInsteadOfPhysicalArmor()
    {
        var line = RecommendationPresentation.ReadinessLine(
            new CombatReadiness(ReadinessDamageType.Magic,
                1.4, 1.4, 102, 102, 0, 0, 1, 1));

        Assert.Contains("55라 준비 완료", line);
        Assert.Contains("마방깎 공급원 1/1", line);
        Assert.DoesNotContain("방깎 0/0", line);
    }
}
