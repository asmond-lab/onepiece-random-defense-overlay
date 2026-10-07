using System.Collections.Immutable;
using OrandOverlay;
namespace PlannerEvidenceCapture;

// Code-only synthetic input catalog. No user hand, success receipt, or predicted reward is injected.
internal sealed record FastUniqueUiCase(string Name, int Round, bool Boundary,
    IReadOnlyDictionary<string, int> Inventory)
{
    public int Story { get; init; }
    public ImmutableDictionary<string, int> Wisps { get; init; } = ImmutableDictionary<string, int>.Empty;
    public NativeNavigationSnapshot Navigation { get; init; } = NativeNavigationSnapshot.Unknown;
    public long? Lumber { get; init; }
}

internal static class FastUniqueUiFixture
{
    internal static RecognitionResult Request(FastUniqueUiCase item, long generation, long revision)
    {
        var entries = item.Inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToList();
        return new RecognitionResult
        {
            State = RecognitionState.Ready, Entries = entries,
            Status = "화면 검수용 예시 · 실제 플레이 기록이 아닙니다",
            ConfirmsSessionBoundary = item.Boundary,
            MapSignals = new MapSignals(Math.Min(14, item.Story + 1), null, item.Story, item.Wisps)
                { NativeNavigation = item.Navigation },
            RecommendationInputs = item.Lumber is { } lumber ? new NavigationStateSnapshot(
                generation, revision, RuntimeRecommendationSnapshotState.Current, true,
                [new(PlanningValue.Known("lumber", lumber), 10000, [])]) : null,
            Diagnostics = BulletSyntheticGoroseiProducer.Diagnostics(item.Round, entries, GoroseiMode.None, null, null)
        };
    }

    internal static IReadOnlyList<FastUniqueUiCase> Build(DataCatalog catalog)
    {
        var rare = catalog.Unit("rawcode:L50h").Recipe;
        var common = ImmutableDictionary<string, int>.Empty.Add(catalog.Unit("rawcode:300h").Id, 1);
        var sabo = ImmutableDictionary<string, int>.Empty.Add("rawcode:E10h", 1);
        var cases = Enumerable.Range(0, 8).Select(round => new FastUniqueUiCase($"first-rare-{round}", round,
            false, rare)).ToList();
        cases.Add(new("deadline-8", 8, false, rare));
        cases.Add(new("rare-owned-7", 7, true, ImmutableDictionary<string, int>.Empty.Add("rawcode:L50h", 1)));
        cases.Add(new("rare-consumed-7", 7, false, rare));
        cases.Add(new("new-session-7", 7, true, rare));
        foreach (var reward in new[] { "e016", "e017", "e019" })
        {
            cases.Add(new($"{reward}-unreceived", 7, true, common));
            cases.Add(new($"{reward}-received", 7, false, common) { Wisps = Wisp(reward, 1) });
            cases.Add(new($"{reward}-repeat", 7, false, common) { Wisps = Wisp(reward, 1) });
            // Consumption observation alone does not invent the resulting unit.
            cases.Add(new($"{reward}-spent", 7, false, common));
            cases.Add(new($"{reward}-ready-rare", 7, false, rare) { Wisps = Wisp(reward, 1) });
        }
        cases.Add(new("selection-1", 9, true, sabo) { Wisps = Wisp("e018", 1) });
        cases.Add(new("selection-3", 9, false, sabo) { Wisps = Wisp("e018", 3) });
        // The user may choose manually even when guidance recommends conserving wisps.
        cases.Add(new("selection-after-one", 9, false, sabo.Add(catalog.Unit("rawcode:300h").Id, 1))
            { Wisps = Wisp("e018", 2) });
        var zombie = catalog.Unit("rawcode:K50h").Recipe.Where(p => p.Key != "rawcode:010h")
            .ToImmutableDictionary().Add(catalog.Unit("rawcode:100h").Id, 1);
        cases.Add(new("zombie-nonselectable", 7, true, zombie) { Wisps = Wisp("e018", 3) });
        cases.Add(new("actual-unknown", 24, true, sabo));
        cases.Add(new("actual-other", 24, false, sabo) { Navigation = Selected("AlliedForces.EmergencyCall") });
        var bullet = catalog.Unit(BulletGuidePolicy.GoalId).Recipe.ToImmutableDictionary(p => p.Key, p => p.Value * 2)
            .Add(catalog.Unit("rawcode:300h").Id, 20);
        // Surviving source support is an explicit positive-control hand, not an override of the produced plan.
        foreach (var code in new[] { "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Y30h", "Q30h", "K50h" })
            bullet = bullet.SetItem("rawcode:" + code, 1);
        cases.Add(new("bullet-zero-to-one", 50, true, bullet)
            { Story = 13, Navigation = Selected(BulletGuidePolicy.NavigationId), Lumber = 100 });
        cases.Add(new("bullet-owned-no-recraft", 50, false, bullet.Add(BulletGuidePolicy.GoalId, 1))
            { Story = 13, Navigation = Selected(BulletGuidePolicy.NavigationId), Lumber = 100 });
        return cases;
    }
    private static ImmutableDictionary<string, int> Wisp(string id, int count) =>
        ImmutableDictionary<string, int>.Empty.Add(id, count);
    private static NativeNavigationSnapshot Selected(string id) =>
        new(NativeNavigationStatus.Selected, id, "Synthetic selected identity, not live navigation evidence");
}
