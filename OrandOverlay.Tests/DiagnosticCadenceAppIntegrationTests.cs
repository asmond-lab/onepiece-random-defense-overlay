using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticCadenceAppIntegrationTests
{
    [Fact]
    public async Task ActualWpfPresentsIndependentBasicBeforeFullDeadlineAndFencesOldWork()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OrandOverlay.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var tool = Path.Combine(root!.FullName, "Tools", "ActivityLogCapture", "bin", configuration,
            "net8.0-windows", "RandypickBetaCapture.exe");
        Assert.True(File.Exists(tool));
        var temporary = Directory.CreateTempSubdirectory("orand-basic-cadence-").FullName;
        var output = Path.Combine(temporary, "capture");
        try
        {
            var start = new ProcessStartInfo(tool)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            start.ArgumentList.Add(output); start.ArgumentList.Add("--basic-cadence");
            using var process = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            Assert.True(process.ExitCode == 0, $"Exit={process.ExitCode}\n{await stdout}\n{await stderr}");
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "basic-cadence.json")));
            var proof = report.RootElement;
            Assert.True(proof.GetProperty("success").GetBoolean());
            Assert.True(proof.GetProperty("basicBeforeDeadline").GetBoolean());
            Assert.Equal(2, proof.GetProperty("fullCallsAtDeadline").GetInt32());
            Assert.Equal(process.Id, proof.GetProperty("processId").GetInt32());
            Assert.Equal(0, proof.GetProperty("remainingWindows").GetInt32());
            Assert.True(proof.GetProperty("cleanup").GetProperty("ownedWindowsClosed").GetBoolean());
            Assert.True(File.Exists(Path.Combine(output, "basic-before-full-deadline.png")));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
