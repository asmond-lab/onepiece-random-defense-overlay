using Xunit;
namespace OrandOverlay.Tests;

public sealed class GoroseiProducerTests
{
    internal static string Source(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MainWindow.xaml.cs"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, name));
    }
    [Fact]
    public void AcceptedScanResetsBeforeCurrentObservationAndRejectsOldMatchFence()
    {
        var text = Source("MainWindow.xaml.cs");
        var scan = text[text.IndexOf("private async Task ScanCoreAsync")..text.IndexOf("private void ResetMatchSession")];
        Assert.Contains("var matchGeneration = _adaptivePlanning.MatchGeneration;", scan);
        Assert.Contains("matchGeneration != _adaptivePlanning.MatchGeneration", scan);
        Assert.True(scan.IndexOf("ResetMatchSession();") < scan.IndexOf("_goroseiObservation.Accept("));
        Assert.Contains("++_recognitionRevision", scan);
        Assert.DoesNotContain("ApplyDetectedGorosei(result.Diagnostics.Gorosei)", scan);
        Assert.Contains("_goroseiObservation.Current", Source("MainWindow.Coach.cs"));
        Assert.Contains("Append(_goroseiObservation.Current)", text);
        Assert.DoesNotContain("_navigationSession.ConfirmedOptionId, _detectedGorosei", text);
        Assert.DoesNotContain("_detectedGorosei, guidePlan.Support", text);
        Assert.Contains("if (playMode == PlayMode.Guide) gorosei = CurrentGorosei;", text);
    }
}
