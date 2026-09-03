using System.Text;
using PlannerEvidenceCapture;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class CaptureSourceFingerprintTests
{
    [Fact]
    public void CanonicalInputsBindOverlayThemeExactlyOnce()
    {
        Assert.Equal(14, CaptureSourceFingerprint.CanonicalPaths.Count);
        Assert.Single(CaptureSourceFingerprint.CanonicalPaths,
            path => path.Equals("App.xaml", StringComparison.Ordinal));
        Assert.Single(CaptureSourceFingerprint.CanonicalPaths,
            path => path.Equals("MainWindow.xaml", StringComparison.Ordinal));
        Assert.Single(CaptureSourceFingerprint.CanonicalPaths,
            path => path.Equals("OverlayTheme.cs", StringComparison.Ordinal));
        Assert.Single(CaptureSourceFingerprint.CanonicalPaths,
            path => path.Equals(
                "tools/PlannerEvidenceCapture/CapturePixelContract.cs",
                StringComparison.Ordinal));
        Assert.Single(CaptureSourceFingerprint.CanonicalPaths,
            path => path.Equals("tools/PlannerEvidenceCapture/Program.cs",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeIsDeterministicAndInputOrderStable()
    {
        var inputs = new[]
        {
            Input("RecommendationBoard.cs", "board"),
            Input("OverlayTheme.cs", "tokens"),
            Input("DESIGN.md", "contract")
        };

        var first = CaptureSourceFingerprint.Compute(inputs);
        var repeated = CaptureSourceFingerprint.Compute(inputs);
        var reversed = CaptureSourceFingerprint.Compute(inputs.Reverse());

        Assert.Equal(first, repeated);
        Assert.Equal(first, reversed);
    }

    [Fact]
    public void ComputeChangesWhenOnlyOverlayThemeChanges()
    {
        var baseline = new[]
        {
            Input("RecommendationBoard.cs", "board"),
            Input("OverlayTheme.cs", "tokens-v1"),
            Input("DESIGN.md", "contract")
        };
        var changed = baseline.Select(input => input.Path == "OverlayTheme.cs"
            ? Input(input.Path, "tokens-v2")
            : input);

        Assert.NotEqual(CaptureSourceFingerprint.Compute(baseline),
            CaptureSourceFingerprint.Compute(changed));
    }

    [Fact]
    public void CaptureVariantsCoverEveryPendingStateAtSupportedScales()
    {
        var pendingStates = new[]
        {
            OrandOverlay.PlannerEvidenceState.SequenceStoryReward,
            OrandOverlay.PlannerEvidenceState.SequenceRareReward,
            OrandOverlay.PlannerEvidenceState.SequenceTopNavigation
        };
        var supportedScales = new[] { 0.75, 1.0, 1.25, 1.5 };

        var variants = Program.CaptureVariants();

        foreach (var state in pendingStates)
            Assert.Equal(supportedScales,
                variants.Where(item => item.State == state)
                    .Select(item => item.Scale).Order());
    }

    private static CaptureSourceInput Input(string path, string content) =>
        new(path, Encoding.UTF8.GetBytes(content));
}
