using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletDeadlineReplayTests
{
    private readonly DataCatalog _catalog = new();
    public BulletDeadlineReplayTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData(680)]
    [InlineData(889)]
    [InlineData(1076)]
    public void FailedSessionMissingSmokerPrecedesOptionalSupport(int sequence)
    {
        var sample = Samples().Single(item => item.GetProperty("Sequence").GetInt32() == sequence);
        var inventory = sample.GetProperty("Inventory").Deserialize<Dictionary<string, int>>()!;
        var plan = new BulletGuidePolicy(_catalog).Plan(sample.GetProperty("Round").GetInt32(),
            sample.GetProperty("CompletedStoryStage").GetInt32(), inventory, "악몽");
        Assert.Equal(BulletGuideStage.BulletMaterials, plan.Stage);
        Assert.Equal("rawcode:V20h", plan.TargetUnitId);
        Assert.Contains("rawcode:U20h", plan.ProtectedUnitIds);
        Assert.Contains("rawcode:930h", plan.ProtectedUnitIds);
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(_catalog),
            Goal = _catalog.Unit(BulletGuidePolicy.GoalId),
            Inventory = inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray(),
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = "Unselected",
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽",
            Round = plan.Round, CompletedStoryStage = 13
        });
        Assert.Equal("rawcode:V20h", Assert.Single(candidates.Recommendations).Route.GoalUnitId);
        var decision = BulletGuideAdvice.Gather(new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = sequence,
            Round = plan.Round, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
            GuidePlan = plan, Inventory = inventory.ToImmutableDictionary(), Recommendations = candidates.Recommendations
        }, _catalog);
        Assert.Equal("rawcode:V20h", decision.TargetUnitId);
        Assert.Contains(decision.Kind, new[] { CoachActionKind.Gather, CoachActionKind.Waiting });
    }

    [Fact]
    public void LaterSessionAlreadyHasAllComponentsAndDoesNotRepeatThem()
    {
        var sample = Samples().Single(item => item.GetProperty("Sequence").GetInt32() == 930);
        var inventory = sample.GetProperty("Inventory").Deserialize<Dictionary<string, int>>()!;
        var plan = new BulletGuidePolicy(_catalog).Plan(40, 13, inventory, "악몽");
        Assert.Equal(3, plan.ComponentCount);
        Assert.NotEqual(BulletGuideStage.BulletMaterials, plan.Stage);
        Assert.NotEqual(BulletGuidePolicy.GoalId, plan.TargetUnitId);
    }

    private static JsonElement[] Samples()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OrandOverlay.csproj")))
            directory = directory.Parent;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName,
            "OrandOverlay.Tests", "Fixtures", "bullet-deadline-replay.json")));
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    [Theory]
    [InlineData(0, BulletGuideStage.RoundUnknown)]
    [InlineData(6, BulletGuideStage.FastUniqueRare)]
    [InlineData(10, BulletGuideStage.FirstLegend)]
    public void DeadlineDoesNotBypassOpeningAndUnknownRound(int round, BulletGuideStage stage)
    {
        Assert.Equal(stage, new BulletGuidePolicy(_catalog).Plan(round, 0,
            new Dictionary<string, int>(), "악몽").Stage);
    }

    [Fact]
    public void LateSingleLegendDoesNotKeepFillingOptionalSecondLegend()
    {
        var plan = new BulletGuidePolicy(_catalog).Plan(30, 10,
            new Dictionary<string, int> { ["rawcode:U20h"] = 1 }, "악몽");
        Assert.Equal(BulletGuideStage.BulletMaterials, plan.Stage);
        Assert.Contains(plan.TargetUnitId, new[] { "rawcode:V20h", "rawcode:930h" });
    }

    [Fact]
    public void FourthLegendStoryRewardStillPrecedesDeadline()
    {
        var inventory = new[] { "U20h", "930h", "HA0h" }.ToDictionary(code => "rawcode:" + code, _ => 1);
        Assert.Equal(BulletGuideStage.FourthLegendReward,
            new BulletGuidePolicy(_catalog).Plan(30, 9, inventory, "악몽").Stage);
    }

    [Theory]
    [InlineData(1, 100, true)]
    [InlineData(0, 100, false)]
    [InlineData(1, 0, false)]
    public void DeadlineSelectionUsesOnlyOwnedWispsAndVerifiedCraftBudget(int wisps, int lumber, bool expected)
    {
        var hand = new RecipeCompletionCalculator(_catalog.Unit).Calculate(["rawcode:V20h"],
            ImmutableDictionary<string, int>.Empty).Leaves.ToImmutableDictionary(leaf => leaf.UnitId,
                leaf => checked((int)leaf.RequiredCount));
        var common = hand.Keys.First(id => _catalog.Unit(id).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains));
        var frame = new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
            Round = 40, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
            Inventory = hand.SetItem(common, hand[common] - 1),
            GuidePlan = new(BulletGuideStage.BulletMaterials, "rawcode:V20h", false),
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", wisps),
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", lumber)
        };
        var decision = BulletGuideSelectionPolicy.Decide(frame, _catalog);
        Assert.Equal(expected, decision is not null);
        if (expected)
        {
            Assert.Equal("e018", decision!.RewardWispId);
            Assert.Equal(common, decision.TargetUnitId);
            Assert.Equal(1, decision.SelectionBatch!.RemainingCount);
        }
    }

    [Fact]
    public void ObservedLineDangerStillWinsOverDeadlinePreparation()
    {
        var frame = new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
            Round = 40, CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽",
            Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:U20h", 1),
            GuidePlan = new(BulletGuideStage.BulletMaterials, "rawcode:V20h", false),
            Signals = ImmutableDictionary<string, long?>.Empty.Add("line-count", 70)
        };
        Assert.Equal("line-survival", new BeginnerCoachPlanner(_catalog).Decide(frame).Id);
    }
}
