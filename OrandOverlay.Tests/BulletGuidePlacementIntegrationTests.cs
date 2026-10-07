using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuidePlacementIntegrationTests
{
    [Fact]
    public void ProductionSupportSummaryIncludesCurrentPlacementInformation()
    {
        var frame = new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
            Round = 60, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
            Inventory = ImmutableDictionary<string, int>.Empty.Add(BulletGuidePolicy.GoalId, 1),
            GuidePlan = new(BulletGuideStage.Operating, null, true)
            {
                Support = new(0, 0, 100, false, null)
            },
            CombatObservations = [new(1, "h081", 0, null, CombatUnitKind.Bullet,
                new(-4660, 6032), 100, 100, 0, false, false)]
        };
        var text = BulletGuideAdvice.SupportSummary(frame);
        Assert.Contains("일반 라운드 생성 중심", text);
        Assert.Contains("현재 거리 500", text);
        Assert.Contains("원문 목표 약 400", text);
    }
}
