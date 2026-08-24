using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

/// <summary>
/// 리팩터링(RecipeTreeBuilder/GoalStrategyCalculator 추출) 전후 추천 순서가
/// 비트 단위로 동일하게 유지되는지 감시하는 특성화 테스트.
/// 값은 v0.6.38 물딜 공통 최소 코어 정책의 실제 출력을 캡처해 박제한 것이다.
/// 스턴 1.4가 방깎보다 먼저 오는 정확 순서를 고정한다.
/// </summary>
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
        Assert.Equal("craft:yamato_transcendent#0.0000|craft:rawcode:S30h#0.0000|craft:rawcode:780h#0.0000|craft:rawcode:O30h#0.0000|craft:dragon_legend#0.0000|craft:rawcode:V50h#0.0000|craft:rawcode:HA0h#0.0000|craft:rawcode:X90h#0.0000", Render(recs));
    }

    [Fact]
    public void Yamato_NearEmptyHand_OrderIsPinned()
    {
        var (_, engine) = Create();
        var recs = engine.RecommendNearestCrafts("yamato_transcendent",
            Inventory(("luffy_common", 1)));
        Assert.Equal("craft:yamato_transcendent#1.1364|craft:rawcode:S30h#0.0000|craft:rawcode:780h#0.0000|craft:rawcode:O30h#0.0000|craft:dragon_legend#0.0000|craft:rawcode:V50h#0.0000|craft:rawcode:HA0h#0.0000|craft:rawcode:X90h#0.0000", Render(recs));
    }

    [Fact]
    public void Yamato_WithNavigationAndGorosei_OrderIsPinned()
    {
        var (_, engine) = Create();
        var recs = engine.RecommendNearestCrafts("yamato_transcendent",
            Inventory(("mobydick", 1), ("item_greenblood", 1), ("kalgara", 1),
                ("armor_support", 1)),
            navigationMode: "PathOfKings.BountyHunter");
        Assert.Equal("craft:yamato_transcendent#0.0000|craft:rawcode:S30h#0.0000|craft:rawcode:780h#0.0000|craft:rawcode:O30h#0.0000|craft:dragon_legend#0.0000|craft:rawcode:V50h#0.0000|craft:rawcode:HA0h#0.0000|craft:rawcode:X90h#0.0000", Render(recs));
    }
}
