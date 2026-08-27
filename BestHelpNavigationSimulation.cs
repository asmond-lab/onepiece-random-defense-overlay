using System.Collections.Immutable;
using System.Globalization;

namespace OrandOverlay;

public sealed partial class BestHelpNavigationSimulation
{
    private const int ManaScale = 10;
    private readonly NavigationMechanicsProfile _profile;

    public BestHelpNavigationSimulation(NavigationMechanicsProfile profile) =>
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public BestHelpMaximumResult SimulateMaximum(BestHelpMaximumInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CurrentMana < 0 || input.HorizonSeconds < 0 ||
            input.RemainingEnemyEffectiveHp <= 0 ||
            input.RemainingControlDeficits.Any(value => value < 0))
            return InvalidMaximum();

        var scenarios = input.SpellScenarios.ToDictionary(value => value.SpellId,
            StringComparer.Ordinal);
        var spells = new List<Spell>();
        var unknown = ImmutableArray.CreateBuilder<string>();
        foreach (var source in _profile.HelperSpells.Where(spell => spell.Id != "A055"))
        {
            scenarios.TryGetValue(source.Id, out var scenario);
            if (!TrySpell(source, scenario, out var spell))
                unknown.Add(source.Id);
            else
                spells.Add(spell);
        }
        if (unknown.Count != 0)
            return new()
            {
                Disposition = BestHelpSimulationDisposition.ScenarioGated,
                UnknownSpellIds = unknown.ToImmutable()
            };

        var baseline = Optimize(spells, input, level: 1, potionMana: 0,
            regenTenthsPerSecond: BestHelpRegen());
        var outcome = Optimize(spells, input, level: 2,
            potionMana: MaximumPotionMana(), regenTenthsPerSecond: BestHelpRegen());
        var damage = Math.Max(0, outcome.Damage - baseline.Damage);
        var control = Math.Max(0,
            outcome.ControlTargetSeconds - baseline.ControlTargetSeconds);
        var damageBp = RoundRatioBp(damage, input.RemainingEnemyEffectiveHp);
        var controlBp = input.RemainingControlDeficits.Length == 0
            ? 0
            : RoundAway(input.RemainingControlDeficits.Sum(deficit =>
                Math.Min(10_000L, RoundRatioBp(control, deficit))) /
                (decimal)input.RemainingControlDeficits.Length);
        return new()
        {
            Disposition = BestHelpSimulationDisposition.Confirmed,
            UnknownSpellIds = [],
            Baseline = baseline,
            Outcome = outcome,
            IncrementalDamage = damage,
            IncrementalControlTargetSeconds = control,
            DamageGainBp = damageBp,
            ControlGainBp = controlBp,
            CombatBp = RoundAway((70m * damageBp + 30m * controlBp) / 100m)
        };
    }

    private BestHelpCastSchedule Optimize(IReadOnlyList<Spell> spells,
        BestHelpMaximumInput input, int level, int potionMana, int regenTenthsPerSecond)
    {
        var activeSpells = spells.Where(spell => spell.Core(level) > 0 ||
            spell.Damage(level) > 0 || spell.Control(level) > 0).ToArray();
        var initialCooldowns = activeSpells.Select(spell =>
            input.CooldownRemainingSeconds.GetValueOrDefault(spell.Id)).ToArray();
        if (initialCooldowns.Any(value => value < 0))
            throw new ArgumentOutOfRangeException(nameof(input));
        var states = new Dictionary<StateKey, Plan>
        {
            [new StateKey(checked((input.CurrentMana + potionMana) * ManaScale),
                string.Join(',', initialCooldowns))] = Plan.Empty
        };
        for (var time = 0; time <= input.HorizonSeconds; time++)
        {
            var next = new Dictionary<StateKey, Plan>();
            foreach (var entry in states)
            {
                var cooldowns = ParseCooldowns(entry.Key.Cooldowns);
                var ready = Enumerable.Range(0, activeSpells.Length)
                    .Where(index => cooldowns[index] <= 0).ToArray();
                var subsetCount = 1 << ready.Length;
                for (var mask = 0; mask < subsetCount; mask++)
                {
                    var mana = entry.Key.ManaTenths;
                    var after = (int[])cooldowns.Clone();
                    var plan = entry.Value;
                    var legal = true;
                    for (var bit = 0; bit < ready.Length; bit++)
                    {
                        if ((mask & (1 << bit)) == 0) continue;
                        var index = ready[bit];
                        var spell = activeSpells[index];
                        var cost = spell.Cost(level) * ManaScale;
                        if (cost > mana) { legal = false; break; }
                        mana -= cost;
                        after[index] = spell.Cooldown(level);
                        plan = plan.Cast(spell, level, time);
                    }
                    if (!legal) continue;
                    for (var index = 0; index < after.Length; index++)
                        after[index]--;
                    if (time < input.HorizonSeconds)
                        mana = checked(mana + regenTenthsPerSecond);
                    KeepBest(next, new StateKey(mana, string.Join(',', after)), plan);
                }
            }
            states = next;
        }
        var best = states.Values.Aggregate(Plan.Empty, Better);
        return new()
        {
            AbilityLevel = level,
            AddedPotionMana = potionMana,
            Casts = best.Casts,
            Damage = best.Damage,
            ControlTargetSeconds = best.Control,
            CoreDeficitsClosed = best.Core
        };
    }

    private int MaximumPotionMana()
    {
        var transition = _profile.Transitions.Single(value =>
            value.Id == "maximum-output-level-change");
        return checked(Parameter(transition, "potionUses") *
            Parameter(transition, "manaPerPotion"));
    }

    private int BestHelpRegen()
    {
        var effect = _profile.Categories.Single(category => category.Id == "BestHelp")
            .Effects.Single(value => value.StartsWith("helper_mana_regen:",
                StringComparison.Ordinal));
        var fraction = effect.Split(':', 2)[1].Split('_', 2)[0].Split('/', 2);
        var numerator = int.Parse(fraction[0], CultureInfo.InvariantCulture);
        var denominator = int.Parse(fraction[1], CultureInfo.InvariantCulture);
        if (denominator != ManaScale)
            throw new InvalidDataException("Best Help mana regeneration is not tenths-exact.");
        return numerator;
    }

    private static bool TrySpell(NavigationHelperSpell source,
        BestHelpSpellScenario? scenario, out Spell spell)
    {
        spell = default!;
        if (source.State != NavigationKnowledgeState.Known || scenario?.TargetCount < 0 ||
            !TryLevelInt(source.ManaCosts, 1, out var cost1) ||
            !TryLevelInt(source.ManaCosts, 2, out var cost2) ||
            !TryLevelInt(source.Cooldowns, 1, out var cooldown1) ||
            !TryLevelInt(source.Cooldowns, 2, out var cooldown2))
            return false;
        var damage1 = scenario?.Level1DamagePerTarget ?? ParsedDamage(source, 1);
        var damage2 = scenario?.Level2DamagePerTarget ?? ParsedDamage(source, 2);
        if (damage1 is null || damage2 is null || damage1 < 0 || damage2 < 0)
            return false;
        var targets = scenario?.TargetCount ?? 1;
        spell = new(source.Id, cost1, cost2, cooldown1, cooldown2,
            checked(damage1.Value * targets), checked(damage2.Value * targets),
            checked((scenario?.Level1ControlTargetSeconds ?? 0) * targets),
            checked((scenario?.Level2ControlTargetSeconds ?? 0) * targets),
            scenario?.CoreDeficitsClosedLevel1 ?? 0,
            scenario?.CoreDeficitsClosedLevel2 ?? 0);
        return true;
    }

    private static long? ParsedDamage(NavigationHelperSpell source, int level)
    {
        var value = source.Levels.SingleOrDefault(item => item.Level == level)?.Value;
        if (value is null) return null;
        var token = value.Split(',').SingleOrDefault(item =>
            item.StartsWith("damage:", StringComparison.Ordinal));
        return token is not null && long.TryParse(token.AsSpan(7), out var damage)
            ? damage : null;
    }

    private static bool TryLevelInt(ImmutableArray<NavigationLevelValue> values,
        int level, out int result) => int.TryParse(
        values.SingleOrDefault(value => value.Level == level)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 0;

    private static int Parameter(NavigationTransition transition, string name) =>
        transition.Integers.Single(value => value.Name == name).Value;

    private static int[] ParseCooldowns(string value) => value.Length == 0
        ? [] : value.Split(',').Select(int.Parse).ToArray();

    private static void KeepBest(Dictionary<StateKey, Plan> states, StateKey key, Plan plan)
    {
        if (!states.TryGetValue(key, out var previous) || Better(previous, plan) == plan)
            states[key] = plan;
    }

    private static Plan Better(Plan left, Plan right) =>
        left.Core != right.Core ? (left.Core > right.Core ? left : right) :
        left.Damage != right.Damage ? (left.Damage > right.Damage ? left : right) :
        left.Control >= right.Control ? left : right;

    private static int RoundRatioBp(long value, long denominator) => denominator <= 0
        ? 0 : (int)Math.Min(10_000, decimal.Round(10_000m * value / denominator,
            0, MidpointRounding.AwayFromZero));
    private static int RoundAway(decimal value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static BestHelpMaximumResult InvalidMaximum() => new()
    {
        Disposition = BestHelpSimulationDisposition.InvalidInput,
        UnknownSpellIds = []
    };

    private readonly record struct StateKey(int ManaTenths, string Cooldowns);
    private sealed record Spell(string Id, int Cost1, int Cost2, int Cooldown1,
        int Cooldown2, long Damage1, long Damage2, long Control1, long Control2,
        int Core1, int Core2)
    {
        public int Cost(int level) => level == 1 ? Cost1 : Cost2;
        public int Cooldown(int level) => level == 1 ? Cooldown1 : Cooldown2;
        public long Damage(int level) => level == 1 ? Damage1 : Damage2;
        public long Control(int level) => level == 1 ? Control1 : Control2;
        public int Core(int level) => level == 1 ? Core1 : Core2;
    }

    private sealed record Plan(int Core, long Damage, long Control,
        ImmutableArray<BestHelpCast> Casts)
    {
        public static Plan Empty { get; } = new(0, 0, 0, []);
        public Plan Cast(Spell spell, int level, int time) => new(
            checked(Core + spell.Core(level)), checked(Damage + spell.Damage(level)),
            checked(Control + spell.Control(level)), Casts.Add(new(spell.Id, time,
                spell.Cost(level), spell.Damage(level), spell.Control(level),
                spell.Core(level))));
    }
}
