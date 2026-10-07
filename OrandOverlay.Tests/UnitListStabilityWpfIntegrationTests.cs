using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class UnitListStabilityWpfIntegrationTests
{
    [Fact]
    public async Task RapidBasicFullReplayKeepsStableCardsAndReflectsRealChanges()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OrandOverlay.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var tool = Path.Combine(root!.FullName, "Tools", "ActivityLogCapture", "bin", configuration,
            "net8.0-windows", "RandypickBetaCapture.exe");
        Assert.True(File.Exists(tool), $"Activity replay tool is unavailable: {tool}");
        var temporary = Directory.CreateTempSubdirectory("orand-unit-list-stability-").FullName;
        var output = Path.Combine(temporary, "capture");
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(tool)
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };
            start.ArgumentList.Add(output);
            start.ArgumentList.Add("--unit-list-stability");
            process = Process.Start(start);
            Assert.NotNull(process);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var stdout = process!.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            var standardOutput = await stdout;
            var standardError = await stderr;
            var reportPath = Path.Combine(output, "unit-list-stability.json");
            Assert.True(File.Exists(reportPath), $"Replay report missing.\n{standardError}\n{standardOutput}");
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            var proof = report.RootElement;
            Assert.True(process.ExitCode == 0,
                $"Replay exit {process.ExitCode}.\n{standardError}\n{standardOutput}\n{await File.ReadAllTextAsync(reportPath)}");
            Assert.True(proof.GetProperty("success").GetBoolean());
            Assert.True(proof.GetProperty("synthetic").GetBoolean());
            Assert.False(proof.GetProperty("actualGameData").GetBoolean());
            Assert.Equal(process.Id, proof.GetProperty("processId").GetInt32());
            Assert.Equal(0, proof.GetProperty("remainingWindows").GetInt32());
            Assert.Empty(proof.GetProperty("failures").EnumerateArray());
            var frames = proof.GetProperty("frames").EnumerateArray().ToArray();
            Assert.Equal(23, frames.Length);
            Assert.All(frames, frame =>
            {
                Assert.Equal("랜디픽 · 유닛 확인", frame.GetProperty("goal").GetString());
                Assert.NotEqual(0, frame.GetProperty("hwnd").GetInt64());
                Assert.True(frame.GetProperty("imageWritten").GetBoolean());
                Assert.False(frame.GetProperty("gameplayReady").GetBoolean());
                Assert.Equal(0, frame.GetProperty("automaticCount").GetInt32());
                Assert.True(File.Exists(Path.Combine(output, frame.GetProperty("image").GetString()!)));
            });
            var source = proof.GetProperty("sourceEvidence");
            Assert.Equal(0, source.GetProperty("viewSlot").GetInt32());
            Assert.Equal(17, source.GetProperty("raw61841_61846_61861").EnumerateObject().Count());
            Assert.True(proof.GetProperty("cleanup").GetProperty("ownedWindowsClosed").GetBoolean());
            Assert.False(proof.GetProperty("cleanup").GetProperty("nativeReaderCreated").GetBoolean());
            Assert.False(proof.GetProperty("cleanup").GetProperty("userInputSent").GetBoolean());
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                }
                finally { process.Dispose(); }
            }
            Directory.Delete(temporary, recursive: true);
        }
    }
}
