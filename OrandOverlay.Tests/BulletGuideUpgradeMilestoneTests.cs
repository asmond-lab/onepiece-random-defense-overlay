using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideUpgradeMilestoneTests
{
    private readonly DataCatalog _catalog = new();
    public BulletGuideUpgradeMilestoneTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData(50, 29, 1)]
    [InlineData(50, 30, 1)]
    [InlineData(50, 29, 14)]
    [InlineData(50, 30, 14)]
    [InlineData(50, 29, 15)]
    [InlineData(50, 30, 15)]
    [InlineData(59, 29, 1)]
    [InlineData(59, 30, 1)]
    [InlineData(59, 29, 14)]
    [InlineData(59, 30, 14)]
    [InlineData(59, 29, 15)]
    [InlineData(59, 30, 15)]
    [InlineData(60, 29, 1)]
    [InlineData(60, 30, 1)]
    [InlineData(60, 29, 14)]
    [InlineData(60, 30, 14)]
    [InlineData(60, 29, 15)]
    [InlineData(60, 30, 15)]
    public void ExactMilestonesSeparateMinimumFromNextMaximum(int round, int armor, int speed)
    {
        var decision = BulletGuideUpgradePolicy.Decide(Frame(round, armor, speed), _catalog)!;
        Assert.Equal("guide1:upgrade:" + (armor < 30 ? "armor" : "speed"), decision.Id);
        Assert.Contains(armor == 30 ? "50라 최소 목표 달성" : "50라 최소 목표 미달", decision.Reason);
        Assert.Contains($"방깎 {armor}/30 · 공속 {speed}/30", decision.Reason);
        Assert.Contains(round < 60 ? "다음 목표: 60라" : "60라 풀강 목표 미달", decision.Milestone);
        Assert.Contains("각각 30스택", decision.Milestone);
        Assert.DoesNotContain("공격력 강화", decision.Controls);
    }

    [Theory]
    [InlineData(50, 1)]
    [InlineData(50, 2)]
    [InlineData(59, 1)]
    [InlineData(59, 2)]
    [InlineData(60, 1)]
    [InlineData(60, 2)]
    public void UnknownExactSeparatesSourceFromUnverifiedMilestone(int round, int speedTier)
    {
        var frame = Frame(round, 30, 15) with
        { GuideRuntime = new(true, 3, speedTier, 1, "fixture") };
        var decision = BulletGuideUpgradePolicy.Decide(frame, _catalog)!;
        Assert.Equal("guide1:upgrade:speed", decision.Id);
        Assert.Contains("원문 목표: 50라 방깎30 + 공속1~15", decision.Milestone);
        Assert.Contains("60라까지 각각 30스택", decision.Milestone);
        Assert.Contains("정확 스택 미확인 · 50라 최소 목표 달성 여부 미확인", decision.Reason);
        Assert.DoesNotContain("현재 15/30", decision.Reason);
        Assert.Null(frame.GuideRuntime.ExactCounts);
        Assert.Equal(BulletGuideUpgradePolicy.MilestoneSummary(frame), decision.Milestone);
    }

    [Theory]
    [InlineData(50, 15)]
    [InlineData(51, 15)]
    [InlineData(59, 29)]
    public void SpeedContinuesWithoutFifteenStopOrRoundInterpolation(int round, int speed)
    {
        var decision = BulletGuideUpgradePolicy.Decide(Frame(round, 30, speed), _catalog)!;
        Assert.Equal("guide1:upgrade:speed", decision.Id);
        Assert.Contains("공속15에서 강제 중단하지 않고 공속30 선행 가능", decision.Reason);
        Assert.Contains("원문 목표: 50라 방깎30 + 공속1~15", decision.Milestone);
        Assert.Contains("50라 최소 목표 달성", decision.Reason);
    }

    [Fact]
    public void SafetyAndNoAttackSelectionRemainIntact()
    {
        var frame = Frame(50, 30, 15);
        CoachFrame[] blocked = [
            frame with { IsCurrent = false }, frame with { Paused = true },
            frame with { Outcome = "clear" }, frame with { Outcome = "fail" },
            frame with { Round = 49 }, frame with { GuidePlan = null },
            frame with { GuidePlan = frame.GuidePlan! with { Support = new(0, 0, 0, false, null) } },
            frame with { GuideRuntime = BulletGuideRuntimeState.Unknown },
            frame with { GuideRuntime = frame.GuideRuntime.WithExactCounts(new(1, 14, 30)) },
            frame with { Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 21) },
            frame with { Inventory = ImmutableDictionary<string, int>.Empty.Add(BulletGuidePolicy.GoalId, 1) },
            frame with { Inventory = frame.Inventory.SetItem("luffy_common", 1), CommittedCraftUnitId = "luffy_common" },
            Frame(50, 30, 30), Frame(60, 30, 30)
        ];
        foreach (var unsafeFrame in blocked)
            Assert.Null(BulletGuideUpgradePolicy.Decide(unsafeFrame, _catalog));
    }

    [Theory]
    [InlineData(50, 30, 30, "60라 풀강 목표 달성")]
    [InlineData(60, 30, 30, "60라 풀강 목표 달성")]
    [InlineData(50, 30, 15, "다음 목표: 60라")]
    [InlineData(60, 30, 15, "60라 풀강 목표 미달")]
    public void SummarySurvivesNoPaymentAndCompletedUpgrade(int round, int armor, int speed, string expected)
    {
        var frame = Frame(round, armor, speed) with
        { Inventory = ImmutableDictionary<string, int>.Empty.Add(BulletGuidePolicy.GoalId, 1) };
        Assert.Null(BulletGuideUpgradePolicy.Decide(frame, _catalog));
        Assert.Contains(expected, BulletGuideAdvice.SupportSummary(frame));
        Assert.Contains(expected, BulletGuideUpgradePolicy.MilestoneSummary(frame));
        Assert.Contains("50라 최소 목표 달성", BulletGuideUpgradePolicy.MilestoneSummary(frame));
    }

    [Fact]
    public void SummaryDoesNotPromoteStaleExactCountsToKnown()
    {
        var frame = Frame(60, 30, 30);
        frame = frame with { GuideRuntime = frame.GuideRuntime with { IsCurrent = false } };
        Assert.Contains("달성 여부 미확인", BulletGuideAdvice.SupportSummary(frame));
        Assert.DoesNotContain("60라 풀강 목표 달성", BulletGuideAdvice.SupportSummary(frame));
        Assert.Contains("달성 여부 미확인", BulletGuideUpgradePolicy.MilestoneSummary(frame));
        Assert.DoesNotContain("목표 달성.", BulletGuideUpgradePolicy.MilestoneSummary(frame));
        Assert.Equal("", BulletGuideUpgradePolicy.MilestoneSummary(frame with { IsCurrent = false }));
    }
    [Fact]
    public void ExactThirtyArmorAndSpeedAtRound60SurfacesCompletedMaximum()
    {
        var frame = Frame(60, 30, 30);
        Assert.Equal(new BulletUpgradeCounts(1, 30, 30), frame.GuideRuntime.ExactCounts);
        Assert.Contains("60라 풀강 목표 달성", BulletGuideUpgradePolicy.MilestoneSummary(frame));
        Assert.Contains("60라 풀강 목표 달성", BulletGuideAdvice.SupportSummary(frame));
    }

    private static CoachFrame Frame(int round, int armor, int speed) => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
        Round = round, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
        Inventory = ImmutableDictionary<string, int>.Empty.Add(BulletGuidePolicy.GoalId, 1).Add("luffy_common", 21),
        GuidePlan = new(BulletGuideStage.Operating, null, true) { Support = new(100, 82, 100, true, null) },
        GuideRuntime = new BulletGuideRuntimeState(true, Tier(armor), Tier(speed), 1, "fixture")
            .WithExactCounts(new(1, speed, armor))
    };

    private static int Tier(int count) => count == 30 ? 3 : count >= 15 ? 2 : 1;
}
