using System.Reflection;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GameplayReliabilityWiringTests
{
    private static List<string> Calls(Type type, string method) =>
        (List<string>)typeof(BulletAbilityWiringTests)
            .GetMethod("Calls", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [type, method])!;

    [Fact]
    public void NativeCaptureReachesRecoveryAndVisibleStatusWithoutReplacingItsSource()
    {
        var capture = Calls(typeof(MainWindow), "CaptureGameplayTelemetry");
        Assert.Contains("GameplayRecoveryJournal.RecordObservation", capture);
        Assert.Contains("GameplayRecoveryJournal.MarkRecognitionRecovered", capture);
        Assert.Contains("MainWindow.RecordGameplayRecognitionGap", capture);
        Assert.Contains("MainWindow.UpdateGameplayRecordingStatus", capture);
        Assert.Contains("GameplaySessionRecorder.Observe", capture);
        Assert.Contains("RecognitionResult.get_Entries", capture);
    }

    [Fact]
    public void FixtureCannotCreatePersistentRecoveryJournal()
    {
        var fixture = OverlayExecutionContext.Fixture(new AppSettings());
        Assert.Null(fixture.CreateGameplayRecoveryJournal(new DataCatalog()));
    }

    [Fact]
    public void UploadedMetadataDistinguishesTestBuildRevisions()
    {
        Assert.Contains("UpdateService.get_CurrentBuildVersion",
            Calls(typeof(MainWindow), "InitializeGameplayServices"));
        Assert.Equal(typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion, UpdateService.CurrentBuildVersion);
    }
}
