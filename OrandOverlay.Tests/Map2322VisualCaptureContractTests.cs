using Map2322VisualCapture;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322VisualCaptureContractTests
{
    [Fact]
    public void FixtureContractProjectsAllSelectedSourceChoicesWithoutRuntimeEffects()
    {
        var checks = CaptureContract.Inspect();
        Assert.Equal("2.322", checks.FixtureMapVersion);
        Assert.Equal(15, checks.Navigation.Length);
        Assert.Equal(15, checks.Navigation.Select(x => x.Id).Distinct().Count());
        Assert.All(checks.Navigation, x => { Assert.True(x.ResolvesInSelectedCatalog); Assert.True(x.ManualOnly); });
        Assert.Equal(14, checks.Story.Length);
        Assert.Equal(Enumerable.Range(1, 14), checks.Story.Select(x => x.Index));
        Assert.Equal(14, checks.Story.Select(x => x.ObjectiveRawcode).Distinct().Count());
        Assert.All(checks.Story, x => { Assert.Equal(5, x.OwnerId); Assert.True(x.SourceMatches); Assert.True(x.BaseOnly); });
        Assert.True(checks.Legacy321StillLoads);
        var screenshots = CaptureContract.ScreenshotScenarios(checks);
        Assert.Equal(59, screenshots.Length);
        Assert.Equal(59, screenshots.Distinct().Count());
        Assert.Contains("default-yujiro-empty-top.png", screenshots);
        Assert.Contains("default-yujiro-empty-bottom.png", screenshots);
        Assert.Equal(1, CaptureContract.FixtureInventory()["ivankov_hidden"]);
        Assert.Equal(1, CaptureContract.FixtureInventory()["rawcode:T60h"]);
        Assert.Equal(3, CaptureContract.FixtureInventory()["rawcode:I10h"]);
        Assert.Contains("default-reference-inventory.png", screenshots);
        Assert.Contains("default-reference-stats.png", screenshots);
        Assert.All(new[] { "basic", "full", "wrong-fingerprint", "missing-archive", "expired" }, state =>
            Assert.Contains("default-reference-" + state + ".png", screenshots));
        Assert.All(new[] { "default-craft-bottom.png", "default-candidate-scrolled.png",
            "default-historical-2320.png", "default-small-craft-resized.png", "default-large-craft-resized.png",
            "default-small-unit-resized.png", "default-large-unit-resized.png",
            "default-consent-top.png", "default-consent-bottom.png", "default-consent-decline.png",
            "default-historical-2320-expanded.png", "default-historical-2320-bottom.png",
            "default-yujiro-owned-top.png", "default-yujiro-owned-bottom.png", "default-update-deferred.png", "default-update-ready.png",
            "default-update-busy.png" }, file => Assert.Contains(file, screenshots));
        Assert.Equal(15, screenshots.Count(x => x.StartsWith("default-navigation-", StringComparison.Ordinal)));
        Assert.All(new[] { "small", "default", "large" }, size =>
            Assert.Contains(size + "-independent-craft.png", screenshots));
    }

    [Fact]
    public void CaptureReceiptRequiresActualCaptionOnBothSidesOfTheNativePaint()
    {
        const string expected = "게임 입장 대기 중";
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        Assert.True(CaptureContract.MatchesCapture(expected, expected, expected, expected, hash, 42, 42, 10, 11));
        Assert.False(CaptureContract.MatchesCapture(expected, expected, "최근 확인 40라운드", expected, hash, 42, 42, 10, 11));
        Assert.False(CaptureContract.MatchesCapture(expected, expected, expected, "최근 확인 40라운드", hash, 42, 42, 10, 11));
        Assert.False(CaptureContract.MatchesCapture(expected, "유닛 확인 중", expected, expected, hash, 42, 42, 10, 11));
        Assert.False(CaptureContract.MatchesCapture(expected, expected, expected, expected, hash, 42, 43, 10, 11));
        Assert.False(CaptureContract.MatchesCapture(expected, expected, expected, expected, hash, 42, 42, 12, 11));
        Assert.False(CaptureContract.DistinctSelectorFrames("일석이조", "긴급소집", hash, hash));
        Assert.True(CaptureContract.DistinctSelectorFrames("일석이조", "긴급소집", hash, new string('a', 64)));
    }

    [Fact]
    public void CaptureAcceptanceRejectsInvisibleOrUninvokedYujiroAndStaleManualSummaries()
    {
        var unknown = new[] { "KING:h0C2", "PICK:A800" };
        Assert.True(CaptureContract.MatchesYujiroInvoke("normal-candidate-rawcode:2C0h", true, true, "rawcode:2C0h", unknown));
        Assert.False(CaptureContract.MatchesYujiroInvoke("normal-candidate-rawcode:2C0h", false, true, "rawcode:2C0h", unknown));
        Assert.False(CaptureContract.MatchesYujiroInvoke("normal-candidate-rawcode:2C0h", true, false, "rawcode:2C0h", unknown));
        Assert.False(CaptureContract.MatchesYujiroInvoke("wrong", true, true, "rawcode:2C0h", unknown));
        Assert.False(CaptureContract.MatchesYujiroInvoke("normal-candidate-rawcode:2C0h", true, true, null, unknown));
        Assert.False(CaptureContract.MatchesYujiroInvoke("normal-candidate-rawcode:2C0h", true, true, "rawcode:2C0h", ["KING:h0C2"]));
        const string summary = "수동 계획: 긴급소집 · 2.322 원문 기준";
        Assert.True(CaptureContract.MatchesNavigationReceipt("긴급소집", summary, "긴급소집", "긴급소집", summary, summary));
        Assert.False(CaptureContract.MatchesNavigationReceipt("긴급소집", summary, "긴급소집", "긴급소집", "수동 계획: 일석이조 · 2.322 원문 기준", summary));
        Assert.False(CaptureContract.MatchesNavigationReceipt("긴급소집", summary, "긴급소집", "긴급소집", summary, "수동 계획: 일석이조 · 2.322 원문 기준"));
        Assert.False(CaptureContract.MatchesNavigationReceipt("긴급소집", summary, "일석이조", "긴급소집", summary, summary));
        var six = new long[] { 1, 2, 3, 4, 5, 6 };
        Assert.True(CaptureContract.CoversScreenshotOwnedHandles(six, six));
        Assert.False(CaptureContract.CoversScreenshotOwnedHandles(six, six.Take(5)));
        Assert.False(CaptureContract.CoversScreenshotOwnedHandles([0], six));
    }

    [Fact]
    public void RecipeMovementAndReferenceGatesRemainDistinctAndMachineReadable()
    {
        var checks = CaptureContract.Inspect();
        Assert.Equal("2C0h", checks.Yujiro.AppRawcode);
        Assert.Equal("h0C2", checks.Yujiro.SourceRawcode);
        Assert.Equal(new[] { "T60h", "Y20h", "Y30h", "G60h" }, checks.Yujiro.FixedUnits);
        Assert.Equal(10000, checks.Yujiro.Gold);
        Assert.Equal(7, checks.Yujiro.Wood);
        Assert.Equal(new[] { "KING:h0C2", "PICK:A800" }, checks.Yujiro.UnresolvedConditions);
        Assert.False(checks.Yujiro.FixedOnlyReady);
        Assert.False(checks.Yujiro.DistinctPickReady);
        Assert.False(checks.Yujiro.CombineCommandAvailable);
        Assert.Equal("DA0h", checks.Kaido.Dragon);
        Assert.Equal("WB0h", checks.Kaido.Hybrid);
        Assert.True(checks.Kaido.DragonAir);
        Assert.False(checks.Kaido.HybridAir);
        Assert.False(checks.Kaido.UnknownAir);
        Assert.True(checks.References.BasicAccepted);
        Assert.True(checks.References.FullAccepted);
        Assert.True(checks.References.WrongFingerprintDenied);
        Assert.True(checks.References.WrongVersionDenied);
        Assert.True(checks.References.ExpiredDenied);
        Assert.True(checks.References.MissingArchiveDenied);
    }
}
