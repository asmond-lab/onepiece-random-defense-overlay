using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class UpdateAssetNameTests
{
    [Theory]
    [InlineData("RandyPick.exe", "test", "1.0.0-test.2", "1.0.0-test.1")]
    [InlineData("OrandOverlay.exe", "stable", "1.0.1", "1.0.0")]
    [InlineData("OrandOverlay.exe", "test", "1.0.0-test.2", "1.0.0-test.1")]
    [InlineData("RandyPick.exe", "stable", "1.0.1", "1.0.0")]
    public async Task ExactlyAllowlistedSignedNamesProduceBoundOffers(string name, string channel, string version, string current)
    {
        using var key = new TestSigningKey(); var payload = TestSigningKey.Payload(version: version);
        payload["channel"] = channel; payload["asset"]!["path"] = $"/downloads/{version}/{name}";
        var envelope = key.Sign(payload);
        var manifest = SignedUpdateManifest.Verify(envelope, "application", channel, key.Trust);
        Assert.Equal(name, manifest.AssetName);
        Assert.Equal(SignedUpdateManifest.Origin + $"/downloads/{version}/{name}", manifest.DownloadUrl);
        var service = new UpdateService(_ => Task.FromResult(envelope), key.Trust, currentIdentity: current);
        var result = await service.CheckDetailedAsync();
        Assert.False(result.Failed); Assert.NotNull(result.Update);
        var offer = result.Update!; Assert.Equal(name, offer.AssetName); Assert.True(service.ReverifyOffer(offer));
        var other = name == "RandyPick.exe" ? "OrandOverlay.exe" : "RandyPick.exe";
        Assert.False(service.ReverifyOffer(offer with { AssetName = other }));
        Assert.False(service.ReverifyOffer(offer with { DownloadUrl = SignedUpdateManifest.Origin + $"/downloads/{version}/{other}" }));
        Assert.False(service.ReverifyOffer(offer with { AssetName = other, DownloadUrl = SignedUpdateManifest.Origin + $"/downloads/{version}/{other}" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndInstallAsync(offer with { AssetName = other }));
    }

    [Theory]
    [InlineData("Other.exe")]
    [InlineData("randypick.exe")]
    [InlineData("RANDYPICK.EXE")]
    [InlineData("RandyPick.exe?x=1")]
    [InlineData("RandyPick.exe#fragment")]
    [InlineData("RandyPick.exe/OrandOverlay.exe")]
    [InlineData("../RandyPick.exe")]
    [InlineData("%2e%2e/RandyPick.exe")]
    [InlineData("%52andyPick.exe")]
    [InlineData("RandyPick.exe ")]
    public void SignedArbitraryOrNoncanonicalPathsAreRejected(string name)
    {
        using var key = new TestSigningKey(); var payload = TestSigningKey.Payload(version: "1.0.0-test.2");
        payload["channel"] = "test"; payload["asset"]!["path"] = $"/downloads/1.0.0-test.2/{name}";
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(key.Sign(payload), "application", "test", key.Trust));
    }

    [Fact]
    public void ExtraUnsignedStyleNameFieldCannotDisagreeWithSignedPath()
    {
        using var key = new TestSigningKey(); var payload = TestSigningKey.Payload();
        payload["asset"]!["assetName"] = "RandyPick.exe";
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(key.Sign(payload), "application", "stable", key.Trust));
        payload["asset"]!.AsObject().Remove("assetName"); payload["assetName"] = "RandyPick.exe";
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(key.Sign(payload), "application", "stable", key.Trust));
    }

    [Fact]
    public void RandyPickHostStageAndRollbackUseTheActualCurrentExePath()
    {
        var digest = "sha256:" + new string('a', 64);
        var script = UpdateService.BuildInstallScript(@"C:\app\RandyPick.exe", @"C:\app\RandyPick.exe.new", @"C:\app\update.log",
            "1.0.0-test.2", 42, 3, digest, "1.0.0-test.1", "RandyPick.exe");
        Assert.Contains("asset RandyPick.exe", script);
        Assert.Contains(@"[System.IO.File]::Open('C:\app\RandyPick.exe.new'", script);
        Assert.Contains(@"[System.IO.File]::Replace('C:\app\RandyPick.exe.new', 'C:\app\RandyPick.exe', 'C:\app\RandyPick.exe.previous'", script);
        Assert.Contains(@"Start-Process -FilePath 'C:\app\RandyPick.exe'", script);
        Assert.Contains("staged verification failed", script); Assert.Contains("rolled back after start failure", script);
        Assert.DoesNotContain("OrandOverlay.exe", script);
        Assert.Throws<ArgumentException>(() => UpdateService.BuildInstallScript("a", "b", "c", "1.0.1", 42, 3, digest, "1.0.0", "Other.exe"));
    }
}
