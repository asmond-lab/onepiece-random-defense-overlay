using System.Collections.Immutable;

namespace OrandOverlay;

public static class NavigationIntervalSimulationAdapter
{
    public static ImmutableArray<NavigationIntervalOptionInput> Adapt(
        NavigationIntervalSimulationBatch batch,
        INavigationIntervalOutcomeProjector projector)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(projector);
        var byId = batch.Allied.Select(result => Allied(result, projector))
            .Concat(batch.Path.Select(result => Path(result, projector)))
            .Concat(batch.Gambler.Select(result => Gambler(result, projector)))
            .Concat([
                Maximum(batch.MaximumOutput, projector),
                Alchemy(batch.Alchemy, projector),
                Reverse(batch.ReverseThinking, projector)])
            .Concat(batch.Random.Select(result => Random(result, projector)))
            .ToDictionary(input => input.OptionId, StringComparer.Ordinal);
        if (byId.Count != NavigationIntervalScorer.RequiredOptionIds.Length ||
            !NavigationIntervalScorer.RequiredOptionIds.All(byId.ContainsKey) ||
            batch.Evidence.Count != NavigationIntervalScorer.RequiredOptionIds.Length)
            throw new ArgumentException("The batch must contain all 15 navigation outputs.");
        return NavigationIntervalScorer.RequiredOptionIds.Select((id, ordinal) =>
        {
            if (!batch.Evidence.TryGetValue(id, out var evidence))
                throw new ArgumentException($"Missing adapter evidence for {id}.");
            NavigationIntervalScorerMath.ValidateBasisPoints(evidence.ConfidenceBp);
            var input = byId[id];
            return input with
            {
                OrdinalId = ordinal,
                ConfidenceFactorsBp = input.ConfidenceFactorsBp.Add(evidence.ConfidenceBp),
                MissingSignalIds = Merge(input.MissingSignalIds, evidence.MissingSignalIds)
            };
        }).ToImmutableArray();
    }

    private static NavigationIntervalOptionInput Allied(
        AlliedForcesSimulationResult result, INavigationIntervalOutcomeProjector projector)
    {
        var id = $"AlliedForces.{result.Option}";
        var eligible = result.Disposition == AlliedForcesSimulationDisposition.Eligible;
        var uncertain = result.HasUnknownResources;
        return Input(id, eligible, uncertain ? 8_000 : 10_000,
            result.Taxonomy switch
            {
                AlliedForcesUserTaxonomy.HighCeiling => NavigationRiskPosture.HighCeiling,
                AlliedForcesUserTaxonomy.FloorDefense => NavigationRiskPosture.FloorDefense,
                _ => NavigationRiskPosture.Balanced
            }, eligible ? Bounds(id, bound => projector.Project(result, bound)) : [],
            [$"allied:{result.Disposition}"]);
    }

    private static NavigationIntervalOptionInput Gambler(
        NavigationGamblerAdapterResult result, INavigationIntervalOutcomeProjector projector)
    {
        var value = result.Result;
        var eligible = value.ArithmeticDisposition == ArithmeticDisposition.Allowed &&
            value.WaveDisposition == WaveDisposition.SafeRecommendation &&
            !value.Outcomes.IsDefaultOrEmpty;
        var outcomes = eligible ? value.Outcomes.Select(outcome => Outcome(
            outcome.Mass, projector.Project(result, outcome.State))).ToImmutableArray() : [];
        var reasons = value.Reasons.Select(reason => $"gambler:{reason}").Append(
            $"gambler:{value.ArithmeticDisposition}:{value.WaveDisposition}").ToImmutableArray();
        return Input(result.OptionId, eligible, eligible ? 10_000 : 0,
            NavigationRiskPosture.HighCeiling,
            eligible ? [new NavigationCoupledScenario(result.OptionId, outcomes)] : [], reasons);
    }

    private static NavigationIntervalOptionInput Path(
        PathOfKingsSimulationResult result, INavigationIntervalOutcomeProjector projector)
    {
        var id = $"PathOfKings.{result.Option}";
        var eligible = result.Option != PathOfKingsOption.MartialLaw &&
            result.Disposition == PathOfKingsDisposition.Eligible;
        ImmutableArray<NavigationCoupledScenario> scenarios = [];
        if (eligible && result.Option == PathOfKingsOption.BountyHunter && result.Bounty is { } bounty)
            scenarios = [new(id, bounty.TerminalStates.Select(state => Outcome(
                state.Probability, projector.Project(result, state))).ToImmutableArray())];
        else if (eligible && result.Option == PathOfKingsOption.RoyalLoader)
            scenarios = result.RoyalScenarios.Select((scenario, index) => new NavigationCoupledScenario(
                $"{id}:{index}", [Outcome(new Rational(1, 1),
                    projector.Project(scenario.FinalScoreInput))])).ToImmutableArray();
        var confidence = result.Disposition == PathOfKingsDisposition.UnknownInput ? 8_000 :
            eligible ? 10_000 : 0;
        return Input(id, eligible && !scenarios.IsDefaultOrEmpty, confidence,
            NavigationRiskPosture.FloorDefense, scenarios,
            [$"path:{result.Disposition}"]);
    }

    private static NavigationIntervalOptionInput Maximum(
        BestHelpMaximumResult result, INavigationIntervalOutcomeProjector projector) =>
        BestHelp("BestHelp.MaximumOutput", result.Disposition,
            result.UnknownSpellIds, bound => projector.Project(result, bound));

    private static NavigationIntervalOptionInput Alchemy(
        BestHelpAlchemyResult result, INavigationIntervalOutcomeProjector projector)
    {
        var eligible = result.Disposition != BestHelpSimulationDisposition.InvalidInput;
        var scenarios = eligible ? [Deterministic("BestHelp.Alchemy", projector.Project(result))] :
            ImmutableArray<NavigationCoupledScenario>.Empty;
        return Input("BestHelp.Alchemy", eligible, Confidence(result.Disposition),
            NavigationRiskPosture.Balanced, scenarios, [$"best-help:{result.Disposition}"]);
    }

    private static NavigationIntervalOptionInput Reverse(
        BestHelpReverseResult result, INavigationIntervalOutcomeProjector projector) =>
        BestHelp("BestHelp.ReverseThinking", result.Disposition,
            result.UnknownSpellIds, bound => projector.Project(result, bound));

    private static NavigationIntervalOptionInput BestHelp(string id,
        BestHelpSimulationDisposition disposition, ImmutableArray<string> unknown,
        Func<NavigationIntervalBound, NavigationIntervalProjectedState> project)
    {
        var eligible = disposition != BestHelpSimulationDisposition.InvalidInput;
        return Input(id, eligible, Confidence(disposition), NavigationRiskPosture.Balanced,
            eligible ? Bounds(id, project) : [],
            unknown.Select(value => $"best-help:unknown:{value}")
                .Prepend($"best-help:{disposition}").ToImmutableArray());
    }

    private static NavigationIntervalOptionInput Random(
        NavigationRandomAdapterResult result, INavigationIntervalOutcomeProjector projector)
    {
        var id = $"Random.{result.Flavor}Flavor";
        var eligible = result.Result.Eligibility == RandomFlavorEligibility.Eligible &&
            !result.Result.Outcomes.IsDefaultOrEmpty;
        var outcomes = eligible ? result.Result.Outcomes.Select(outcome => Outcome(
            outcome.Probability, projector.Project(result, outcome))).ToImmutableArray() : [];
        return Input(id, eligible, eligible ? 10_000 : 0, NavigationRiskPosture.HighCeiling,
            eligible ? [new NavigationCoupledScenario(id, outcomes)] : [],
            [$"random:{result.Result.Eligibility}"]);
    }

    private static int Confidence(BestHelpSimulationDisposition disposition) => disposition switch
    {
        BestHelpSimulationDisposition.Confirmed => 10_000,
        BestHelpSimulationDisposition.ScenarioGated => 8_000,
        _ => 0
    };

    private static NavigationIntervalOptionInput Input(string id, bool eligible, int confidence,
        NavigationRiskPosture posture, ImmutableArray<NavigationCoupledScenario> scenarios,
        ImmutableArray<string> reasons) => new(id, -1, eligible, [confidence], [], posture,
            scenarios, reasons);

    private static ImmutableArray<NavigationCoupledScenario> Bounds(string id,
        Func<NavigationIntervalBound, NavigationIntervalProjectedState> project) =>
        [Deterministic($"{id}:lower", project(NavigationIntervalBound.Lower)),
            Deterministic($"{id}:upper", project(NavigationIntervalBound.Upper))];

    private static NavigationCoupledScenario Deterministic(string id,
        NavigationIntervalProjectedState state) =>
        new(id, [Outcome(new Rational(1, 1), state)]);

    private static NavigationScoringOutcome Outcome(Rational probability,
        NavigationIntervalProjectedState state) =>
        new(probability, state.BuildBp, state.CoreBp, state.CombatBp);

    private static ImmutableArray<string> Merge(ImmutableArray<string> left,
        ImmutableArray<string> right) => (left.IsDefault ? [] : left)
        .Concat(right.IsDefault ? [] : right).Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal).ToImmutableArray();
}
