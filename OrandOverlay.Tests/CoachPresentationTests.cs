using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CoachPresentationTests
{
    [Fact]
    public void ObservedUnselectedDoesNotReuseUserConfirmation()
    {
        var frame = new CoachFrame { MatchGeneration = 1, Revision = 1, Round = 21,
            CompletedStoryStage = 0, IsCurrent = true, Inventory = ImmutableDictionary<string, int>.Empty,
            ConfirmedNavigation = BulletGuidePolicy.NavigationId,
            NativeNavigation = new(NativeNavigationStatus.Unselected, null, "native unselected") };
        var decision = new CoachDecision(CoachActionKind.Navigation, "navigation", "항법 선택", "선택", "", "", "");
        var alert = CoachPresentation.Create(decision, frame).NavigationAlert;
        Assert.Equal(NativeNavigationPresentation.Describe(frame.NativeNavigation, null), alert);
        Assert.DoesNotContain(NavigationProfiles.Find(BulletGuidePolicy.NavigationId).Name, alert);
    }

    [Fact]
    public void CompiledRenderConsumesDisplayProjectionWithoutChangingPolicy()
    {
        var render = typeof(BeginnerCoachView).GetMethod("Render")!;
        var il = render.GetMethodBody()!.GetILAsByteArray()!;
        var calls = new List<System.Reflection.MethodBase>();
        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6f)) continue;
            try { var m = render.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1)); if (m is not null) calls.Add(m); }
            catch (ArgumentException) { }
        }
        Assert.Contains(calls, m => m.DeclaringType == typeof(CoachPresentation) && m.Name == "Create");
    }

    [Fact]
    public void EssentialBudgetAndDeferredReasonCannotBeHiddenByDensity()
    {
        var frame = new CoachFrame { MatchGeneration = 1, Revision = 1, Round = 9,
            CompletedStoryStage = 0, IsCurrent = true, Inventory = ImmutableDictionary<string, int>.Empty };
        var decision = new CoachDecision(CoachActionKind.Reward, "selection", "확보", "이번 1회", "총 부족 9 / 보유 1 / 예상 잔여 0", "수령 확인", "계획") { RewardWispId = "e018" };
        var property = typeof(CoachPresentation).GetProperty("ShowEssentialReason");
        Assert.NotNull(property);
        Assert.True((bool)property!.GetValue(CoachPresentation.Create(decision, frame))!);
        var held = decision with { Kind = CoachActionKind.Waiting, RewardWispId = null, CraftDeferredForReward = true };
        Assert.True((bool)property.GetValue(CoachPresentation.Create(held, frame))!);
        var craft = decision with { Kind = CoachActionKind.Craft, RewardWispId = null };
        Assert.False((bool)property.GetValue(CoachPresentation.Create(craft, frame))!);
    }

    [Fact]
    public void CurrentUnknownAndConflictingNavigationStayVisible()
    {
        var frame = new CoachFrame { MatchGeneration = 1, Revision = 2, Round = 24,
            CompletedStoryStage = 1, IsCurrent = true, Difficulty = "악몽",
            Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 1),
            Mode = PlayMode.Guide, GuideNumber = 1 };
        var decision = new CoachDecision(CoachActionKind.Gather, "gather", "재료 확보", "조작", "이유", "확인", "계획");
        var unknownAlert = CoachPresentation.Create(decision, frame).NavigationAlert;
        Assert.NotEmpty(unknownAlert);
        var conflict = frame with { NativeNavigation = new(NativeNavigationStatus.Conflict, null, "JP/SP conflict") };
        var conflictAlert = CoachPresentation.Create(decision, conflict).NavigationAlert;
        Assert.NotEmpty(conflictAlert);
        Assert.NotEqual(unknownAlert, conflictAlert);
        var other = frame with { NativeNavigation = new(NativeNavigationStatus.Selected, "AlliedForces.EmergencyCall", "fixture") };
        var selectedAlert = CoachPresentation.Create(decision, other).NavigationAlert;
        Assert.Contains(NavigationProfiles.Find(other.NativeNavigation.OptionId!).Name, selectedAlert);
        Assert.NotEqual(unknownAlert, selectedAlert);
        Assert.NotEqual(conflictAlert, selectedAlert);
        Assert.Equal(decision.Title, CoachPresentation.Create(decision, other).Title);
    }

    [Fact]
    public void WaitingPresentationIsConciseWithoutChangingPlannerDecision()
    {
        var frame = new CoachFrame { MatchGeneration = 0, Revision = 1, Round = 0,
            CompletedStoryStage = 0, IsCurrent = false, Inventory = ImmutableDictionary<string, int>.Empty };
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal("start", decision.Id);
        var type = typeof(CoachDecision).Assembly.GetType("OrandOverlay.CoachPresentation");
        Assert.NotNull(type);
        var view = type!.GetMethod("Create")!.Invoke(null, [decision, frame])!;
        string Value(string name) => (string)view.GetType().GetProperty(name)!.GetValue(view)!;
        Assert.NotEmpty(Value("Title"));
        Assert.NotEmpty(Value("Controls"));
        Assert.Empty(Value("Status"));
        Assert.Empty(Value("NavigationAlert"));
    }
}
