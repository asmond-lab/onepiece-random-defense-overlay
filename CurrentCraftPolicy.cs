namespace OrandOverlay;

internal sealed record CurrentCraftAssessment(
    bool MaterialsReady,
    int Priority,
    GoalStrategyProfile Strategy,
    bool LosesRequiredCombat = false,
    long GoalMaterialLoss = 0,
    bool NeedsResourceConfirmation = false,
    StrategyMetrics LostCoverage = default);

/// <summary>
/// Evaluates one action against the live inventory, never a promised final composition.
/// Lower priorities mean earlier actions; final-build ranking remains a separate concern.
/// </summary>
internal sealed class CurrentCraftPolicy(
    Func<string, UnitDefinition> resolve,
    UnitDefinition goal,
    IReadOnlyDictionary<string, int> inventory,
    GoalStrategyProfile strategy,
    int round,
    int completedStoryStage,
    RecipeCompletionCalculator calculator,
    RecipeConditionContext? conditionContext = null)
{
    private readonly StrategyMetrics _current = Metrics(resolve, inventory);

    internal CurrentCraftAssessment Assess(UnitDefinition unit)
    {
        var materials = unit.Recipe.Where(pair => !IsResource(resolve(pair.Key))).ToList();
        var resources = unit.Recipe.Where(pair => IsResource(resolve(pair.Key))).ToList();
        var consumed = RecipeWildcards.AllocateDirect(unit, inventory, resolve);
        var ready = inventory.GetValueOrDefault(unit.Id) <= 0 &&
                    materials.Count > 0 && consumed is not null &&
                    RecipeConditionEvaluator.Evaluate(unit, conditionContext).IsSatisfied &&
                    resources.All(pair => !inventory.ContainsKey(pair.Key) ||
                        inventory[pair.Key] >= pair.Value);
        if (!ready) return new CurrentCraftAssessment(false, 5, strategy);

        // Project exactly the successful immediate allocation, not a second recursive guess.
        var afterInventory = inventory.ToDictionary(
            pair => pair.Key, pair => Math.Max(0, pair.Value) - checked((int)consumed!.GetValueOrDefault(pair.Key)),
            StringComparer.OrdinalIgnoreCase);
        afterInventory[unit.Id] = afterInventory.GetValueOrDefault(unit.Id) + 1;
        var after = Metrics(resolve, afterInventory);
        var lost = new StrategyMetrics(
            Stun: Loss(_current.Stun, after.Stun, strategy.StunTarget),
            Slow: Loss(_current.Slow, after.Slow, strategy.SlowTarget),
            ArmorReduction: Loss(_current.ArmorReduction, after.ArmorReduction, strategy.ArmorReductionTarget),
            MagicArmorReduction: Loss(_current.MagicArmorReduction, after.MagicArmorReduction,
                strategy.MagicArmorReductionTarget),
            BossControl: Loss(_current.BossControl, after.BossControl, Math.Max(1, strategy.BossControlTarget)),
            BerserkBossControl: Loss(_current.BerserkBossControl, after.BerserkBossControl,
                Math.Max(1, strategy.BerserkBossControlTarget)),
            SingleDamage: Loss(_current.SingleDamage, after.SingleDamage, Math.Max(1, strategy.SingleDamageTarget)),
            FinisherDamage: Loss(_current.FinisherDamage, after.FinisherDamage,
                Math.Max(1, strategy.FinisherDamageTarget)));
        var losesCombat = lost.Total > 0.0001;
        var goalBefore = calculator.Calculate([goal.Id], inventory);
        var goalAfter = calculator.Calculate([goal.Id], afterInventory);
        var goalMaterialLoss = Math.Max(0,
            goalAfter.MissingLeaves.Sum(leaf => leaf.MissingCount) -
            goalBefore.MissingLeaves.Sum(leaf => leaf.MissingCount));
        var bossGain = Gain(_current.BossControl, after.BossControl, strategy.BossControlTarget) ||
                       Gain(_current.BerserkBossControl, after.BerserkBossControl,
                           strategy.BerserkBossControlTarget);
        var storyGain = Gain(_current.SingleDamage, after.SingleDamage,
                            Math.Max(1, strategy.SingleDamageTarget)) ||
                        Gain(_current.FinisherDamage, after.FinisherDamage,
                            Math.Max(1, strategy.FinisherDamageTarget));
        var controlGain = Gain(_current.Stun, after.Stun, strategy.StunTarget) ||
                          Gain(_current.Slow, after.Slow, strategy.SlowTarget) ||
                          Gain(_current.ArmorReduction, after.ArmorReduction,
                              strategy.ArmorReductionTarget) ||
                          Gain(_current.MagicArmorReduction, after.MagicArmorReduction,
                              strategy.MagicArmorReductionTarget);
        var priority = round >= RecommendationUrgencyPolicy.SurvivalRound && bossGain ? 0
            : round >= RecommendationUrgencyPolicy.SurvivalRound &&
              completedStoryStage < RecommendationUrgencyPolicy.StoryDeadlineStage && storyGain ? 1
            : controlGain || bossGain ? 2
            : unit.Id.Equals(goal.Id, StringComparison.OrdinalIgnoreCase) ? 3
            : 4;
        return new CurrentCraftAssessment(true, losesCombat ? 6 : priority, strategy,
            LosesRequiredCombat: losesCombat,
            GoalMaterialLoss: goalMaterialLoss,
            NeedsResourceConfirmation: resources.Any(pair => !inventory.ContainsKey(pair.Key)),
            LostCoverage: lost);
    }

    internal static double RepairScore(StrategyMetrics candidate, StrategyMetrics lost) =>
        Coverage(candidate.Stun, lost.Stun) + Coverage(candidate.Slow, lost.Slow) +
        Coverage(candidate.ArmorReduction, lost.ArmorReduction) +
        Coverage(candidate.MagicArmorReduction, lost.MagicArmorReduction) +
        Coverage(candidate.BossControl, lost.BossControl) +
        Coverage(candidate.BerserkBossControl, lost.BerserkBossControl) +
        Coverage(candidate.SingleDamage, lost.SingleDamage) +
        Coverage(candidate.FinisherDamage, lost.FinisherDamage);

    private static double Coverage(double value, double missing) =>
        missing <= 0.0001 ? 0 : Math.Clamp(value / missing, 0, 1);

    private static double Loss(double before, double after, double target) =>
        Math.Max(0, Math.Min(before, target) - Math.Min(after, target));

    private static bool Gain(double before, double after, double target) =>
        Math.Min(after, target) > Math.Min(before, target) + 0.0001;

    private static bool IsResource(UnitDefinition unit) =>
        unit.Tier == "자원" ||
        unit.Rawcodes.Any(code => code is "GOLD" or "LUMBER" or "POINT" or "RANDOM");

    private static StrategyMetrics Metrics(Func<string, UnitDefinition> resolve,
        IReadOnlyDictionary<string, int> inventory)
    {
        var metrics = new StrategyMetrics();
        foreach (var (id, count) in inventory.Where(pair => pair.Value > 0))
            metrics += GoalStrategyCalculator.StrategyMetricsFor(resolve(id)) * count;
        return metrics;
    }
}
