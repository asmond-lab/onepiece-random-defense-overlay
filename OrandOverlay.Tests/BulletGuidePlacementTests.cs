using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuidePlacementTests
{
    // Reflection lets the missing feature fail an assertion, not compilation.
    private static string? Observe(CoachFrame frame)
    {
        var type = typeof(CoachFrame).Assembly.GetType("OrandOverlay.BulletGuidePlacementPolicy");
        Assert.NotNull(type);
        var method = type.GetMethod("Summary");
        Assert.NotNull(method);
        return (string?)method.Invoke(null, [frame]);
    }

    [Fact]
    public void ReportsCurrentDistanceSeparatelyFromSourceTarget()
    {
        var text = Observe(Frame());
        Assert.NotNull(text);
        Assert.Contains("현재 거리 500", text);
        Assert.Contains("일반 라운드 생성 중심 (-4960, 5632)", text);
        Assert.Contains("원문 목표 약 400", text);
    }

    [Theory]
    [InlineData(0, -5024, 5600, -4896, 5664)]
    [InlineData(1, 992, 5600, 1120, 5664)]
    [InlineData(2, -5024, 480, -4896, 544)]
    [InlineData(3, 992, 480, 1120, 544)]
    public void UsesPinnedSwRectCenters(byte owner, float left, float bottom, float right, float top)
    {
        var x = (left + right) / 2;
        var y = (bottom + top) / 2;
        var frame = Frame();
        var bullet = frame.CombatObservations[0] with { Owner = owner, Position = new(x, y) };
        var text = Observe(frame with { CombatObservations = [bullet] });
        Assert.Contains(FormattableString.Invariant($"생성 중심 ({x:0}, {y:0})"), text);
        Assert.Contains("현재 거리 0 ·", text);
        Assert.Contains("현재 거리 500 ·", Observe(frame with
            { CombatObservations = [bullet with { Position = new(x + 300, y + 400) }] }));
    }

    [Fact]
    public void SuppressesFramesWithoutCurrentUsableGuideContext()
    {
        var frame = Frame();
        foreach (var invalid in new[]
        {
            frame with { IsCurrent = false }, frame with { Paused = true },
            frame with { Outcome = "clear" }, frame with { Outcome = "fail" },
            frame with { Difficulty = "unknown" }, frame with { Mode = PlayMode.Beginner },
            frame with { GuideNumber = 2 }, frame with { GuidePlan = null },
            frame with { GuidePlan = new(BulletGuideStage.Operating, null, false) },
            frame with { Inventory = ImmutableDictionary<string, int>.Empty }
        }) Assert.Null(Observe(invalid));
    }

    [Fact]
    public void RequiresExactlyOneValidAliveLocalBullet()
    {
        var frame = Frame();
        var bullet = frame.CombatObservations[0];
        Assert.Null(Observe(frame with { CombatObservations = [bullet, bullet with { SampleId = 2 }] }));
        Assert.Null(Observe(frame with { CombatObservations = [] }));
        foreach (var invalid in new[]
        {
            bullet with { Owner = 4 }, bullet with { Kind = CombatUnitKind.LocalUnit },
            bullet with { Rawcode = "h082" }, bullet with { Life = 0 },
            bullet with { Life = null }, bullet with { Life = float.NaN },
            bullet with { Life = float.PositiveInfinity }, bullet with { Position = null },
            bullet with { Position = new(float.NaN, 0) },
            bullet with { Position = new(0, float.PositiveInfinity) }
        }) Assert.Null(Observe(frame with { CombatObservations = [invalid] }));
    }

    private static CoachFrame Frame() => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
        Round = 60, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
        Inventory = ImmutableDictionary<string, int>.Empty.Add(BulletGuidePolicy.GoalId, 1),
        GuidePlan = new(BulletGuideStage.Operating, null, true),
        CombatObservations = [new(1, "h081", 0, null, CombatUnitKind.Bullet,
            new(-4660, 6032), 100, 100, 0, false, false)]
    };
}
