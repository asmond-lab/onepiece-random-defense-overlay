using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticOverlayToggleTests
{
    [Fact]
    public void LossOfCurrentHandDoesNotDisableUserVisibilityToggle()
    {
        Assert.True(DiagnosticReferencePresentationPolicy.ToggleAvailable(session: true, fences: true, pendingUpdate: false));
        Assert.False(DiagnosticReferencePresentationPolicy.ToggleAvailable(session: true, fences: false, pendingUpdate: false));
        Assert.True(DiagnosticReferencePresentationPolicy.ToggleAvailable(session: false, fences: false, pendingUpdate: true));
        var source = File.ReadAllText(Path.Combine(FindRoot(), "MainWindow.xaml.cs"));
        Assert.Contains("DiagnosticReferencePresentationPolicy.ToggleAvailable", source);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml.cs"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
