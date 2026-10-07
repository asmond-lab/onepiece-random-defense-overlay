using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class OverlayResizeFixtureTests
{
    [Fact]
    public async Task UnitCheckOverlaySupportsNativeResizeAndRetainsSessionSize()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OrandOverlay.csproj")))
            root = root.Parent;
        Assert.NotNull(root);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var fixture = Path.Combine(root!.FullName, "Tools", "OverlayResizeCapture", "bin",
            configuration, "net8.0-windows", "OverlayResizeCapture.exe");
        Assert.True(File.Exists(fixture), $"Resize fixture is unavailable: {fixture}");
        var output = Path.Combine(Path.GetTempPath(), "randypick-overlay-resize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo(fixture, $"\"{output}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(process);
            await process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var reportPath = Path.Combine(output, "report.json");
            Assert.True(File.Exists(reportPath), File.Exists(Path.Combine(output, "error.txt"))
                ? await File.ReadAllTextAsync(Path.Combine(output, "error.txt"))
                : "Resize fixture did not emit a report.");
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            Assert.True(report.RootElement.GetProperty("success").GetBoolean(),
                await File.ReadAllTextAsync(reportPath));
            Assert.Equal("2.321", report.RootElement.GetProperty("catalogVersion").GetString());
            Assert.False(report.RootElement.GetProperty("physicalMouseDragPerformed").GetBoolean());
            Assert.Equal(0, process.ExitCode);
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
            Directory.Delete(output, recursive: true);
        }
    }
}
