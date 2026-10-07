using System.Collections.Immutable;
using System.Text.Json;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class HandStatsProfileTests
{
    [Fact]
    public void InjectedProfileOverridesOnlyMatchedUnitAndDisclosesProvenance()
    {
        var catalog = LoadCatalog();
        var before = catalog.Unit("rawcode:W30h").OfficialAbilities
            .Select(ability => (ability.Name, ability.DisplayValue)).ToArray();
        var profile = Profile(Unit("W30h", new Dictionary<string, HandStatsAbility>
        {
            ["발동방어력 감소"] = new(5, null, null),
            ["이동속도 감소"] = new(0, null, null)
        }, notes: ["조건부 발동"]));

        var summary = new InventoryStatsCalculator(catalog, profile).Calculate(
            [new InventoryEntry { UnitId = "rawcode:W30h", Count = 2 }]);

        Assert.Equal(10, summary.TriggeredArmorReduction); // Not legacy W30h +30 per copy.
        Assert.Equal(0, summary.Slow); // Explicit source zero remains distinct from absent.
        Assert.Equal(2, summary.SourceUnitCount);
        Assert.Equal(0, summary.UnlistedUnitCount);
        Assert.Equal("source profile", summary.ProfileStatus);
        Assert.Contains("조건부 발동", summary.SourceNotes);
        Assert.Equal(before, catalog.Unit("rawcode:W30h").OfficialAbilities
            .Select(ability => (ability.Name, ability.DisplayValue)).ToArray());
    }

    [Fact]
    public void BooleanAndPositiveNumericProvidersRemainSeparateFromTextualUnknowns()
    {
        var catalog = LoadCatalog();
        var profile = Profile(Unit("H90H", new Dictionary<string, HandStatsAbility>
        {
            ["단일"] = new(null, true, null),
            ["끝딜"] = new(1, null, null),
            ["이동속도 감소"] = new(null, null, "수치 미상")
        }));

        var summary = new InventoryStatsCalculator(catalog, profile).Calculate(
            [new InventoryEntry { UnitId = "rawcode:G90H", Count = 1 }]);

        Assert.Equal(1, summary.SingleDamageProviders);
        Assert.Equal(1, summary.FinisherDamageProviders);
        Assert.Equal(1, summary.FinisherDamageWeight);
        Assert.Equal(0, summary.Slow);
        Assert.Equal(1, summary.UnknownValueUnitCount);
    }

    [Fact]
    public void CanonicalAliasUsesProfileWithoutNameMatching()
    {
        var catalog = LoadCatalog();
        var profile = Profile(Unit("H90H", new Dictionary<string, HandStatsAbility>
        {
            ["스턴"] = new(1.25, null, null)
        }));

        var summary = new InventoryStatsCalculator(catalog, profile).Calculate(
            [new InventoryEntry { UnitId = "rawcode:G90H", Count = 2 }]);

        Assert.Equal(2.5, summary.Stun);
        Assert.Equal(2, summary.SourceUnitCount);
    }

    [Fact]
    public void SingleTargetAndMagicDamageFieldsRemainSeparate()
    {
        var catalog = LoadCatalog();
        var profile = Profile(Unit("W30h", new Dictionary<string, HandStatsAbility>
        {
            ["단일방어력 감소"] = new(12, null, null),
            ["단일마법 데미지 증가"] = new(7, null, null),
            ["폭발형 데미지 증폭"] = new(3, null, null),
            ["모든피해증가"] = new(-2, null, null)
        }));

        var summary = new InventoryStatsCalculator(catalog, profile).Calculate(
            [new InventoryEntry { UnitId = "rawcode:W30h", Count = 1 }]);

        Assert.Equal(12, summary.SingleArmorReduction);
        Assert.Equal(7, summary.SingleMagicAmp);
        Assert.Equal(3, summary.ExplosionAmp);
        Assert.Equal(-2, summary.AllDamageAmp);
    }

    [Fact]
    public void MatchedSourceEntriesDoNotReceiveLegacyW30hOrG30hBonuses()
    {
        var catalog = LoadCatalog();
        var profile = Profile(
            Unit("W30h", new Dictionary<string, HandStatsAbility>
            {
                ["아머브레이크"] = new(null, true, null),
                ["발동방어력 감소"] = new(2, null, null)
            }),
            Unit("G30h", new Dictionary<string, HandStatsAbility>
            {
                ["발동방어력 감소"] = new(4, null, null)
            }));

        var summary = new InventoryStatsCalculator(catalog, profile).Calculate(
        [
            new InventoryEntry { UnitId = "rawcode:W30h", Count = 1 },
            new InventoryEntry { UnitId = "rawcode:G30h", Count = 1 }
        ]);

        Assert.Equal(6, summary.TriggeredArmorReduction); // No legacy +30 or conditional +25.
        Assert.Equal(1, summary.ArmorBreakProviders);
    }

    [Fact]
    public void NumericPositiveSingleWeightCountsOneProviderPerOwnedUnit()
    {
        var catalog = LoadCatalog();
        var profile = Profile(Unit("W30h", new Dictionary<string, HandStatsAbility>
        {
            ["단일"] = new(0.5, null, null)
        }));

        var summary = new InventoryStatsCalculator(catalog, profile).Calculate(
            [new InventoryEntry { UnitId = "rawcode:W30h", Count = 2 }]);

        Assert.Equal(2, summary.SingleDamageProviders);
        Assert.Equal(1, summary.SingleDamageWeight);
    }

    [Fact]
    public void ReviewedNonStackingGroupsUseMaximumAndSourceStackingStaysUnobserved()
    {
        var catalog = LoadCatalog();
        var profile = Profile(
            Unit("W30h", new Dictionary<string, HandStatsAbility>
            {
                ["방어력 감소"] = new(10, null, null),
                ["중첩방어력 감소"] = new(5, null, null)
            }, groups: new Dictionary<string, string> { ["방어력 감소"] = "hawkins-special-rare-armor" }),
            Unit("G30h", new Dictionary<string, HandStatsAbility>
            {
                ["방어력 감소"] = new(15, null, null),
                ["중첩방어력 감소"] = new(2, null, null)
            }, groups: new Dictionary<string, string> { ["방어력 감소"] = "hawkins-special-rare-armor" }));

        var summary = new InventoryStatsCalculator(catalog, profile).Calculate(
        [
            new InventoryEntry { UnitId = "rawcode:W30h", Count = 2 },
            new InventoryEntry { UnitId = "rawcode:G30h", Count = 1 }
        ]);

        Assert.Equal(15, summary.ArmorReduction);
        Assert.Equal(12, summary.StackingArmorReduction);
        Assert.Equal(12, summary.UnobservedStackingArmorReduction);
        Assert.Equal(15, summary.TotalArmorReduction);
    }

    [Fact]
    public void EmptyAndUnsupportedSourceAbilitiesAreUnknownAndDisclosed()
    {
        var catalog = LoadCatalog();
        var empty = new InventoryStatsCalculator(catalog, Profile(Unit("W30h", new Dictionary<string, HandStatsAbility>())))
            .Calculate([new InventoryEntry { UnitId = "rawcode:W30h" }]);
        var conditional = new InventoryStatsCalculator(catalog, Profile(Unit("W30h", new Dictionary<string, HandStatsAbility>
        {
            ["조건부방어력 감소"] = new(9, null, null)
        }))).Calculate([new InventoryEntry { UnitId = "rawcode:W30h" }]);

        Assert.Equal(1, empty.UnknownValueUnitCount);
        Assert.Equal(1, conditional.UnknownValueUnitCount);
        Assert.Equal(0, conditional.ArmorReduction);
        Assert.Contains("조건부방어력 감소", conditional.SourceNotes);
        Assert.Contains("합계 제외", conditional.SourceNotes);
    }

    [Fact]
    public void EmptySourceTextAndDistinctAliasFormRowsAreAccepted()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Json("48784", "G90H").Replace("\"sourceText\":\"x\"", "\"sourceText\":\"\""));
            var loaded = HandStatsProfile.LoadFromFile(path);
            var forms = Profile(
                Unit("H90H", new Dictionary<string, HandStatsAbility> { ["스턴"] = new(1, null, null) }),
                Unit("G90H", new Dictionary<string, HandStatsAbility> { ["스턴"] = new(2, null, null) }));

            Assert.True(loaded.TryGet("G90H", out var loadedForm));
            Assert.Equal("", loadedForm.SourceText);
            Assert.True(forms.TryGet("H90H", out _));
            Assert.True(forms.TryGet("G90H", out _));
            var summary = new InventoryStatsCalculator(LoadCatalog(), forms).Calculate(
                [new InventoryEntry { UnitId = "rawcode:G90H" }]);
            Assert.Equal(2, summary.Stun);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExistingFileRejectsDuplicateAndInvalidSourceInsteadOfPartialInstall()
    {
        var duplicatePath = Path.GetTempFileName();
        var invalidPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(duplicatePath, Json("48784", "W30h", "W30h"));
            File.WriteAllText(invalidPath, Json("99", "W30h"));

            Assert.Throws<InvalidDataException>(() => HandStatsProfile.LoadFromFile(duplicatePath));
            Assert.Throws<InvalidDataException>(() => HandStatsProfile.LoadFromFile(invalidPath));
        }
        finally
        {
            File.Delete(duplicatePath);
            File.Delete(invalidPath);
        }
    }

    private static DataCatalog LoadCatalog()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        return catalog;
    }

    private static HandStatsProfile Profile(params HandStatsUnit[] units) =>
        HandStatsProfile.CreateForTests("test helper", units,
            new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero));

    private static HandStatsUnit Unit(string rawcode, IDictionary<string, HandStatsAbility> abilities,
        IReadOnlyList<string>? notes = null, IDictionary<string, string>? groups = null) => new(
            rawcode, "테스트 유닛", "특수", "원문 유닛", "특수", "원문",
            abilities.ToImmutableDictionary(StringComparer.Ordinal), notes ?? [], [])
        {
            NonStackingGroups = (groups ?? new Dictionary<string, string>()).ToImmutableDictionary(StringComparer.Ordinal)
        };

    private static string Json(string sourceGuideId, params string[] rawcodes) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        sourceGuideId = int.Parse(sourceGuideId),
        sourceUrl = "https://tmo.gg/g/ord/build-helper/48784",
        sourceTitle = "test",
        capturedAt = "2026-09-13T00:00:00Z",
        units = rawcodes.Select(rawcode => new
        {
            rawcode,
            name = "n",
            tier = "t",
            sourceName = "n",
            sourceTier = "t",
            sourceText = "x",
            abilities = new Dictionary<string, object>(),
            notes = Array.Empty<string>()
        }).ToArray()
    });
}
