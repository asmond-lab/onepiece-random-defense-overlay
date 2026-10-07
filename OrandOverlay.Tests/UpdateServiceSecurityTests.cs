using System.Text;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class UpdateServiceSecurityTests
{
    private const string Digest = "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public async Task Check_UsesSignedStableManifestAndRechecksOriginalEnvelope()
    {
        using var key = new TestSigningKey(); var envelope = key.Sign(TestSigningKey.Payload());
        var service = new UpdateService(url => { Assert.Equal(SignedUpdateManifest.ApplicationManifestUrl, url); return Task.FromResult(envelope); }, key.Trust, currentIdentity: "1.0.0");
        var result = await service.CheckDetailedAsync();
        Assert.False(result.Failed); Assert.NotNull(result.Update);
        var update = result.Update!;
        Assert.Equal(Digest, update.Sha256); Assert.Equal(3, update.AssetSize);
        Assert.True(UpdateService.IsTrustedUpdate(update, key.Trust, "1.0.0"));
        Assert.False(UpdateService.IsTrustedUpdate(update with { DownloadUrl = "https://evil.example/OrandOverlay.exe" }, key.Trust, "1.0.0"));
        Assert.False(UpdateService.IsTrustedUpdate(update with { AssetSize = 4 }, key.Trust, "1.0.0"));
        Assert.False(UpdateService.IsTrustedUpdate(update with { Sha256 = "sha256:" + new string('0', 64) }, key.Trust, "1.0.0"));
        Assert.False(UpdateService.IsTrustedUpdate(update with { Latest = new Version(99, 0) }, key.Trust, "1.0.0"));
        Assert.False(UpdateService.IsTrustedUpdate(update with { Tag = "99.0" }, key.Trust, "1.0.0"));
        Assert.False(UpdateService.IsTrustedUpdate(update with { SignedEnvelope = "{}" }, key.Trust, "1.0.0"));
        using var other = new TestSigningKey();
        Assert.False(UpdateService.IsTrustedUpdate(update, other.Trust, "1.0.0"));
    }

    [Fact]
    public async Task Check_ValidEqualOrOlderVersionIsNotFailure()
    {
        using var key = new TestSigningKey();
        foreach (var version in new[] { "1.0.0", "0.0" })
        {
            var envelope = key.Sign(TestSigningKey.Payload(version: version));
            var result = await new UpdateService(_ => Task.FromResult(envelope), key.Trust, currentIdentity: "1.0.0").CheckDetailedAsync();
            Assert.Null(result.Update); Assert.False(result.Failed);
        }
    }

    [Fact]
    public async Task Check_UnsignedOrWrongChannelIsFailureWithoutRedirectFallback()
    {
        using var key = new TestSigningKey();
        var payload = TestSigningKey.Payload(); payload["channel"] = "test";
        foreach (var envelope in new[] { "{}", TestSigningKey.Payload().ToJsonString(), key.Sign(payload) })
        {
            var result = await new UpdateService(_ => Task.FromResult(envelope), key.Trust, currentIdentity: "1.0.0").CheckDetailedAsync();
            Assert.Null(result.Update); Assert.True(result.Failed);
        }
        var redirects = 0;
        var production = new UpdateService(_ => throw new IOException(), _ => { redirects++; return Task.FromResult<string?>(null); });
        Assert.True((await production.CheckDetailedAsync()).Failed); Assert.Equal(0, redirects);
    }

    [Fact]
    public async Task Installer_RejectsForgedSourceCompatibleUpdateInfoBeforeAnyDownload()
    {
        var update = new UpdateInfo(new Version(9, 9, 9), "9.9.9", SignedUpdateManifest.Origin + "/downloads/9.9.9/OrandOverlay.exe", 3, Digest);
        Assert.False(UpdateService.IsTrustedUpdate(update));
        Assert.False(UpdateService.IsTrustedUpdate(new UpdateInfo(new Version(9, 9), "9.9", update.DownloadUrl)));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateService().DownloadAndInstallAsync(update));
        Assert.Null(UpdateService.ParseLatest("{}", new Version(1, 0)));
        Assert.Null(UpdateService.ParseRedirectLocation(update.DownloadUrl, new Version(1, 0)));
    }

    [Theory]
    [InlineData("abc", 3, true)] [InlineData("abd", 3, false)]
    [InlineData("ab", 3, false)] [InlineData("abcd", 3, false)]
    [InlineData("abc", 0, false)] [InlineData("abc", 536870913, false)]
    public async Task VerifyDownloadedBody_RequiresExactBoundedLengthAndHash(string body, long expectedSize, bool valid)
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(body));
        await using var target = new MemoryStream();
        if (valid) { await UpdateService.VerifyDownloadedBodyAsync(source, target, expectedSize, Digest); Assert.Equal(body, Encoding.UTF8.GetString(target.ToArray())); }
        else await Assert.ThrowsAsync<InvalidDataException>(() => UpdateService.VerifyDownloadedBodyAsync(source, target, expectedSize, Digest));
    }

    [Fact]
    public void InstallScript_UsesBoundedWaitAtomicReplaceAndImmediateStartRollback()
    {
        var script = UpdateService.BuildInstallScript(@"C:\app\O'Rand.exe", @"C:\app\O'Rand.exe.new", @"C:\app\update.log", "9.9.9", 42, 3, Digest, "1.0.0");
        Assert.Contains("-lt 120", script); Assert.Contains("[System.IO.File]::Replace", script);
        Assert.Contains(".previous", script); Assert.Contains("rolled back after start failure", script);
        Assert.DoesNotContain(@"Remove-Item -LiteralPath 'C:\app\O''Rand.exe'", script);
        Assert.DoesNotContain("Move-Item -LiteralPath", script);
    }

    [Fact]
    public async Task ProfileRefresh_RejectsTamperedBytesAndKeepsCacheWithoutStamp()
    {
        using var key = new TestSigningKey();
        var root = Path.Combine(Path.GetTempPath(), "orand-profile-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "memory-profiles.json"); File.WriteAllText(path, "prior-cache");
            var bytes = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "memory-profiles.json")));
            var envelope = key.Sign(TestSigningKey.Payload("memory-profiles", bytes));
            bytes[0] ^= 1;
            await MemoryProfileRefreshService.TryRefreshAsync(root, fetch: _ => Task.FromResult(envelope), fetchAsset: (_, _) => Task.FromResult(bytes), trust: key.Trust);
            Assert.Equal("prior-cache", File.ReadAllText(path));
            Assert.False(File.Exists(Path.Combine(root, "memory-profiles.cloudflare.last-check")));
            Assert.False(File.Exists(path + ".tmp"));
            var assetCalls = 0;
            await MemoryProfileRefreshService.TryRefreshAsync(root, fetch: _ => Task.FromResult("[]"), fetchAsset: (_, _) => { assetCalls++; return Task.FromResult(bytes); }, trust: key.Trust);
            Assert.Equal(0, assetCalls); Assert.Equal("prior-cache", File.ReadAllText(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
