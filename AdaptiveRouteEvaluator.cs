using System.Collections.Immutable;

namespace OrandOverlay;

public sealed class AdaptiveRouteEvaluator
{
    private const int AutomaticConfidenceThresholdBp = 8000;
    private const int ShortlistLimit = 6;

    public AdaptiveRouteEvaluationResult Evaluate(AdaptiveRouteEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var calculator = new RecipeCompletionCalculator(id => input.Units[id]);
        var preliminary = input.Candidates
            .Where(candidate => input.Units.ContainsKey(candidate.GoalUnitId))
            .Select(candidate => ScoreCore(input, candidate, calculator))
            .ToList();
        var physical = Shortlist(preliminary, DamageLane.Physical);
        var magic = Shortlist(preliminary, DamageLane.Magic);
        var shortlisted = physical.Concat(magic).ToList();
        var final = shortlisted.Select(score => ApplyPenalties(input, score,
                shortlisted, calculator))
            .ToList();
        physical = OrderFinal(final.Where(score => score.Lane == DamageLane.Physical))
            .ToImmutableArray();
        magic = OrderFinal(final.Where(score => score.Lane == DamageLane.Magic))
            .ToImmutableArray();
        var bestPhysical = physical.FirstOrDefault(score => !score.HasHardBlocker);
        var bestMagic = magic.FirstOrDefault(score => !score.HasHardBlocker);
        var selected = SelectRobust(input.IsAutomatic, bestPhysical, bestMagic);
        return new AdaptiveRouteEvaluationResult
        {
            PhysicalCandidates = physical,
            MagicCandidates = magic,
            BestPhysical = bestPhysical,
            BestMagic = bestMagic,
            Selected = selected
        };
    }

    public static DamageLane ClassifyDamage(UnitDefinition unit)
    {
        var physical = GoalStrategyCalculator.IsPhysicalDamageTier(unit.Tier) ||
                       unit.Rawcodes.Contains("DB0H", StringComparer.Ordinal) ||
                       unit.OfficialAbilities.Any(ability =>
                           ability.Name.Equals("바제스", StringComparison.Ordinal) &&
                           !ability.DisplayValue.Equals("불가", StringComparison.OrdinalIgnoreCase));
        var magic = GoalStrategyCalculator.IsMagicDamageTier(unit.Tier);
        return (physical, magic) switch
        {
            (true, false) => DamageLane.Physical,
            (false, true) => DamageLane.Magic,
            _ => DamageLane.Unknown
        };
    }

    internal static int RoundAway(long numerator, long denominator)
    {
        if (denominator <= 0) return 10000;
        return checked((int)Math.Round((decimal)numerator / denominator,
            MidpointRounding.AwayFromZero));
    }

    private static AdaptiveRouteScore ScoreCore(AdaptiveRouteEvaluationInput input,
        AdaptiveRouteCandidate candidate, RecipeCompletionCalculator calculator)
    {
        var goal = input.Units[candidate.GoalUnitId];
        var lane = ClassifyDamage(goal);
        var supports = AdaptiveRouteStrategyPlanner.Synthesize(goal,
            candidate.StrategySupportPoolIds, input.Units);
        var packageIds = candidate.ExplicitRequiredRootIds.AddRange(supports)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToImmutableArray();
        var goalAllocation = calculator.CalculateAllocation([candidate.GoalUnitId], input.Inventory);
        var afterGoal = ToInventory(goalAllocation.RemainingInventory);
        var explicitAllocation = calculator.CalculateAllocation(
            candidate.ExplicitRequiredRootIds, afterGoal);
        var supportAllocation = calculator.CalculateAllocation(supports,
            ToInventory(explicitAllocation.RemainingInventory));
        var strategyBp = AdaptiveRouteStrategyPlanner.Score(goal, packageIds, input.Units);
        var packageOwned = Add(explicitAllocation.Progress.OwnedLeafCount,
            supportAllocation.Progress.OwnedLeafCount);
        var packageRequired = Add(explicitAllocation.Progress.RequiredLeafCount,
            supportAllocation.Progress.RequiredLeafCount);
        var recipeBp = RatioBp(packageOwned, packageRequired);
        var packageBp = RoundAway((long)recipeBp * strategyBp, 10000);
        var affinity = AdaptiveRouteAffinityCalculator.Score(input, goal,
            candidate.OptionalAffinityRootIds, packageIds,
            supportAllocation.RemainingInventory, calculator);
        var goalBp = RatioBp(goalAllocation.Progress.OwnedLeafCount,
            goalAllocation.Progress.RequiredLeafCount);
        var baseBp = RoundAway(55L * goalBp + 30L * packageBp + 15L * affinity.Bp, 100);
        var consumed = Merge(goalAllocation.ConsumedByUnitId,
            Merge(explicitAllocation.ConsumedByUnitId, supportAllocation.ConsumedByUnitId));
        var hasOppositeTop = packageIds.Any(id => input.Units.TryGetValue(id, out var unit) &&
            ClassifyDamage(unit) is var packageLane && packageLane != DamageLane.Unknown &&
            packageLane != lane);
        var hardBlocked = candidate.HasHardBlocker || lane == DamageLane.Unknown || hasOppositeTop;
        var confidence = new[]
        {
            Math.Clamp(candidate.RecognitionConfidenceBp, 0, 10000),
            Math.Clamp(candidate.DamageConfidenceBp, 0, 10000),
            Math.Clamp(candidate.RecipeConfidenceBp, 0, 10000),
            affinity.EvidenceConfidenceBp,
            lane == DamageLane.Unknown ? 0 : 10000
        }.Min();
        return new AdaptiveRouteScore
        {
            Lane = lane,
            GoalUnitId = candidate.GoalUnitId,
            VariantId = candidate.VariantId,
            PackageUnitIds = packageIds,
            SynthesizedSupportUnitIds = supports,
            ConsumedInventory = consumed,
            GoalBp = goalBp,
            RecipePackageBp = recipeBp,
            StrategyPackageBp = strategyBp,
            PackageBp = packageBp,
            AffinityBp = affinity.Bp,
            BaseBp = baseBp,
            OpportunityPenaltyBp = 0,
            AbandonmentLowerBp = 0,
            AbandonmentUpperBp = 0,
            FinalLowerBp = baseBp,
            FinalUpperBp = baseBp,
            ConfidenceBp = confidence,
            EvidenceConfidenceBp = affinity.EvidenceConfidenceBp,
            SampleCount = candidate.SampleCount,
            GoalRequiredLeaves = goalAllocation.Progress.RequiredLeafCount,
            PackageRequiredLeaves = packageRequired,
            GoalMissingLeaves = Math.Max(0, goalAllocation.Progress.RequiredLeafCount -
                goalAllocation.Progress.OwnedLeafCount),
            PackageMissingLeaves = Math.Max(0, packageRequired - packageOwned),
            HasHardBlocker = hardBlocked
        };
    }

    private static ImmutableArray<AdaptiveRouteScore> Shortlist(
        IEnumerable<AdaptiveRouteScore> scores, DamageLane lane) => scores
        .Where(score => score.Lane == lane)
        .OrderBy(score => score.HasHardBlocker)
        .ThenByDescending(score => score.GoalBp)
        .ThenBy(score => score.GoalRequiredLeaves)
        .ThenBy(score => score.PackageRequiredLeaves)
        .ThenByDescending(score => score.SampleCount)
        .ThenBy(score => score.GoalUnitId, StringComparer.Ordinal)
        .Take(ShortlistLimit)
        .ToImmutableArray();

    private static AdaptiveRouteScore ApplyPenalties(AdaptiveRouteEvaluationInput input,
        AdaptiveRouteScore score, IReadOnlyList<AdaptiveRouteScore> shortlist,
        RecipeCompletionCalculator calculator)
    {
        var alternatives = shortlist.Where(other =>
                !other.GoalUnitId.Equals(score.GoalUnitId, StringComparison.OrdinalIgnoreCase) ||
                !other.VariantId.Equals(score.VariantId, StringComparison.Ordinal))
            .ToList();
        var altBefore = alternatives.Count == 0 ? 0 : alternatives.Max(other => other.BaseBp);
        var reducedInventory = Subtract(input.Inventory, score.ConsumedInventory);
        var altAfter = alternatives.Count == 0 ? 0 : alternatives.Max(other =>
        {
            var candidate = input.Candidates.First(value =>
                value.GoalUnitId.Equals(other.GoalUnitId, StringComparison.OrdinalIgnoreCase) &&
                value.VariantId.Equals(other.VariantId, StringComparison.Ordinal));
            return ScoreCore(input with { Inventory = reducedInventory }, candidate, calculator).BaseBp;
        });
        var opportunity = RoundAway(Math.Max(0, altBefore - altAfter), 5);
        var abandonment = AbandonmentInterval(input, score, calculator);
        return score with
        {
            OpportunityPenaltyBp = opportunity,
            AbandonmentLowerBp = abandonment.Lower,
            AbandonmentUpperBp = abandonment.Upper,
            FinalLowerBp = Math.Clamp(score.BaseBp - opportunity - abandonment.Upper, 0, 10000),
            FinalUpperBp = Math.Clamp(score.BaseBp - opportunity - abandonment.Lower, 0, 10000)
        };
    }

    private static (int Lower, int Upper) AbandonmentInterval(
        AdaptiveRouteEvaluationInput input, AdaptiveRouteScore score,
        RecipeCompletionCalculator calculator)
    {
        if (input.FirstObservedLegendIds.IsDefaultOrEmpty) return (0, 0);
        var attached = score.PackageUnitIds.Add(score.GoalUnitId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        attached.UnionWith(score.ConsumedInventory.Keys);
        var values = input.FirstObservedLegendIds
            .Where(input.Units.ContainsKey)
            .Select(id => attached.Contains(id) ? 0 : RoundAway(2500L * Math.Max(1,
                calculator.CalculateAllocation([id], EmptyInventory).Progress.RequiredLeafCount),
                Math.Max(1, calculator.CalculateAllocation([id], EmptyInventory)
                    .Progress.RequiredLeafCount)))
            .ToList();
        return values.Count == 0 ? (0, 0) : (values.Min(), Math.Min(2500, values.Max()));
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyInventory =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static AdaptiveRouteScore? SelectRobust(bool automatic,
        AdaptiveRouteScore? physical, AdaptiveRouteScore? magic)
    {
        var eligible = new[] { physical, magic }
            .Where(score => score is not null && !score.HasHardBlocker &&
                (!automatic || score.ConfidenceBp >= AutomaticConfidenceThresholdBp))
            .Cast<AdaptiveRouteScore>()
            .ToList();
        if (eligible.Count == 0) return null;
        var ordered = OrderFinal(eligible).ToList();
        if (ordered.Count > 1 && CompareWorstToBest(ordered[0], ordered[1]) <= 0) return null;
        return ordered[0];
    }

    private static int CompareWorstToBest(AdaptiveRouteScore left, AdaptiveRouteScore right)
    {
        var value = left.FinalLowerBp.CompareTo(right.FinalUpperBp);
        if (value != 0) return value;
        value = left.ConfidenceBp.CompareTo(right.ConfidenceBp);
        if (value != 0) return value;
        return -StringComparer.Ordinal.Compare(left.GoalUnitId, right.GoalUnitId);
    }

    private static IOrderedEnumerable<AdaptiveRouteScore> OrderFinal(
        IEnumerable<AdaptiveRouteScore> scores) => scores
        .OrderBy(score => score.HasHardBlocker)
        .ThenByDescending(score => score.FinalLowerBp)
        .ThenByDescending(score => score.ConfidenceBp)
        .ThenByDescending(score => score.GoalBp)
        .ThenByDescending(score => score.PackageBp)
        .ThenByDescending(score => score.SampleCount)
        .ThenBy(score => score.GoalMissingLeaves)
        .ThenBy(score => score.PackageMissingLeaves)
        .ThenBy(score => score.OpportunityPenaltyBp)
        .ThenBy(score => score.AbandonmentUpperBp)
        .ThenBy(score => score.GoalUnitId, StringComparer.Ordinal)
        .ThenBy(score => score.VariantId, StringComparer.Ordinal);

    private static int RatioBp(long owned, long required) => required <= 0
        ? 10000
        : Math.Clamp(RoundAway(checked(10000L * owned), required), 0, 10000);

    internal static IReadOnlyDictionary<string, int> ToInventory(
        IReadOnlyDictionary<string, long> inventory) => inventory.ToDictionary(
        pair => pair.Key, pair => checked((int)Math.Min(int.MaxValue, pair.Value)),
        StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, int> Subtract(
        IReadOnlyDictionary<string, int> inventory, IReadOnlyDictionary<string, long> consumed) =>
        inventory.ToDictionary(pair => pair.Key,
            pair => Math.Max(0, pair.Value - checked((int)Math.Min(int.MaxValue,
                consumed.GetValueOrDefault(pair.Key)))), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, long> Merge(
        IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right)
    {
        var result = new Dictionary<string, long>(left, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in right)
            result[pair.Key] = Add(result.GetValueOrDefault(pair.Key), pair.Value);
        return result;
    }

    private static long Add(long left, long right) =>
        left >= long.MaxValue - right ? long.MaxValue : left + right;

}
