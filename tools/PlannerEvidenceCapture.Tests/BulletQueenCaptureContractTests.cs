using Xunit;
namespace PlannerEvidenceCapture.Tests;

public sealed class BulletQueenCaptureContractTests
{
    [Fact]
    public void ReadyFixtureRequiresActualUserSelectionAfterUnknownNegativeControl()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OrandOverlay.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "tools/PlannerEvidenceCapture/BulletGuideCapture.cs"));
        var beforeReady = source[..source.IndexOf("var queenPair = Observe(\"queen-bonclay-ready\"")];
        var start = beforeReady.LastIndexOf("var queenUnknown = Observe(");
        Assert.True(start >= 0, "Missing Unknown Queen negative control before ready fixture");
        var block = beforeReady[start..];
        Assert.Contains("queen-bonclay-unknown", block);
        Assert.Contains("QueenConversionInput.Unknown", block);
        Assert.Contains("WaitCoach(main, () => ((ComboBox)view.FindName(\"QueenCondition\")).SelectedValue =", block);
        Assert.Contains("QueenConversionInput.UserConfirmedMissionsComplete", block);
        Assert.DoesNotContain("GetField(\"_queenInput\"", block);
    }
}
