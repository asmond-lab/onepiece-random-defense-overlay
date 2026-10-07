using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace OrandOverlay;

public enum RandomFlavor
{
    Blue,
    Green,
    Yellow
}

public enum RandomFlavorBonusKind
{
    RandomWisp,
    Lumber,
    Absalom
}

public enum RandomFlavorChildOption
{
    DoubleBenefit,
    EmergencyCall,
    TraitEngineering,
    MartialLaw,
    BountyHunter,
    RoyalLoader,
    Casino,
    RiskHedge,
    ContinuousBetting,
    MaximumOutput,
    Alchemy,
    ReverseThinking
}

public enum RandomFlavorEligibility
{
    Eligible,
    HardIncompatibleWithSelectedTop
}

public sealed record RandomFlavorChildEffect
{
    public string Id { get; }
    public int RewardUnits { get; }

    public RandomFlavorChildEffect(string id, int rewardUnits)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An effect id is required.", nameof(id));
        if (rewardUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(rewardUnits));
        Id = id;
        RewardUnits = rewardUnits;
    }
}

public sealed record RandomFlavorChildSimulation(
    RandomFlavorChildOption Option,
    RandomFlavorChildEffect Effect);

public readonly record struct RandomFlavorBonus(
    RandomFlavorBonusKind Kind,
    int Count);

public sealed record RandomFlavorRecommendationInput
{
    public int ActualTopCount { get; }
    public bool HasSelectedTopPlan { get; }
    public ImmutableArray<RandomFlavorChildSimulation> Children { get; }

    public RandomFlavorRecommendationInput(
        int actualTopCount,
        bool hasSelectedTopPlan,
        ImmutableArray<RandomFlavorChildSimulation> children)
    {
        if (actualTopCount < 0)
            throw new ArgumentOutOfRangeException(nameof(actualTopCount));
        if (children.IsDefault)
            throw new ArgumentException("Child simulations are required.", nameof(children));

        var expected = Enum.GetValues<RandomFlavorChildOption>();
        if (children.Length != expected.Length ||
            children.Any(child => child is null || child.Effect is null) ||
            !children.Select(child => child.Option)
                .OrderBy(option => option)
                .SequenceEqual(expected))
        {
            throw new ArgumentException(
                "Supply each non-Random child option exactly once.", nameof(children));
        }

        ActualTopCount = actualTopCount;
        HasSelectedTopPlan = hasSelectedTopPlan;
        Children = children;
    }

    public RandomFlavorChildSimulation Child(RandomFlavorChildOption option) =>
        Children.Single(child => child.Option == option);
}

public sealed record RandomFlavorBranchOutcome(
    RandomFlavorChildOption Option,
    Rational Probability,
    bool DispatchSucceeded,
    bool VyPending,
    bool LyLocked,
    bool RerollAttempted,
    RandomFlavorChildEffect? AppliedChildEffect,
    RandomFlavorBonus? Bonus)
{
    public int RewardUnits => AppliedChildEffect?.RewardUnits ?? 0;
}

public sealed record RandomFlavorSimulationResult(
    RandomFlavorEligibility Eligibility,
    ImmutableArray<RandomFlavorBranchOutcome> Outcomes)
{
    public Rational ProbabilityMass => Outcomes.Aggregate(
        new Rational(0, 1),
        (mass, outcome) => mass + outcome.Probability);
}

public static class RandomFlavorNavigationSimulator
{
    private static readonly Rational BranchProbability = new(1, 12);

    public static RandomFlavorSimulationResult Simulate(
        RandomFlavor flavor,
        RandomFlavorRecommendationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(flavor))
            throw new ArgumentOutOfRangeException(nameof(flavor));

        if (input.ActualTopCount == 0 && input.HasSelectedTopPlan)
        {
            return new RandomFlavorSimulationResult(
                RandomFlavorEligibility.HardIncompatibleWithSelectedTop, []);
        }

        var bonus = new RandomFlavorBonus(BonusFor(flavor), 1);
        var outcomes = Enum.GetValues<RandomFlavorChildOption>()
            .Select(option => CreateOutcome(input, option, bonus))
            .ToImmutableArray();
        return new RandomFlavorSimulationResult(
            RandomFlavorEligibility.Eligible, outcomes);
    }

    private static RandomFlavorBranchOutcome CreateOutcome(
        RandomFlavorRecommendationInput input,
        RandomFlavorChildOption option,
        RandomFlavorBonus bonus)
    {
        var succeeded = PathSelectionSucceeds(option, input.ActualTopCount);
        return new RandomFlavorBranchOutcome(
            option,
            BranchProbability,
            succeeded,
            VyPending: !succeeded,
            LyLocked: true,
            RerollAttempted: false,
            AppliedChildEffect: succeeded ? input.Child(option).Effect : null,
            Bonus: succeeded ? bonus : null);
    }

    private static bool PathSelectionSucceeds(
        RandomFlavorChildOption option,
        int actualTopCount) => option switch
        {
            RandomFlavorChildOption.MartialLaw => actualTopCount == 0,
            RandomFlavorChildOption.BountyHunter or
                RandomFlavorChildOption.RoyalLoader => actualTopCount <= 1,
            _ => true
        };

    private static RandomFlavorBonusKind BonusFor(RandomFlavor flavor) => flavor switch
    {
        RandomFlavor.Blue => RandomFlavorBonusKind.RandomWisp,
        RandomFlavor.Green => RandomFlavorBonusKind.Lumber,
        RandomFlavor.Yellow => RandomFlavorBonusKind.Absalom,
        _ => throw new ArgumentOutOfRangeException(nameof(flavor))
    };
}
