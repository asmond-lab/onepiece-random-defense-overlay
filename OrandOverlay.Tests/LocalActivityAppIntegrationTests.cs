using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class LocalActivityAppIntegrationTests
{
    private static List<string> Calls(string method) =>
        (List<string>)typeof(BulletAbilityWiringTests)
            .GetMethod("Calls", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [typeof(MainWindow), method])!;

    [Fact]
    public void AcceptedFullLaneReachesGameCaptureAndEveryCoverageGapResetsIt()
    {
        var initialize = Calls("InitializeActivityRecording");
        Assert.Contains("LocalGameActivityCapture..ctor", initialize);

        var read = Calls("CaptureActivityRead");
        Assert.Contains("LocalGameActivityCapture.Observe", read);
        Assert.Contains("LocalGameActivityCapture.Reset", read);

        var invalidation = Calls("InvalidateDiagnosticInventoryObservation");
        Assert.Contains("LocalGameActivityCapture.Reset", invalidation);
    }

    [Fact]
    public void ActivityStartMetadataUsesTheSameExecutableDirectoryContractAsTheWriter()
    {
        var initialize = Calls("InitializeActivityRecording");
        Assert.Contains("OverlayExecutionContext.get_ActivityLogDirectory", initialize);
        Assert.DoesNotContain("AppContext.get_BaseDirectory", initialize);
    }

    [Fact]
    public void ActivityHooksCoverEveryOwnedWindowAndPresentationPath()
    {
        var initialize = Calls("InitializeActivityRecording");
        Assert.Equal(3, initialize.Count(call => call == "MainWindow.WatchActivityWindow"));
        Assert.Contains("MainWindow.CaptureActivityPresentation",
            Calls("RenderDiagnosticInventoryReference"));
        Assert.Contains("MainWindow.RecordActivity", Calls("ScanCoreAsync"));
    }

    [Fact]
    public void LocalRecordingFailureAndLossHavePriorityOnTheVisibleStatus()
    {
        var method = typeof(MainWindow).GetMethod("LocalActivityRecordingWarning",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.Null(method!.Invoke(null, [false, 0L, false]));
        Assert.Equal("로컬 활동 기록 저장 지연 · 인식은 계속합니다",
            method.Invoke(null, [true, 0L, false]));
        var lossStatus = Assert.IsType<string>(method.Invoke(null, [true, 7L, false]));
        Assert.Contains("7", lossStatus);
        Assert.DoesNotContain("record-loss", lossStatus);
        Assert.NotEqual(method.Invoke(null, [true, 0L, false]), lossStatus);
        Assert.Equal("로컬 활동 기록 저장 실패 · 디스크와 권한을 확인하세요",
            method.Invoke(null, [true, 7L, true]));

        Assert.Contains("MainWindow.UpdateLocalActivityRecordingStatus", Calls("RecordActivity"));
        Assert.Contains("MainWindow.TryShowLocalActivityRecordingWarning",
            Calls("UpdateObservedTelemetryStatus"));
        Assert.Contains("MainWindow.TryShowLocalActivityRecordingWarning",
            Calls("UpdateGameplayRecordingStatus"));
    }

    [Fact]
    public void ShutdownCancelsScanningThenStopsTheLocalWriter()
    {
        var closing = Calls("Gameplay_OnClosing");
        Assert.Contains("CancellationTokenSource.Cancel", closing);
        Assert.Contains("MainWindow.StopActivityRecordingAsync", closing);
        Assert.True(closing.IndexOf("CancellationTokenSource.Cancel") <
                    closing.IndexOf("MainWindow.StopActivityRecordingAsync"));

        var stop = Calls("StopActivityRecordingAsync");
        Assert.Contains("DispatcherTimer.Stop", stop);
        Assert.Contains("LocalActivityLog.DisposeAsync", stop);
        Assert.Contains("Trace.TraceError", stop);
    }

    [Fact]
    public async Task ActualRandomWispSequenceRunsInIsolatedWpfProcess()
    {
        using var directory = new TempDirectory();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OrandOverlay.csproj")))
            root = root.Parent;
        Assert.NotNull(root);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var tool = Path.Combine(root!.FullName, "Tools", "ActivityLogCapture", "bin",
            configuration, "net8.0-windows", "RandypickBetaCapture.exe");
        Assert.True(File.Exists(tool), $"Activity replay tool is unavailable: {tool}");

        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(directory.OutputPath);
        start.ArgumentList.Add("--live-wisp");
        using var process = Process.Start(start);
        Assert.NotNull(process);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process!.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        var standardOutput = await output;
        var standardError = await error;
        Assert.True(process.ExitCode == 0,
            $"Replay exit {process.ExitCode}.{Environment.NewLine}{standardError}{Environment.NewLine}{standardOutput}");
        Assert.True(process.HasExited);

        var reportPath = Path.Combine(directory.OutputPath, "live-wisp-replay.json");
        Assert.True(File.Exists(reportPath), $"Replay report missing.{Environment.NewLine}{standardOutput}");
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        var rootElement = report.RootElement;
        Assert.True(rootElement.GetProperty("success").GetBoolean());
        Assert.Equal(process.Id, rootElement.GetProperty("processId").GetInt32());
        Assert.Equal(0, rootElement.GetProperty("remainingWindows").GetInt32());
        var evidence = rootElement.GetProperty("evidence");
        Assert.True(evidence.GetProperty("rejectedBasicRetained").GetBoolean());
        Assert.Equal("basic", evidence.GetProperty("gapLane").GetString());
        Assert.False(evidence.GetProperty("gapCorrelationReset").GetBoolean());
        Assert.False(evidence.GetProperty("gameCounterReset").GetBoolean());
        Assert.Equal(1, evidence.GetProperty("gameWispCount").GetInt32());
        Assert.Equal(1, evidence.GetProperty("wispCount").GetInt32());
        Assert.Equal(1, evidence.GetProperty("counterIncrement").GetInt32());
        Assert.Equal(1, evidence.GetProperty("candidate900h").GetInt32());
        Assert.False(evidence.GetProperty("duplicateAction").GetBoolean());
        Assert.False(evidence.GetProperty("actionConfirmed").GetBoolean());
        Assert.False(evidence.GetProperty("outputsAttributed").GetBoolean());
        Assert.True(rootElement.GetProperty("cleanup").GetProperty("ownedWindowsClosed").GetBoolean());
        Assert.True(rootElement.GetProperty("cleanup").GetProperty("processExitsWithTool").GetBoolean());
    }

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            RootPath = Directory.CreateTempSubdirectory("orand-live-wisp-process-").FullName;
            OutputPath = Path.Combine(RootPath, "capture");
        }

        internal string RootPath { get; }
        internal string OutputPath { get; }
        public void Dispose() => Directory.Delete(RootPath, recursive: true);
    }
}
