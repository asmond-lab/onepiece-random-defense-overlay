using System.Collections.Immutable;

namespace OrandOverlay;

internal static class NavigationIntervalScorerComparator
{
    public static NavigationIntervalOptionScore? FindRobustWinner(
        ImmutableArray<NavigationIntervalOptionScore> options,
        NavigationScoringRegime regime,
        string? currentOptionId) =>
        options.SingleOrDefault(candidate => options
            .Where(other => other.OptionId != candidate.OptionId)
            .All(other => Compare(
                Worst(candidate, regime, currentOptionId), candidate,
                Best(other, regime, currentOptionId), other,
                regime, currentOptionId) > 0));

    private static NavigationScenarioScore Worst(NavigationIntervalOptionScore option,
        NavigationScoringRegime regime, string? current) => option.Scenarios
        .MinBy(score => new ComparatorKey(score, option, regime, current))!;

    private static NavigationScenarioScore Best(NavigationIntervalOptionScore option,
        NavigationScoringRegime regime, string? current) => option.Scenarios
        .MaxBy(score => new ComparatorKey(score, option, regime, current))!;

    private static int Compare(NavigationScenarioScore left,
        NavigationIntervalOptionScore leftOption, NavigationScenarioScore right,
        NavigationIntervalOptionScore rightOption, NavigationScoringRegime regime,
        string? current) => new ComparatorKey(left, leftOption, regime, current)
        .CompareTo(new ComparatorKey(right, rightOption, regime, current));

    private readonly record struct ComparatorKey : IComparable<ComparatorKey>
    {
        private readonly ImmutableArray<NavigationSignedRational> _values;
        private readonly int _uncertainty;
        private readonly bool _current;
        private readonly int _ordinal;

        public ComparatorKey(NavigationScenarioScore score,
            NavigationIntervalOptionScore option, NavigationScoringRegime regime,
            string? current)
        {
            _values = Values(score, regime);
            _uncertainty = option.UncertaintyBp;
            _current = option.OptionId.Equals(current, StringComparison.Ordinal);
            _ordinal = option.OrdinalId;
        }

        public int CompareTo(ComparatorKey other)
        {
            for (var index = 0; index < _values.Length; index++)
            {
                var comparison = NavigationIntervalScorerMath.Compare(
                    _values[index], other._values[index]);
                if (comparison != 0) return comparison;
            }
            var uncertainty = other._uncertainty.CompareTo(_uncertainty);
            if (uncertainty != 0) return uncertainty;
            var current = _current.CompareTo(other._current);
            return current != 0 ? current : other._ordinal.CompareTo(_ordinal);
        }

        private static ImmutableArray<NavigationSignedRational> Values(
            NavigationScenarioScore score, NavigationScoringRegime regime) => regime switch
            {
                NavigationScoringRegime.SecureCore =>
                    Integers(score.NavigationScoreBp, score.FloorBp, score.UpperBp,
                        score.ValueBp, score.CaptureConflictBp),
                NavigationScoringRegime.GuaranteedRecovery =>
                    Integers(score.FloorBp, score.NavigationScoreBp, score.ValueBp,
                        score.UpperBp, score.CaptureConflictBp),
                NavigationScoringRegime.DesperationRecovery =>
                    [NavigationIntervalScorerMath.FromRational(score.RecoveryProbability),
                        .. Integers(score.Quantile95, score.NavigationScoreBp,
                            score.ValueBp, score.FloorBp, score.CaptureConflictBp)],
                _ => throw new ArgumentOutOfRangeException(nameof(regime))
            };

        private static ImmutableArray<NavigationSignedRational> Integers(
            params int[] values) => values.Select(
            NavigationIntervalScorerMath.FromInteger).ToImmutableArray();
    }
}
