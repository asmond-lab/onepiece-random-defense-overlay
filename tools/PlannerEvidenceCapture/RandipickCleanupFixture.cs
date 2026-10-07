using OrandOverlay;
namespace PlannerEvidenceCapture;

internal static class RandipickCleanupFixture
{
    internal static IReadOnlyList<FastUniqueUiCase> Build(DataCatalog catalog)
    {
        var source = FastUniqueUiFixture.Build(catalog);
        var cases = new[] { "first-rare-7", "selection-1", "e016-received", "actual-other" }
            .Select(name => source.Single(item => item.Name == name) with { Boundary = true }).ToList();
        var navigationConflict = source.Single(item => item.Name == "actual-other") with
        {
            Name = "navigation-conflict-held", Boundary = true,
            Navigation = new(NativeNavigationStatus.Conflict, null, "Synthetic JP/SP conflicting identity; no native/game access")
        };
        // Sequence 365's recorded hand, replayed after the round advances to 19.
        var liveHand = new Dictionary<string, int>
        {
            ["luffy_common"] = 3, ["rawcode:320h"] = 1, ["rawcode:N00h"] = 2,
            ["rawcode:J10h"] = 1, ["rawcode:U10h"] = 1, ["rawcode:R00h"] = 1,
            ["rawcode:500h"] = 3, ["rawcode:210h"] = 1, ["rawcode:200h"] = 3,
            ["rawcode:C00h"] = 1, ["rawcode:F00h"] = 1, ["rawcode:D20h"] = 1,
            ["rawcode:800h"] = 1, ["rawcode:410h"] = 1, ["rawcode:700h"] = 3,
            ["rawcode:600h"] = 2, ["rawcode:X00h"] = 1, ["rawcode:110h"] = 1,
            ["rawcode:L50h"] = 2, ["rawcode:900h"] = 1, ["rawcode:C10h"] = 1,
            ["rawcode:D10h"] = 1, ["rawcode:100h"] = 1, ["rawcode:B10h"] = 1,
            ["rawcode:W00h"] = 1, ["rawcode:400h"] = 5, ["rawcode:G20h"] = 2,
            ["rawcode:D00h"] = 1, ["rawcode:910h"] = 1, ["rawcode:T00h"] = 1,
            ["rawcode:E00h"] = 2
        };
        cases.Add(new("legend-19-progress", 19, true, liveHand) { Story = 8, Lumber = 16 });
        var chainHand = liveHand.ToDictionary();
        string[] chain = ["F00h", "V00h", "V00h", "220h", "E20h", "J20h", "B30h"];
        for (var index = 0; index < chain.Length; index++)
        {
            cases.Add(new($"legend-chain-{index}", 19, true, chainHand.ToDictionary()) { Story = 8, Lumber = 16 });
            chainHand = BulletGuideCraftSafety.ProjectAfterCraft(catalog, "rawcode:" + chain[index], chainHand)
                ?? throw new InvalidOperationException("Recorded legend chain has missing ingredients.");
            chainHand = chainHand.Where(pair => pair.Value > 0).ToDictionary();
        }
        cases.Add(new("legend-chain-complete", 19, true, chainHand) { Story = 8, Lumber = 13 });
        var auxiliaryConsumable = catalog.Unit("rawcode:B30h").Recipe.Where(p => catalog.Unit(p.Key).Tier != "자원")
            .ToDictionary(p => p.Key, p => p.Value);
        var marcoCount = auxiliaryConsumable["rawcode:220h"];
        auxiliaryConsumable.Remove("rawcode:220h");
        foreach (var ingredient in catalog.Unit("rawcode:220h").Recipe.Where(p => catalog.Unit(p.Key).Tier != "자원"))
            auxiliaryConsumable[ingredient.Key] = auxiliaryConsumable.GetValueOrDefault(ingredient.Key) + ingredient.Value * marcoCount;
        auxiliaryConsumable["rawcode:HA0h"] = 1; // Later progression may consume the last auxiliary Chopper.
        cases.Add(new("legend-19-auxiliary-consumable", 19, true, auxiliaryConsumable) { Story = 8, Lumber = 16 });
        var auxiliarySpare = auxiliaryConsumable.ToDictionary();
        auxiliarySpare["rawcode:D10h"]++;
        cases.Add(new("legend-19-auxiliary-spare", 19, true, auxiliarySpare) { Story = 8, Lumber = 16 });
        cases.Add(navigationConflict);
        return cases;
    }
}
