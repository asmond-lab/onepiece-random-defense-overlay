using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GrowthUnitPointerTrackerTests
{
    private const ulong JinbePointer = 0x1234;
    private static readonly IReadOnlyDictionary<ulong, uint> NoGrowth =
        new Dictionary<ulong, uint>();
    private static readonly IReadOnlySet<ulong> NoPointers = new HashSet<ulong>();
    private static readonly uint Jinbe = RawcodeCodec.TryParse("810h", out var code)
        ? code
        : throw new InvalidOperationException("810h rawcode 변환 실패");
    private static readonly uint GuardPoint = RawcodeCodec.TryParse("D10h", out var guard)
        ? guard
        : throw new InvalidOperationException("D10h rawcode 변환 실패");
    private static readonly uint Buggy = RawcodeCodec.TryParse("510h", out var buggy)
        ? buggy
        : throw new InvalidOperationException("510h rawcode 변환 실패");

    [Theory]
    [InlineData(24, 0, 0, 24, true)]
    [InlineData(24, 1, 0, 24, false)]
    [InlineData(0, 0, 0, 24, false)]
    public void NeutralGrowthUsesPreservedPlayerColorForLocalAttribution(
        byte owner, byte playerColor, byte localSlot, byte neutralSlot, bool expected)
    {
        Assert.Equal(expected, GrowthUnitOwnershipPolicy.IsLocalNeutralGrowth(
            owner, playerColor, localSlot, neutralSlot));
    }

    [Fact]
    public void LocalColorBrainGrowthCompletesLawRareRecipe()
    {
        Assert.True(RawcodeCodec.TryParse("E10h", out var brain));
        Assert.True(GrowthUnitOwnershipPolicy.IsLocalNeutralGrowth(
            owner: 24, playerColor: 0, localPlayerSlot: 0, neutralPlayerSlot: 24));
        var catalog = new DataCatalog();
        catalog.Load();
        var progress = new RecipeCompletionCalculator(catalog.Unit).Calculate(
            ["rawcode:L20h"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rawcode:H10h"] = 1,
                ["rawcode:G10h"] = 1,
                [RawcodeCodec.DynamicUnitId(brain)] = 1
            });

        Assert.Equal(1, progress.CompletionRatio);
    }

    [Fact]
    public void GrowthDiagnosticsUseSameCanonicalIdsAsInventory()
    {
        Assert.True(RawcodeCodec.TryParse("300h", out var luffy));
        Assert.True(RawcodeCodec.TryParse("E10h", out var brain));
        var catalog = new DataCatalog();
        catalog.Load();

        var ids = WarcraftMemoryRecognitionService.MapGrowthUnitIds(
            new RawcodeUnitMap(catalog), [luffy, brain]);

        Assert.Contains("luffy_common", ids);
        Assert.Contains("rawcode:E10h", ids);
    }

    [Fact]
    public void LocallySeenJinbeRemainsAttributedAfterOwnerTransfer()
    {
        var tracker = new GrowthUnitPointerTracker();
        tracker.Commit(
            new Dictionary<ulong, uint> { [JinbePointer] = Jinbe },
            new HashSet<ulong> { JinbePointer });

        Assert.True(tracker.Matches(JinbePointer, Jinbe));

        tracker.Commit(NoGrowth, new HashSet<ulong> { JinbePointer });

        Assert.True(tracker.Matches(JinbePointer, Jinbe));
    }

    [Fact]
    public void UnseenForeignGrowthUnitIsNeverAttributed()
    {
        var tracker = new GrowthUnitPointerTracker();

        Assert.False(tracker.Matches(JinbePointer, Jinbe));
    }

    [Fact]
    public void SingleUntrackedNeutralGuardPointIsExcluded()
    {
        var counts = GrowthUnitOwnershipPolicy.InventoryCounts(
            new Dictionary<uint, int>(),
            new Dictionary<uint, int> { [GuardPoint] = 1 });

        Assert.Empty(counts);
    }

    [Fact]
    public void SingleUntrackedNeutralJinbeIsAttributedAsSoloGrowthUnit()
    {
        var counts = GrowthUnitOwnershipPolicy.InventoryCounts(
            new Dictionary<uint, int>(),
            new Dictionary<uint, int> { [Jinbe] = 1 });

        Assert.Equal(1, counts[Jinbe]);
    }

    [Fact]
    public void MultipleUntrackedNeutralGrowthUnitsAreNotAttributed()
    {
        var counts = GrowthUnitOwnershipPolicy.InventoryCounts(
            new Dictionary<uint, int>(),
            new Dictionary<uint, int>
            {
                [Jinbe] = 1,
                [GuardPoint] = 1
            });

        Assert.Empty(counts);
    }

    [Fact]
    public void TrackedJinbeRemainsWhileNeutralGuardPointIsExcluded()
    {
        var counts = GrowthUnitOwnershipPolicy.InventoryCounts(
            new Dictionary<uint, int> { [Jinbe] = 1 },
            new Dictionary<uint, int> { [GuardPoint] = 1 },
            hasAttributedGrowth: true);

        Assert.Equal(1, counts[Jinbe]);
        Assert.DoesNotContain(GuardPoint, counts.Keys);
    }

    [Fact]
    public void TrackedLocalGrowthDoesNotAdoptSingleForeignNeutralGrowth()
    {
        var counts = GrowthUnitOwnershipPolicy.InventoryCounts(
            new Dictionary<uint, int> { [Jinbe] = 1 },
            new Dictionary<uint, int> { [Buggy] = 1 },
            hasAttributedGrowth: true);

        Assert.Equal(1, counts[Jinbe]);
        Assert.DoesNotContain(Buggy, counts.Keys);
    }

    [Theory]
    [InlineData("810h")] // 징베
    [InlineData("510h")] // 버기 마기탄
    [InlineData("D10h")] // 쵸파 가드 포인트
    [InlineData("U00h")] // 루피 기어세컨드
    public void CatalogSpecialsCanUsePointerOwnershipTracking(string rawcode)
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var map = new RawcodeUnitMap(catalog);
        Assert.True(RawcodeCodec.TryParse(rawcode, out var code));

        Assert.True(map.IsGrowthUnit(code));
    }

    [Fact]
    public void DestroyedGrowthUnitExpiresAfterThreeSnapshots()
    {
        var tracker = new GrowthUnitPointerTracker();
        tracker.Commit(
            new Dictionary<ulong, uint> { [JinbePointer] = Jinbe },
            new HashSet<ulong> { JinbePointer });

        tracker.Commit(NoGrowth, NoPointers);
        tracker.Commit(NoGrowth, NoPointers);
        Assert.True(tracker.Matches(JinbePointer, Jinbe));

        tracker.Commit(NoGrowth, NoPointers);
        Assert.False(tracker.Matches(JinbePointer, Jinbe));
    }

    [Fact]
    public void RetainedGrowthJinbeSatisfiesRareRecipe()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var tracker = new GrowthUnitPointerTracker();
        tracker.Commit(
            new Dictionary<ulong, uint> { [JinbePointer] = Jinbe },
            new HashSet<ulong> { JinbePointer });
        tracker.Commit(NoGrowth, new HashSet<ulong> { JinbePointer });
        var inventory = tracker.Snapshot().Values
            .Select(RawcodeCodec.DynamicUnitId)
            .Concat(["rawcode:I10h", "rawcode:F10h"])
            .ToDictionary(unitId => unitId, _ => 1, StringComparer.OrdinalIgnoreCase);

        var progress = new RecipeCompletionCalculator(catalog.Unit)
            .Calculate(["rawcode:Y10h"], inventory);

        Assert.Equal(1, progress.CompletionRatio);
        Assert.DoesNotContain(progress.MissingLeaves, leaf => leaf.UnitId == "rawcode:810h");
    }

    [Fact]
    public void PointerCacheRestoresOnlyInsideSameWarcraftProcess()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orand-growth-cache-" + Guid.NewGuid());
        var path = Path.Combine(directory, "growth-unit-pointers.json");
        Directory.CreateDirectory(directory);
        try
        {
            GrowthUnitPointerCacheStore.Save(path, processStarted: 123,
                new Dictionary<ulong, uint> { [JinbePointer] = Jinbe });

            Assert.Equal(Jinbe, Assert.Single(
                GrowthUnitPointerCacheStore.Load(path, processStarted: 123)).Value);
            Assert.Empty(GrowthUnitPointerCacheStore.Load(path, processStarted: 124));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CorruptPointerCacheFailsClosed()
    {
        var path = Path.Combine(Path.GetTempPath(), "orand-growth-cache-" + Guid.NewGuid());
        try
        {
            File.WriteAllText(path, "{ broken");

            Assert.Empty(GrowthUnitPointerCacheStore.Load(path, processStarted: 123));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
