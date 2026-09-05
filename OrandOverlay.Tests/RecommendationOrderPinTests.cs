using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecommendationOrderPinTests
{
    private static (DataCatalog Catalog, RecommendationEngine Engine) Create()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        return (catalog, new RecommendationEngine(catalog));
    }

    private static List<InventoryEntry> Inventory(params (string id, int count)[] items) =>
        items.Select(x => new InventoryEntry { UnitId = x.id, Count = x.count }).ToList();

    private static string Render(IReadOnlyList<Recommendation> recs) =>
        string.Join("|", recs.Select(r => r.Route.Id + "#" + r.Score.ToString("F4")));

    [Fact]
    public void Yamato_PartialRareHand_OrderIsPinned()
    {
        var (_, engine) = Create();
        var recs = engine.RecommendNearestCrafts("yamato_transcendent",
            Inventory(("mobydick", 1), ("item_greenblood", 1), ("slow_support", 2)));
        Assert.Equal("craft:yamato_transcendent#0.0000|craft:rawcode:780h#0.0000|craft:rawcode:S30h#0.0000|craft:rawcode:O30h#0.0000|craft:dragon_legend#0.0000|craft:rawcode:3A0h#2.7778|craft:rawcode:W50h#0.0000|craft:mihawk_hidden#0.0000|craft:rawcode:W30h#0.0000|craft:rawcode:HA0h#0.0000|craft:rawcode:N30h#0.0000|craft:rawcode:M10h#0.0000|craft:rawcode:D20h#0.0000", Render(recs));
    }

    [Fact]
    public void Yamato_NearEmptyHand_OrderIsPinned()
    {
        var (_, engine) = Create();
        var recs = engine.RecommendNearestCrafts("yamato_transcendent",
            Inventory(("luffy_common", 1)));
        Assert.Equal("craft:yamato_transcendent#1.1364|craft:rawcode:780h#0.0000|craft:rawcode:S30h#0.0000|craft:rawcode:O30h#0.0000|craft:dragon_legend#0.0000|craft:rawcode:W50h#0.0000|craft:rawcode:X90h#0.0000|craft:mihawk_hidden#0.0000|craft:rawcode:3A0h#0.0000|craft:rawcode:HA0h#0.0000|craft:rawcode:N30h#0.0000|craft:rawcode:M10h#0.0000|craft:rawcode:D20h#0.0000|craft:rawcode:H20h#0.0000", Render(recs));
    }

    [Fact]
    public void Yamato_WithNavigationAndGorosei_OrderIsPinned()
    {
        var (_, engine) = Create();
        var recs = engine.RecommendNearestCrafts("yamato_transcendent",
            Inventory(("mobydick", 1), ("item_greenblood", 1), ("kalgara", 1),
                ("armor_support", 1)),
            navigationMode: "PathOfKings.BountyHunter");
        Assert.Equal("craft:yamato_transcendent#0.0000|craft:rawcode:780h#0.0000|craft:rawcode:S30h#0.0000|craft:rawcode:O30h#0.0000|craft:dragon_legend#0.0000|craft:rawcode:3A0h#2.7778|craft:rawcode:W30h#0.0000|craft:rawcode:N30h#0.0000|craft:rawcode:V50h#0.0000|craft:rawcode:F10h#0.0000|craft:rawcode:M10h#0.0000", Render(recs));
    }
}
