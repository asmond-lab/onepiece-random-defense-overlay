namespace OrandOverlay;

public sealed record BulletUpgradeCounts(int Attack, int Speed, int Armor)
{
    public static BulletUpgradeCounts? FromItems(
        IReadOnlyDictionary<int, (string Rawcode, int Charges)> items)
    {
        int? Count(int slot, string normal, string maximum)
        {
            if (!items.TryGetValue(slot, out var item)) return null;
            if (item.Rawcode == maximum) return 30;
            return item.Rawcode == normal && item.Charges is >= 1 and <= 30 ? item.Charges : null;
        }
        return Count(0, "I091", "I006") is { } attack &&
               Count(2, "I092", "I007") is { } speed &&
               Count(4, "I093", "I008") is { } armor
            ? new(attack, speed, armor) : null;
    }
}
