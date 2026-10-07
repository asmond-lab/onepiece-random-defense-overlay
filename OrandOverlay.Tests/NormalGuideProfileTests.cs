using System.Text;
using System.Text.Json.Nodes;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalGuideProfileTests
{
    [Fact]
    public void FlagMembershipMatchesAllRareLegendAndHiddenSourceEntries()
    {
        var profile = NormalGuideProfile.LoadBundled();
        string[] expected =
        [
            "rawcode:Q10h", "rawcode:Y10h", "rawcode:E20h", "rawcode:X90h", "rawcode:I20h",
            "rawcode:K20h", "rawcode:X10h", "rawcode:R10h", "rawcode:Z10h", "rawcode:620h",
            "rawcode:G30h", "rawcode:V20h", "rawcode:MC0h", "rawcode:S30h", "kalgara",
            "rawcode:HA0h", "rawcode:W30h", "rawcode:N30h", "rawcode:M30h", "mihawk_hidden",
            "rawcode:Q20h", "rawcode:030h", "rawcode:740h", "rawcode:R20h", "rawcode:730h",
            "rawcode:430h", "rawcode:Z90h", "rawcode:X20h", "rawcode:Y20h", "rawcode:S20h",
            "rawcode:230h", "rawcode:240h", "rawcode:I30h", "rawcode:U20h", "rawcode:Z30h", "rawcode:J30h"
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal),
            profile.Where(unit => unit.StoryFast).Select(unit => unit.UnitId).Order(StringComparer.Ordinal));
        Assert.False(Unit(profile, "rawcode:5B0H").StoryFast);
        Assert.False(Unit(profile, "rawcode:L20h").StoryFast);
    }

    [Fact]
    public void EveryMappedIdResolvesAgainstThe2320CatalogWithoutNameGuessing()
    {
        var profile = NormalGuideProfile.LoadBundled();
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.320");
        var ids = catalog.AllUnits.Select(unit => unit.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(168, profile.Count);
        Assert.All(profile, unit => Assert.Contains(unit.UnitId, ids));
        Assert.All(profile.SelectMany(unit => unit.RecommendedPartners), id => Assert.Contains(id, ids));
        Assert.Equal("physical", Unit(profile, "kalgara").DamageType);
        Assert.Equal("physical", Unit(profile, "mihawk_hidden").DamageType);
        Assert.DoesNotContain(profile, unit => unit.UnitId is "rawcode:F30h" or "rawcode:340h" or "rawcode:DA0h");
    }

    [Fact]
    public void DamageKeepsExplicitDualUseAndUnknownSeparateFromSupportRoles()
    {
        var profile = NormalGuideProfile.LoadBundled();
        string[] both = ["rawcode:AA0H", "rawcode:XB0H", "rawcode:N50H", "rawcode:B40h", "rawcode:760h", "rawcode:C50h"];
        Assert.Equal(both.Order(StringComparer.Ordinal),
            profile.Where(unit => unit.DamageType == "both").Select(unit => unit.UnitId).Order(StringComparer.Ordinal));
        Assert.Equal("physical", Unit(profile, "rawcode:Q90h").DamageType);
        Assert.Equal("magical", Unit(profile, "rawcode:O80h").DamageType);
        Assert.Equal("unknown", Unit(profile, "rawcode:740h").DamageType);
        Assert.Equal("unknown", Unit(profile, "rawcode:Q10h").DamageType);
        Assert.Equal("magical", Unit(profile, "rawcode:W80H").DamageType);
    }

    [Fact]
    public void AirTerrainIgnoreAndTeleportRemainDistinct()
    {
        var profile = NormalGuideProfile.LoadBundled();
        Assert.Equal(["지형무시이동"], Unit(profile, "rawcode:740h").MovementRoles);
        Assert.Equal(["공중이동"], Unit(profile, "kalgara").MovementRoles);
        Assert.Equal(["순간이동"], Unit(profile, "rawcode:R10h").MovementRoles);
        Assert.Equal(["공중이동", "순간이동"], Unit(profile, "rawcode:OC0H").MovementRoles);
        Assert.Equal(["순간이동"], Unit(profile, "rawcode:G50h").MovementRoles);
        Assert.Empty(Unit(profile, "rawcode:G30h").MovementRoles);
        Assert.Empty(Unit(profile, "rawcode:F40h").MovementRoles);
    }

    [Fact]
    public void ExplicitAuthorPartnersRemainSpecificToTheAnchorAndPreserveOtherTierReferences()
    {
        var profile = NormalGuideProfile.LoadBundled();
        Assert.Equal(["rawcode:V20h", "rawcode:IC0h", "rawcode:W30h"],
            Unit(profile, "rawcode:A90H").RecommendedPartners);
        Assert.Equal(["rawcode:C30h", "rawcode:P30h", "rawcode:780h"],
            Unit(profile, "rawcode:G40h").RecommendedPartners);
        Assert.Equal(["rawcode:4B0H", "rawcode:Q40h", "rawcode:J70h"],
            Unit(profile, "rawcode:O80h").RecommendedPartners);
        Assert.DoesNotContain("rawcode:V20h", Unit(profile, "rawcode:G40h").RecommendedPartners);
        Assert.Empty(Unit(profile, "rawcode:750h").RecommendedPartners);
        Assert.Empty(Unit(profile, "rawcode:5B0H").RecommendedPartners);
        Assert.Empty(Unit(profile, "rawcode:Q10h").RecommendedPartners);
    }

    [Fact]
    public void KnownCatalogFilterDropsUnknownPartnersWithoutDroppingTheAnchor()
    {
        var root = BundledJson();
        root["units"]![0]!["recommendedPartners"] = new JsonArray("rawcode:V20h", "rawcode:ZZZZ", "unknown_partner");
        var profile = NormalGuideProfile.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), ["rawcode:V20h"]);
        Assert.Equal(["rawcode:V20h"], Unit(profile, "rawcode:Q10h").RecommendedPartners);
        Assert.Equal(168, profile.Count);
        root["units"]![0]!.AsObject().Remove("recommendedPartners");
        Assert.Empty(Unit(Parse(root), "rawcode:Q10h").RecommendedPartners);
        root["units"]![0]!["recommendedPartners"] = new JsonArray("항법-특성공학");
        Assert.Throws<InvalidDataException>(() => Parse(root));
        Assert.Empty(new NormalGuideUnit("sample", false, "unknown", []).RecommendedPartners);
    }

    [Theory]
    [InlineData("guideId", "48784")]
    [InlineData("sourceUrl", "https://tmo.gg/g/ord/build-helper/43747")]
    [InlineData("catalogMapVersion", "2.314")]
    [InlineData("status", "combat-verified")]
    public void ForeignSourceOrCombatStatusIsRejected(string property, string value)
    {
        var root = BundledJson();
        root[property] = value;
        Assert.Throws<InvalidDataException>(() => Parse(root));
    }

    [Fact]
    public void CorruptDirectionMovementAndNumericFlagsCannotBecomeLabels()
    {
        var root = BundledJson();
        root["units"]![0]!["damageType"] = "physical-magical";
        Assert.Throws<InvalidDataException>(() => Parse(root));
        root = BundledJson();
        root["units"]![0]!["movementRoles"] = new JsonArray("공중/지형이동");
        Assert.Throws<InvalidDataException>(() => Parse(root));
        root = BundledJson();
        root["units"]![0]!["storyFast"] = 1;
        Assert.Throws<InvalidDataException>(() => Parse(root));
        root = BundledJson();
        root["units"]!.AsArray().Add(root["units"]![0]!.DeepClone());
        Assert.Throws<InvalidDataException>(() => Parse(root));
    }

    private static NormalGuideUnit Unit(IReadOnlyList<NormalGuideUnit> profile, string id) =>
        Assert.Single(profile, unit => unit.UnitId == id);

    private static JsonObject BundledJson() => JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Data", NormalGuideProfile.FileName)))!.AsObject();

    private static IReadOnlyList<NormalGuideUnit> Parse(JsonObject root) =>
        NormalGuideProfile.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
}
