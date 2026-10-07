using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class UpdateChannelVersionTests
{
    [Theory]
    [InlineData("1.0.0-test.1", "1.0.0-test.2")]
    [InlineData("1.0.0-test.2", "1.0.0-test.10")]
    [InlineData("1.0.0-test.20260914.9", "1.0.0-test.20260914.10")]
    [InlineData("1.0.0-test.999999999999999999999", "1.0.0-test.1000000000000000000000")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.2-test.8", "1.0.3-test.1")]
    [InlineData("1.0.2-test.14", "1.0.3-test.1")]
    [InlineData("1.0.2-test.14.recognition.3", "1.0.3-test.1")]
    [InlineData("1.0.3-test.1", "1.0.4-test.1")]
    public async Task OffersAreOrderedAndReverifiedOffline(string current, string next)
    {
        using var key = new TestSigningKey();
        Assert.True(UpdateChannelVersion.TryParse(current, out var c));
        Assert.True(UpdateChannelVersion.TryParse(next, out var n));
        Assert.True(n.IsUpgradeFrom(c)); Assert.False(c.IsUpgradeFrom(n)); Assert.False(c.IsUpgradeFrom(c));
        var payload = TestSigningKey.Payload(version: next); payload["channel"] = c.Channel;
        var envelope = key.Sign(payload);
        var service = new UpdateService(url => { Assert.Equal(c.ManifestUrl, url); return Task.FromResult(envelope); }, key.Trust, currentIdentity: current);
        var result = await service.CheckDetailedAsync();
        Assert.False(result.Failed); Assert.NotNull(result.Update); Assert.True(service.ReverifyOffer(result.Update!));
        Assert.False(service.ReverifyOffer(result.Update! with { Tag = current }));
        Assert.False(service.ReverifyOffer(result.Update! with { AssetSize = 4 }));
        Assert.False(service.ReverifyOffer(result.Update! with { Sha256 = "sha256:" + new string('0', 64) }));
        Assert.False(service.ReverifyOffer(result.Update! with { SignedEnvelope = "{}" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndInstallAsync(result.Update! with { AssetSize = 4 }));
        var reverse = TestSigningKey.Payload(version: current); reverse["channel"] = c.Channel;
        var lower = await new UpdateService(_ => Task.FromResult(key.Sign(reverse)), key.Trust, currentIdentity: next).CheckDetailedAsync();
        Assert.Null(lower.Update); Assert.False(lower.Failed);
        var equal = await new UpdateService(_ => Task.FromResult(envelope), key.Trust, currentIdentity: next).CheckDetailedAsync();
        Assert.Null(equal.Update); Assert.False(equal.Failed);
    }

    [Theory]
    [InlineData("1.0.0-test.01")] [InlineData("1.0.0-test.")] [InlineData("1.0.0-test.1..2")]
    [InlineData("1.0.0-test.1/2")] [InlineData("1.0.0-test.1+git")] [InlineData("1.0.0-rc.1")]
    [InlineData("01.0.0")] [InlineData("1.0.0-TEST.1")] [InlineData("")]
    public async Task MalformedLocalIdentityFailsBeforeFetch(string identity)
    {
        Assert.False(UpdateChannelVersion.TryParse(identity, out _));
        using var key = new TestSigningKey(); var calls = 0;
        var result = await new UpdateService(_ => { calls++; return Task.FromResult("{}"); }, key.Trust, currentIdentity: identity).CheckDetailedAsync();
        Assert.True(result.Failed); Assert.Null(result.Update); Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("1.0.0", "test", "2.0.0-test.1")]
    [InlineData("1.0.0-test.1", "stable", "2.0.0")]
    public async Task SignedRemoteChannelCannotChangeLocalPolicy(string identity, string channel, string version)
    {
        using var key = new TestSigningKey(); var payload = TestSigningKey.Payload(version: version); payload["channel"] = channel;
        var result = await new UpdateService(_ => Task.FromResult(key.Sign(payload)), key.Trust, currentIdentity: identity).CheckDetailedAsync();
        Assert.True(result.Failed); Assert.Null(result.Update);
    }

    [Fact]
    public void OversizeAndLeadingZeroNumericSegmentsAreRejected()
    {
        Assert.False(UpdateChannelVersion.TryParse("1.0.0-test." + new string('1', 65), out _));
        Assert.True(UpdateChannelVersion.TryParse("1.0.0-test." + new string('1', 53), out _));
        Assert.False(UpdateChannelVersion.TryParse("1.0.0-test.20260914.01", out _));
    }

    [Fact]
    public void ScriptVerifiesStagedBytesAfterWaitBeforeReplaceOrLaunch()
    {
        var digest = "sha256:" + new string('a', 64);
        var script = UpdateService.BuildInstallScript(@"C:\test\app.exe", @"C:\test\app.exe.new", @"C:\test\update.log", "1.0.0-test.10", 42, 3, digest, "1.0.0-test.2");
        Assert.True(script.IndexOf("wait timeout", StringComparison.Ordinal) < script.IndexOf("$staged.Length", StringComparison.Ordinal));
        Assert.True(script.IndexOf("ComputeHash", StringComparison.Ordinal) < script.IndexOf("[System.IO.File]::Replace", StringComparison.Ordinal));
        Assert.True(script.IndexOf("staged verification failed", StringComparison.Ordinal) < script.IndexOf("Start-Process", StringComparison.Ordinal));
        Assert.Contains("$staged.Length -ne 3", script); Assert.Contains(new string('a', 64), script);
        Assert.Contains("rolled back after start failure", script);
        Assert.Throws<ArgumentException>(() => UpdateService.BuildInstallScript("a", "b", "c", "1.0.0", 42));
        Assert.Throws<ArgumentException>(() => UpdateService.BuildInstallScript("a", "b", "c", "1.0.0-test.1", 42, 3, digest, "1.0.0-test.2"));
        Assert.Throws<ArgumentException>(() => UpdateService.BuildInstallScript("a", "b", "c", "2.0.0", 42, 3, digest, "1.0.0-test.2"));
    }
    [Theory]
    [InlineData("abd")] [InlineData("ab")]
    public async Task TamperedStageExitsWithoutReplacingOrLaunching(string staged)
    {
        var directory = Path.Combine(Path.GetTempPath(), "orand-stage-tamper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var exe = Path.Combine(directory, "never-launch.exe"); var log = Path.Combine(directory, "update.log");
            await File.WriteAllTextAsync(exe, "original sentinel"); await File.WriteAllTextAsync(exe + ".new", staged);
            var script = UpdateService.BuildInstallScript(exe, exe + ".new", log, "1.0.0-test.2", int.MaxValue, 3,
                "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "1.0.0-test.1");
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe", UseShellExecute = false, CreateNoWindow = true,
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))
            })!;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(1, process.ExitCode); Assert.Equal("original sentinel", await File.ReadAllTextAsync(exe));
            Assert.False(File.Exists(exe + ".previous"));
            Assert.Contains("staged verification failed", await File.ReadAllTextAsync(log));
            Assert.DoesNotContain("started", await File.ReadAllTextAsync(log));
        }
        finally { Directory.Delete(directory, true); }
    }

}
