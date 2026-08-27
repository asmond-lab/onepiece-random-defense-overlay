using System.Numerics;
using System.Collections.Immutable;

namespace OrandOverlay;

internal sealed class SimulationContext
{
    private readonly NavigationLimits _limits;
    public NavigationLimitKind? ExceededLimit { get; private set; }
    public int Requested { get; private set; }
    public int Limit { get; private set; }
    public int MaxBits { get; private set; }

    public SimulationContext(NavigationLimits limits) =>
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));

    public bool Check(NavigationLimitKind kind, int requested)
    {
        var check = kind switch
        {
            NavigationLimitKind.GambleActions => _limits.CheckActions(requested),
            NavigationLimitKind.AggregatedStates => _limits.CheckStates(requested),
            NavigationLimitKind.RationalBits => _limits.CheckRationalBits(requested),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (check.ArithmeticDisposition == ArithmeticDisposition.Allowed) return true;
        ExceededLimit = kind; Requested = requested; Limit = check.Limit;
        return false;
    }

    public bool Observe(Rational value)
    {
        var bits = Math.Max(BitLength(value.Numerator), BitLength(value.Denominator));
        MaxBits = Math.Max(MaxBits, bits);
        return Check(NavigationLimitKind.RationalBits, bits);
    }

    public bool TryAdd(Rational left, Rational right, out Rational result)
    {
        result = left + right;
        return Observe(left) && Observe(right) && Observe(result);
    }

    public bool TryMultiply(Rational left, Rational right, out Rational result)
    {
        result = left * right;
        return Observe(left) && Observe(right) && Observe(result);
    }

    public bool TrySum(IEnumerable<Rational> values, out Rational result)
    {
        result = new Rational(0, 1);
        foreach (var value in values)
            if (!TryAdd(result, value, out result)) return false;
        return true;
    }

    public GamblerSimulationResult Failure(NavigationMechanicsOption option) =>
        new(ArithmeticDisposition.ArithmeticLimitExceeded,
            WaveDisposition.NoSafeRecommendation, ExceededLimit,
            Requested, Limit, 0, MaxBits,
            option.DisabledActions.Select(value => value switch
            {
                "low_gamble" => GamblerActionTier.Low,
                "middle_gamble" => GamblerActionTier.Middle,
                "high_gamble" => GamblerActionTier.High,
                "rare_reroll" => GamblerActionTier.RareReroll,
                _ => throw new InvalidDataException("Unknown disabled Gambler action.")
            }).ToImmutableArray(), [], new Rational(0, 1),
            [ReasonCode.ArithmeticLimitExceeded, ReasonCode.NoSafeRecommendation]);

    private static int BitLength(BigInteger value)
    {
        var bits = BigInteger.Abs(value).GetBitLength();
        return bits > int.MaxValue ? int.MaxValue : (int)bits;
    }
}
