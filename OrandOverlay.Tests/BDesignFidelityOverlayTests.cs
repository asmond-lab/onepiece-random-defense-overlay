using Xunit;

namespace OrandOverlay.Tests;

public sealed class BDesignFidelityOverlayTests
{
    private static string Source(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(name);
    }
    [Fact]
    public void OverlayKeepsDimensionsAndUsesRealModeEvents()
    {
        var xaml = Source("OverlayWindow.xaml");
        Assert.Contains("Width=\"540\" Height=\"740\"", xaml);
        Assert.Contains("Assets/randypick-logo-64.png", xaml);
        Assert.Contains("{x:Static local:RandyPickBrand.BetaLabel}", xaml);
        Assert.Equal($"BETA {UpdateService.CurrentVersion.ToString(3)}", RandyPickBrand.BetaLabel);
        Assert.Contains("overlay-mode-segment", xaml);
        var coach = Source("OverlayWindow.Coach.cs");
        Assert.Contains("ModeRequested?.Invoke(mode)", coach);
        Assert.Contains("SetModePresentation(frame.Mode)", coach);
        Assert.Contains("_overlay.ModeRequested += SetPlayMode", Source("NormalCandidateMain.cs"));
    }
    [Fact]
    public void OverlayCardsFoldsAndPinRemainPresentationOnly()
    {
        var source = Source("NormalCandidateView.cs") + Source("NormalCandidateView.Craft.cs") + Source("NormalCandidateView.Progression.cs");
        Assert.Contains("!IsMainWorkspace && _candidateScroll.ActualWidth >= 430 ? 5", source);
        Assert.Contains("IsMainWorkspace ? 10 : 5", source);
        Assert.Contains("OverlayCandidateCard", source);
        Assert.Contains("FoldChoices", source);
        Assert.Contains("state.HasUserChoice = true", source);
        Assert.Contains("_model.CollapsedCategories.Contains(category)", source);
        Assert.Contains("_model.FirstUpperId", source);
        Assert.Contains("직접 선택", source);
        Assert.Contains("normal-craft-flow", source);
        Assert.Contains("OverlayTheme.PlanCanvas", source);
        Assert.Contains("_model!.CanHighlight(c)", source);
        Assert.Contains("normal-resume", source);
        Assert.DoesNotContain("_model.Update(", source);
    }
}
