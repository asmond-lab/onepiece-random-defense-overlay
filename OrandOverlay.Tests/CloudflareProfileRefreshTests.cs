using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CloudflareProfileRefreshTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orand-cf-refresh-" + Guid.NewGuid().ToString("N"));
    private static byte[] ProfileBytes => MemoryProfileRefreshFixture.ReadVerifiedLegacyBytes();

    [Fact]
    public async Task ForceRefreshBypassesCooldownButNotSignatureOrRollbackChecks()
    {
        using var key = new TestSigningKey(); var bytes = ProfileBytes;
        var first = key.Sign(TestSigningKey.Payload("memory-profiles", bytes, "1.0.1"));
        var calls = 0;
        Task<string> Fetch(CancellationToken _) { calls++; return Task.FromResult(first); }
        await MemoryProfileRefreshService.TryRefreshAsync(_root, fetch: Fetch, fetchAsset: (_, _) => Task.FromResult(bytes), trust: key.Trust);
        Assert.Equal(1, calls);
        await MemoryProfileRefreshService.TryRefreshAsync(_root, fetch: Fetch, fetchAsset: (_, _) => Task.FromResult(bytes), trust: key.Trust);
        Assert.Equal(1, calls);
        var older = key.Sign(TestSigningKey.Payload("memory-profiles", bytes, "1.0.0"));
        var assetCalls = 0;
        await MemoryProfileRefreshService.TryRefreshAsync(_root, fetch: _ => Task.FromResult(older),
            fetchAsset: (_, _) => { assetCalls++; return Task.FromResult(bytes); }, trust: key.Trust, force: true);
        Assert.Equal(0, assetCalls);
        Assert.Equal(first, File.ReadAllText(Path.Combine(_root, "memory-profiles.signed.json")));
    }

    [Fact]
    public async Task SameVersionCannotSubstituteDifferentProfileBytes()
    {
        using var key = new TestSigningKey(); var bytes = ProfileBytes;
        var first = key.Sign(TestSigningKey.Payload("memory-profiles", bytes, "1.0.1"));
        await MemoryProfileRefreshService.TryRefreshAsync(_root, fetch: _ => Task.FromResult(first), fetchAsset: (_, _) => Task.FromResult(bytes), trust: key.Trust);
        var changed = bytes.Concat(Encoding.UTF8.GetBytes(" ")).ToArray();
        var conflict = key.Sign(TestSigningKey.Payload("memory-profiles", changed, "1.0.1"));
        await MemoryProfileRefreshService.TryRefreshAsync(_root, fetch: _ => Task.FromResult(conflict), fetchAsset: (_, _) => Task.FromResult(changed), trust: key.Trust, force: true);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_root, "memory-profiles.json")));
        Assert.Equal(first, File.ReadAllText(Path.Combine(_root, "memory-profiles.signed.json")));
    }

    [Fact]
    public async Task LegacyProviderStampCannotSuppressFirstCloudflareRefresh()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "memory-profiles.last-check"), DateTimeOffset.UtcNow.ToString("O"));
        using var key = new TestSigningKey(); var bytes = ProfileBytes;
        var signed = key.Sign(TestSigningKey.Payload("memory-profiles", bytes));
        await MemoryProfileRefreshService.TryRefreshAsync(_root, fetch: _ => Task.FromResult(signed), fetchAsset: (_, _) => Task.FromResult(bytes), trust: key.Trust);
        Assert.True(File.Exists(Path.Combine(_root, "memory-profiles.cloudflare.last-check")));
    }

    [Fact]
    public async Task SignedBundleWithDisabledUnverifiedReferenceCannotChangeCacheOrStamp()
    {
        using var key = new TestSigningKey(); var bytes = ProfileBytes;
        var first = key.Sign(TestSigningKey.Payload("memory-profiles", bytes, "1.0.1"));
        await MemoryProfileRefreshService.TryRefreshAsync(_root, fetch: _ => Task.FromResult(first),
            fetchAsset: (_, _) => Task.FromResult(bytes), trust: key.Trust);
        var stampPath = Path.Combine(_root, "memory-profiles.cloudflare.last-check");
        var stamp = File.ReadAllBytes(stampPath);
        var bundled = MemoryProfileRefreshFixture.ReadBundledBytes();
        using var document = System.Text.Json.JsonDocument.Parse(bundled);
        var reference = Assert.Single(document.RootElement.EnumerateArray(),
            profile => profile.GetProperty("profileId").GetString() == "war3-3.0.0.24268-reference");
        Assert.False(reference.GetProperty("enabled").GetBoolean());
        Assert.False(reference.GetProperty("verified").GetBoolean());
        var signed = key.Sign(TestSigningKey.Payload("memory-profiles", bundled, "1.0.2"));
        var assetCalls = 0;
        await MemoryProfileRefreshService.TryRefreshAsync(_root, fetch: _ => Task.FromResult(signed),
            fetchAsset: (_, _) => { assetCalls++; return Task.FromResult(bundled); }, trust: key.Trust, force: true);
        Assert.Equal(1, assetCalls);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_root, "memory-profiles.json")));
        Assert.Equal(first, File.ReadAllText(Path.Combine(_root, "memory-profiles.signed.json")));
        Assert.Equal(stamp, File.ReadAllBytes(stampPath));
        Assert.False(File.Exists(Path.Combine(_root, "memory-profiles.json.tmp")));
        Assert.False(File.Exists(Path.Combine(_root, "memory-profiles.signed.json.tmp")));
    }

    [Fact]
    public void SignedTestVersionCannotCrossIntoStableChannel()
    {
        using var key = new TestSigningKey();
        var payload = TestSigningKey.Payload(version: "0.6.70-test.20260913.3"); payload["channel"] = "test";
        var signed = key.Sign(payload);
        Assert.Equal("0.6.70-test.20260913.3", SignedUpdateManifest.Verify(signed, "application", "test", key.Trust).Version);
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(signed, "application", "stable", key.Trust));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
