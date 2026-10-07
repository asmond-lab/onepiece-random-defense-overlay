using System.Collections.Immutable;

namespace OrandOverlay;

// A cooldown observation alone does not establish target eligibility or ability availability.
public sealed record HelperAbilityState(string Rawcode, int Level, float? CooldownRemaining);

public sealed record HelperUnitState(float Mana, float MaximumMana, ImmutableArray<HelperAbilityState> Abilities);
