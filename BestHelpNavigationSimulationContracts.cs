using System.Collections.Immutable;

namespace OrandOverlay;

public enum BestHelpSimulationDisposition
{
    Confirmed,
    ScenarioGated,
    InvalidInput
}

public sealed record BestHelpSpellScenario
{
    public required string SpellId { get; init; }
    public int TargetCount { get; init; }
    public long? Level1DamagePerTarget { get; init; }
    public long? Level2DamagePerTarget { get; init; }
    public int Level1ControlTargetSeconds { get; init; }
    public int Level2ControlTargetSeconds { get; init; }
    public int CoreDeficitsClosedLevel1 { get; init; }
    public int CoreDeficitsClosedLevel2 { get; init; }
}

public sealed record BestHelpMaximumInput
{
    public int CurrentMana { get; init; }
    public int HorizonSeconds { get; init; }
    public long RemainingEnemyEffectiveHp { get; init; }
    public ImmutableArray<long> RemainingControlDeficits { get; init; } = [];
    public IReadOnlyDictionary<string, int> CooldownRemainingSeconds { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public ImmutableArray<BestHelpSpellScenario> SpellScenarios { get; init; } = [];
}

public sealed record BestHelpCast(string SpellId, int TimeSeconds, int ManaCost,
    long Damage, long ControlTargetSeconds, int CoreDeficitsClosed);

public sealed record BestHelpCastSchedule
{
    public required int AbilityLevel { get; init; }
    public required int AddedPotionMana { get; init; }
    public required ImmutableArray<BestHelpCast> Casts { get; init; }
    public required long Damage { get; init; }
    public required long ControlTargetSeconds { get; init; }
    public required int CoreDeficitsClosed { get; init; }
}

public sealed record BestHelpMaximumResult
{
    public required BestHelpSimulationDisposition Disposition { get; init; }
    public required ImmutableArray<string> UnknownSpellIds { get; init; }
    public BestHelpCastSchedule? Baseline { get; init; }
    public BestHelpCastSchedule? Outcome { get; init; }
    public long IncrementalDamage { get; init; }
    public long IncrementalControlTargetSeconds { get; init; }
    public int DamageGainBp { get; init; }
    public int ControlGainBp { get; init; }
    public int CombatBp { get; init; }
}

public sealed record BestHelpRecipeOverrides
{
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>
        DirectUnitChildren { get; init; }

    public static BestHelpRecipeOverrides Parse(
        IEnumerable<string> lines,
        IReadOnlySet<string> authoritativeUnitRawcodes)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(authoritativeUnitRawcodes);
        var recipes = new Dictionary<string, IReadOnlyDictionary<string, int>>(
            StringComparer.Ordinal);
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var halves = line.Split('=', 2);
            if (halves.Length != 2 || !authoritativeUnitRawcodes.Contains(halves[0]) ||
                recipes.ContainsKey(halves[0]))
                throw new InvalidDataException("Invalid or duplicate map recipe override.");
            var children = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var token in halves[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = token.Split(':', 2);
                if (pair.Length != 2 || !int.TryParse(pair[1], out var count) || count <= 0)
                    throw new InvalidDataException("Invalid map recipe child.");
                if (authoritativeUnitRawcodes.Contains(pair[0]))
                    children[pair[0]] = checked(children.GetValueOrDefault(pair[0]) + count);
            }
            recipes.Add(halves[0], children);
        }
        return new() { DirectUnitChildren = recipes };
    }
}

public sealed record BestHelpAlchemyInput
{
    public int CurrentMana { get; init; }
    public int HorizonSeconds { get; init; }
    public IReadOnlyDictionary<string, int> Inventory { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, UnitDefinition> UnitsByRawcode { get; init; } =
        new Dictionary<string, UnitDefinition>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> MissingRouteLeaves { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlySet<string> ReservedRouteRoots { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> ReservedGrowthUnits { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> ReservedUniqueUnits { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
}

public sealed record BestHelpDismantle(string UnitRawcode,
    IReadOnlyDictionary<string, int> ReturnedUnitChildren);

public sealed record BestHelpAlchemyResult
{
    public required BestHelpSimulationDisposition Disposition { get; init; }
    public required ImmutableArray<BestHelpDismantle> Dismantles { get; init; }
    public required IReadOnlyDictionary<string, int> RouteLeavesGained { get; init; }
    public int ManaSpent { get; init; }
}

public sealed record BestHelpReverseInput
{
    public required string RayleighUnitId { get; init; }
    public int RayleighRouteGainBp { get; init; }
    public int ExcavationStackGainBp { get; init; }
    public IReadOnlyDictionary<string, int> HelperActionValuesBp { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlySet<string> RemovedHelperActionIds { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
}

public sealed record BestHelpReverseResult
{
    public required BestHelpSimulationDisposition Disposition { get; init; }
    public required string RayleighUnitId { get; init; }
    public int RayleighCount { get; init; }
    public int ExcavationStacks { get; init; }
    public required ImmutableArray<string> LostHelperActionIds { get; init; }
    public int HelperLossBp { get; init; }
    public int NetRouteGainBp { get; init; }
    public required ImmutableArray<string> UnknownSpellIds { get; init; }
}
