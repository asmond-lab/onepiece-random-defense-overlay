using System.Reflection;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GameplayIntegrationWiringTests
{
    private static List<string> Calls(Type type, string method) =>
        (List<string>)typeof(BulletAbilityWiringTests)
            .GetMethod("Calls", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [type, method])!;

    [Fact]
    public void ConsentPrecedesMainConstructionAndRuntimeFactories()
    {
        var startup = Calls(typeof(App), "StartRuntime");
        Assert.Contains("OverlayExecutionContext.EnsureConsent", startup);
        Assert.True(startup.IndexOf("OverlayExecutionContext.EnsureConsent") <
            startup.IndexOf("MainWindow..ctor"));
        var initialize = Calls(typeof(MainWindow), "InitializeGameplayServices");
        Assert.Contains("OverlayExecutionContext.CreateGameplayTelemetry", initialize);
        Assert.Contains("OverlayExecutionContext.CreateGameplayStatsRefreshService", initialize);
    }

    [Fact]
    public void AcceptedObservationRecommendationResetAndShutdownReachRecorder()
    {
        Assert.Contains("MainWindow.CaptureGameplayTelemetry", Calls(typeof(MainWindow), "CaptureCoachObservation"));
        Assert.Contains("MainWindow.RecordGameplayRecommendation", Calls(typeof(MainWindow), "RecordCoachAsync"));
        Assert.Contains("MainWindow.ResetGameplayTelemetry", Calls(typeof(MainWindow), "ResetMatchSession"));
        Assert.Contains("MainWindow.ShutdownGameplayTelemetryAsync", Calls(typeof(MainWindow), "Gameplay_OnClosing"));
        Assert.Contains("GameplaySessionRecorder.Observe", Calls(typeof(MainWindow), "CaptureGameplayTelemetry"));
        Assert.Contains("GameplaySessionRecorder.Complete", Calls(typeof(MainWindow), "CaptureGameplayTelemetry"));
    }

    [Fact]
    public void CurrentNativeCountersAndCohortSnapshotsReachConsumers()
    {
        Assert.Contains("WarcraftMemoryRecognitionService.RecognizeCore",
            Calls(typeof(WarcraftMemoryRecognitionService), "Recognize"));
        var native = Calls(typeof(WarcraftMemoryRecognitionService), "RecognizeCore");
        Assert.Contains("WarcraftGambleCountersReader.Read", native);
        Assert.Contains("RecognitionResult.set_GambleCounters", native);
        var capture = Calls(typeof(MainWindow), "CaptureGameplayTelemetry");
        Assert.Contains("RecognitionResult.get_GambleCounters", capture);
        Assert.Contains("GameplayTelemetryObservation.set_GambleCounters", capture);
        var configure = Calls(typeof(MainWindow), "ConfigureGameplayEngine");
        Assert.Contains("RecommendationEngine.SetGameplayCohort", configure);
        Assert.Contains("GameplayStatsRefreshService.GetSnapshot", configure);
        Assert.Contains("RecommendationEngine.SetLiveStats", configure);
        Assert.True(configure.IndexOf("RecommendationEngine.SetGameplayCohort") <
            configure.IndexOf("RecommendationEngine.SetLiveStats"));
    }
}
