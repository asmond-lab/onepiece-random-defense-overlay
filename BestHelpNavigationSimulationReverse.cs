using System.Collections.Immutable;

namespace OrandOverlay;

public sealed partial class BestHelpNavigationSimulation
{
    public BestHelpReverseResult SimulateReverse(BestHelpReverseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.RayleighUnitId) ||
            input.RayleighRouteGainBp < 0 || input.ExcavationStackGainBp < 0 ||
            input.HelperActionValuesBp.Any(pair => pair.Value < 0))
            return InvalidReverse(input.RayleighUnitId ?? string.Empty);

        var transition = _profile.Transitions.Single(value =>
            value.Id == "reverse-grant-restriction");
        var maximumManaCost = Parameter(transition, "maximumHelperManaCost");
        var lost = ImmutableArray.CreateBuilder<string>();
        var unknown = ImmutableArray.CreateBuilder<string>();
        foreach (var spell in _profile.HelperSpells.Where(value => value.Id != "A055"))
        {
            var costs = spell.ManaCosts.Select(value =>
                int.TryParse(value.Value, out var cost) ? cost : (int?)null).ToArray();
            if (spell.State != NavigationKnowledgeState.Known || costs.Length == 0 ||
                costs.Any(value => value is null))
            {
                unknown.Add(spell.Id);
                continue;
            }
            if (input.RemovedHelperActionIds.Contains(spell.Id) ||
                costs.Max() > maximumManaCost)
                lost.Add(spell.Id);
        }
        foreach (var removed in input.RemovedHelperActionIds.Order(StringComparer.Ordinal))
        {
            if (_profile.HelperSpells.All(spell => spell.Id != removed))
                unknown.Add(removed);
        }
        if (unknown.Count != 0)
            return new()
            {
                Disposition = BestHelpSimulationDisposition.ScenarioGated,
                RayleighUnitId = input.RayleighUnitId,
                LostHelperActionIds = lost.Distinct().Order(StringComparer.Ordinal).ToImmutableArray(),
                UnknownSpellIds = unknown.Distinct().Order(StringComparer.Ordinal).ToImmutableArray()
            };

        var losses = lost.Distinct().Sum(id =>
            input.HelperActionValuesBp.GetValueOrDefault(id));
        var rayleigh = Parameter(transition, "rayleigh");
        var stacks = Parameter(transition, "excavationStack");
        return new()
        {
            Disposition = BestHelpSimulationDisposition.Confirmed,
            RayleighUnitId = input.RayleighUnitId,
            RayleighCount = rayleigh,
            ExcavationStacks = stacks,
            LostHelperActionIds = lost.Distinct().Order(StringComparer.Ordinal).ToImmutableArray(),
            HelperLossBp = losses,
            NetRouteGainBp = checked(rayleigh * input.RayleighRouteGainBp +
                stacks * input.ExcavationStackGainBp - losses),
            UnknownSpellIds = []
        };
    }

    private static BestHelpReverseResult InvalidReverse(string rayleighUnitId) => new()
    {
        Disposition = BestHelpSimulationDisposition.InvalidInput,
        RayleighUnitId = rayleighUnitId,
        LostHelperActionIds = [],
        UnknownSpellIds = []
    };
}
