using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideCraftSafetyTests
{
    private readonly DataCatalog _catalog = new();
    public BulletGuideCraftSafetyTests() => _catalog.Load(loadCarryPolicy: false);

    [Fact]
    public void SourceReadyRoundFiftyPipelineCanActuallyReachBulletCraft()
    {
        var inventory = new[] { "930h", "V20h", "U20h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Y30h", "Q30h", "K50h" }
            .Select(code => new InventoryEntry { UnitId = _catalog.AllUnits.First(unit => unit.Rawcodes.Contains(code)).Id, Count = 1 }).ToArray();
        var plan = new BulletGuidePolicy(_catalog).Plan(50, 13, inventory.ToDictionary(entry => entry.UnitId, entry => entry.Count),
            "악몽", BulletGuidePolicy.NavigationId);
        Assert.Equal(BulletGuideStage.CraftBullet, plan.Stage);
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(_catalog, combineHotkeys: hotkeys),
            Goal = _catalog.Unit(BulletGuidePolicy.GoalId), Inventory = inventory,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 50, CompletedStoryStage = 13
        });
        var steps = new AutoCombinePlanner(_catalog, hotkeys).Plan(candidates.Recommendations, inventory);
        Assert.Contains(steps, step => step.TargetUnitId == BulletGuidePolicy.GoalId);
    }

    [Fact]
    public void WarcuryCraftCannotSpendArmorAboveOrdinaryHundredFloor()
    {
        var counts = new[] { "180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Q30h", "K50h",
            "HA0h", "I20h", "L00h" }.ToDictionary(code => "rawcode:" + code, _ => 1);
        var support = new BulletGuideSupportPolicy(_catalog).Evaluate(counts,
            BulletGuidePolicy.NavigationId, GoroseiMode.Warcury);
        Assert.Equal(118, support.ArmorPotential);
        var plan = new BulletGuidePlan(BulletGuideStage.ControlSupport, "rawcode:IC0h", true)
            { Round = 50, ConfirmedNavigation = BulletGuidePolicy.NavigationId, Support = support };
        var inventory = counts.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(_catalog, combineHotkeys: hotkeys),
            Goal = _catalog.Unit(BulletGuidePolicy.GoalId), Inventory = inventory,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.Warcury, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 50, CompletedStoryStage = 13
        });
        var steps = new AutoCombinePlanner(_catalog, hotkeys).Plan(candidates.Recommendations, inventory);
        Assert.DoesNotContain(steps, step => step.TargetUnitId == "rawcode:IC0h");
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, false, true)]
    [InlineData(1, true, true)]
    public void LastRareStunNeedsReplacementBeforeMobyDickPromotion(int jozu, bool barto, bool allowed)
    {
        var target = _catalog.Unit("rawcode:Q30h");
        var inventory = target.Recipe.ToDictionary(pair => pair.Key, pair => pair.Value);
        inventory["rawcode:180h"] = 1;
        inventory["rawcode:E20h"] = jozu;
        if (barto) inventory["rawcode:Z20h"] = 1;
        Assert.Equal(allowed, BulletGuideCraftSafety.Allows(_catalog, target.Id, inventory, 50, null));
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    [InlineData(1, true, true)]
    public void AuxiliaryRareChopperDoesNotBlockMainCraft(int chopper, bool cracker, bool allowed)
    {
        var target = _catalog.Unit("rawcode:S30h");
        var inventory = target.Recipe.ToDictionary(pair => pair.Key, pair => pair.Value);
        inventory["rawcode:180h"] = 1;
        inventory["rawcode:K20h"] = chopper;
        if (cracker) inventory["rawcode:H30h"] = 1;
        Assert.Equal(allowed, BulletGuideCraftSafety.Allows(_catalog, target.Id, inventory, 50, null));
    }

    [Theory]
    [InlineData("rawcode:2B0H", "rawcode:U30h")]
    [InlineData("rawcode:W80H", "rawcode:M30h")]
    [InlineData("rawcode:A40h", "rawcode:Z20h")]
    public void PrimaryBossSlowAndStunStillNeedSurvivingReplacement(string targetId, string roleId)
    {
        var target = _catalog.Unit(targetId);
        var roleCodes = _catalog.Unit(roleId).Rawcodes;
        roleId = target.Recipe.Keys.Single(id => _catalog.Unit(id).Rawcodes.Intersect(roleCodes).Any());
        var inventory = target.Recipe.ToDictionary(pair => pair.Key, pair => pair.Value);
        inventory[BulletGuidePolicy.GoalId] = 1;
        Assert.True(inventory.GetValueOrDefault(roleId) > 0);
        Assert.NotNull(BulletGuideCraftSafety.ProjectAfterCraft(_catalog, targetId, inventory));
        Assert.False(BulletGuideCraftSafety.Allows(_catalog, targetId, inventory, 50, null));
        // The boss policy retains up to two bodies, so leave two after spending one.
        inventory[roleId] += 2;
        Assert.True(BulletGuideCraftSafety.Allows(_catalog, targetId, inventory, 50, null));
    }

    [Fact]
    public void EarlyBulletCannotBypassGuideByInjectingAReadyCraftStep()
    {
        var inventory = new Dictionary<string, int> { ["rawcode:930h"] = 1, ["rawcode:V20h"] = 1, ["rawcode:U20h"] = 1 };
        Assert.False(BulletGuideCraftSafety.Allows(_catalog, BulletGuidePolicy.GoalId, inventory, 49, null));
    }

    [Fact]
    public void CommonReserveIsProtectedByActualRecipeAllocation()
    {
        var unit = _catalog.AllUnits.First(unit => unit.Recipe.Count > 0 && unit.Recipe.Keys.All(id =>
            _catalog.Unit(id).Tier == "흔함"));
        var inventory = unit.Recipe.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.False(BulletGuideCraftSafety.Allows(_catalog, unit.Id, inventory, 50, null));
    }

    [Fact]
    public void FullRecipeNoResourceObservationNeverEmitsGuideCraft()
    {
        var unit = _catalog.Unit("rawcode:530h");
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with
        {
            Mode = PlayMode.Guide, GuideNumber = 1, Round = 15, CompletedStoryStage = 9,
            GuidePlan = new(BulletGuideStage.FirstLegend, unit.Id, false),
            Inventory = unit.Recipe.Where(pair => _catalog.Unit(pair.Key).Tier != "자원")
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Value),
            CraftSteps = [new(unit.Id, unit.Name, unit.Recipe.Keys.First(), "fixture", "", "fixture-key", [])]
        };
        var result = new BeginnerCoachPlanner(_catalog).Decide(frame);
        Assert.NotEqual(CoachActionKind.Craft, result.Kind);
        Assert.DoesNotContain("fixture-key", result.Controls);
    }
}
