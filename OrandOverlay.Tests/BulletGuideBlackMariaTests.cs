using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletGuideBlackMariaTests
{
    [Theory]
    [InlineData("stun", "guide1:blackmaria:Stun")]
    [InlineData("slow", "guide1:blackmaria:Slow")]
    [InlineData("ready", null)]
    [InlineData("unknown", null)]
    public void RoleAdviceUsesActualPlanWithoutCreditingSelection(string state, string? expectedId)
    {
        string[] support = state switch {
            "stun" => ["Q30h", "M30h", "K50h"],
            "slow" => ["Z20h"],
            "ready" => ["Q30h", "M30h", "K50h", "Z20h"], _ => [] };
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(new[] { "180h", "U40h" }.Concat(support).ToArray());
        if (state == "unknown") frame = frame with { GuidePlan = frame.GuidePlan! with { Support = null } };
        var roleDecision = BulletGuideBlackMariaPolicy.Decide(frame);
        Assert.Equal(expectedId, roleDecision?.Id);
        if (state == "unknown") Assert.Null(frame.GuidePlan!.Support);
        else
        {
            Assert.Equal(state != "stun", frame.GuidePlan!.Support!.StunPairReady);
            Assert.Equal(state != "slow", frame.GuidePlan.Support.SlowPotential >= 82);
        }
        Assert.Null(BlackMariaObservation.Selected(frame));
        var summary = BulletGuideBlackMariaPolicy.Summary(frame);
        Assert.Contains(summary, new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame).OperationGuide);
        Assert.Contains(summary, BulletGuideAdvice.SupportSummary(frame));
    }

    [Theory]
    [InlineData("A09R", "스턴")][InlineData("A0T6", "화상")]
    public void NativeSelectionFlowsToPlannerAndDoesNotCompleteSupport(string ability, string label)
    {
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(["180h", "U40h", "Z20h"], new BlackMariaReaderTests.Bytes(ability).Observe());
        var before = frame.GuidePlan!.Support;
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal("guide1:blackmaria:Slow", decision.Id);
        Assert.Equal("rawcode:U40h", decision.TargetUnitId);
        Assert.Contains("선택 관측: " + label, decision.Controls);
        Assert.Contains("선택 관측: " + label, BulletGuideAdvice.SupportSummary(frame));
        Assert.Equal(ability == "A09R" ? BlackMariaMode.Stun : BlackMariaMode.Burn, BlackMariaObservation.Selected(frame));
        Assert.Equal(BulletGuideBlackMariaPolicy.Decide(frame)!.UnknownSignals, decision.UnknownSignals);
        Assert.Equal(before, frame.GuidePlan.Support);
        Assert.Equal(7, before!.SlowPotential);
        Assert.DoesNotContain("화상 선택 권고", decision.Controls);
    }

    [Theory]
    [InlineData("320h")][InlineData("X10h")]
    public void TwoUnreservedSameRareUnitsReachConsiderationNotCraft(string code)
    {
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(BulletGuideUncommonSaleTests.OperatingSupportCodes().Concat(new[] { code, code }).ToArray());
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal("guide1:blackmaria:consider", decision.Id);
        Assert.NotEqual(CoachActionKind.Craft, decision.Kind);
        Assert.Contains("획득 경로 미확인", decision.UnknownSignals);
        Assert.Contains("사용자 보충", decision.Reason);
        Assert.Contains("즉시 조합", decision.Controls);
        Assert.Equal(2, frame.Inventory["rawcode:" + code]);
    }

    [Theory]
    [InlineData("split")][InlineData("common")][InlineData("oneKaku")][InlineData("oneLuffy")]
    [InlineData("selected")][InlineData("committed")][InlineData("protected")][InlineData("target")]
    public void DuplicateConsiderationPreservesTierCountsAndReservations(string blocked)
    {
        string[] extras = blocked switch { "split" => ["320h", "X10h"], "common" => ["300h", "300h"],
            "oneKaku" => ["320h"], "oneLuffy" => ["X10h"], _ => ["320h", "320h"] };
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(BulletGuideUncommonSaleTests.OperatingSupportCodes().Concat(extras).ToArray());
        frame = blocked switch {
            "selected" => frame with { SelectedGoalIds = ["rawcode:320h"] },
            "committed" => frame with { CommittedCraftUnitId = "rawcode:320h" },
            "protected" => frame with { GuidePlan = frame.GuidePlan! with { ProtectedUnitIds = ["rawcode:320h"] } },
            "target" => frame with { GuidePlan = frame.GuidePlan! with { TargetUnitId = "rawcode:320h" } }, _ => frame };
        Assert.NotEqual("guide1:blackmaria:consider", new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame).Id);
    }

    [Theory]
    [InlineData("stale")][InlineData("pause")][InlineData("fail")][InlineData("clear")]
    [InlineData("difficulty")][InlineData("mode")][InlineData("guide")][InlineData("legend")]
    public void UnsafeFramesDoNotRecommendBlackMaria(string blocked)
    {
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(["180h", "U40h", "Z20h"]);
        frame = blocked switch {
            "stale" => frame with { IsCurrent = false }, "pause" => frame with { Paused = true },
            "fail" => frame with { Outcome = "fail" }, "clear" => frame with { Outcome = "clear" },
            "difficulty" => frame with { Difficulty = "unknown" }, "mode" => frame with { Mode = PlayMode.Beginner },
            "guide" => frame with { GuideNumber = 2 },
            _ => frame with { Inventory = frame.Inventory.Remove("rawcode:U40h").Add("rawcode:X20h", 1) } };
        Assert.Empty(BulletGuideBlackMariaPolicy.Summary(frame));
        Assert.Null(BulletGuideBlackMariaPolicy.Decide(frame));
        Assert.DoesNotContain("blackmaria", new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame).Id);
    }

    [Theory]
    [InlineData("foreign")][InlineData("dead")][InlineData("missing")][InlineData("multiple")]
    [InlineData("stale")][InlineData("generation")][InlineData("revision")][InlineData("inventoryMultiplicity")]
    public void UntrustedFrameSelectionIsUnknownWithoutNumericCredit(string blocked)
    {
        var unit = new BlackMariaReaderTests.Bytes("A09R").Observe()!;
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(["180h", "U40h", "Z20h"], unit);
        frame = blocked switch {
            "foreign" => frame with { CombatObservations = [unit with { Owner = 7, Kind = CombatUnitKind.RecipeExemplar }] },
            "dead" => frame with { CombatObservations = [unit with { Life = 0 }] },
            "missing" => frame with { CombatObservations = [] },
            "multiple" => frame with { CombatObservations = [unit, unit with { SampleId = 2 }] },
            "stale" => frame with { IsCurrent = false }, "generation" => frame with { MatchGeneration = 0 },
            "revision" => frame with { Revision = 0 },
            _ => frame with { Inventory = frame.Inventory.SetItem("rawcode:U40h", 2) } };
        Assert.Null(BlackMariaObservation.Selected(frame));
        Assert.Equal(7, frame.GuidePlan!.Support!.SlowPotential);
    }

    [Fact]
    public void BothMissingAndUnknownNeverChooseOneMode()
    {
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(["180h", "U40h"]);
        Assert.Null(BulletGuideBlackMariaPolicy.Decide(frame));
        Assert.Null(BulletGuideBlackMariaPolicy.Decide(frame with { GuidePlan = frame.GuidePlan! with { Support = null } }));
    }

    [Theory]
    [InlineData("A0T7", "이감")][InlineData("A09R", "스턴")]
    public void AlreadySelectedRoleYieldsToExistingSaleWithoutCreditingSupport(string ability, string label)
    {
        var codes = ability == "A0T7"
            ? BulletGuideUncommonSaleTests.OperatingSupportCodes().Where(code => code is not ("Q30h" or "K50h" or "M30h"))
            : BulletGuideUncommonSaleTests.OperatingSupportCodes().Where(code => code is not ("O30h" or "Y30h"));
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(codes.Concat(new[] { "U40h", "D20h", "Y00h" }).ToArray(),
            new BlackMariaReaderTests.Bytes(ability).Observe());
        var before = frame.GuidePlan!.Support;
        Assert.NotNull(before);
        Assert.True(ability == "A0T7" ? before.SlowPotential < 82 && before.StunPairReady : before.SlowPotential >= 82 && !before.StunPairReady);
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal("guide1:sell-special:rawcode:Y00h", decision.Id);
        Assert.Contains("선택 관측: " + label, decision.OperationGuide);
        Assert.Contains("선택 관측: " + label, BulletGuideAdvice.SupportSummary(frame));
        Assert.Contains("이미 " + label + " 선택 관측: 재선택 지시 없이 다음 행동 진행", decision.OperationGuide);
        Assert.Equal(before, frame.GuidePlan.Support);
    }

    [Fact]
    public void OwnedDistortionWithoutDuplicatesReachesProductionAdvice()
    {
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(["180h", "U40h"]);
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Contains("왜곡 블랙마리아", decision.OperationGuide);
        Assert.Contains("스턴·이감 모두 부족", decision.OperationGuide);
        Assert.Contains("고정 우선순위 없음", decision.OperationGuide);
        Assert.Contains("왜곡 블랙마리아", BulletGuideAdvice.SupportSummary(frame));
    }
}
