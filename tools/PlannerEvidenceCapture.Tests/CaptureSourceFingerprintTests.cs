using System.Text;
using System.IO;
using PlannerEvidenceCapture;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class CaptureSourceFingerprintTests
{
    [Fact]
    public void BuiltBundleComputesWithoutWorkingDirectorySources()
    {
        string? result = null;
        var error = Record.Exception(() => result = CaptureSourceFingerprint.FromCanonicalFiles());
        Assert.Null(error);
        Assert.Matches("^[A-F0-9]{64}$", result!);
    }

    [Fact]
    public void BundleRejectsTamperedInput()
    {
        using var bundle = new BundleFixture();
        File.AppendAllText(Path.Combine(bundle.Root, "FingerprintInputs", "App.xaml.input"), "tamper");
        Assert.Throws<InvalidDataException>(() => CaptureSourceFingerprint.FromBundle(bundle.Root));
    }

    [Fact]
    public void BundleRejectsTraversalInsteadOfNormalizingIt()
    {
        using var bundle = new BundleFixture();
        var alias = Path.Combine(bundle.Root, "..", Path.GetFileName(bundle.Root));
        Assert.Throws<ArgumentException>(() => CaptureSourceFingerprint.FromBundle(alias));
    }

    [Fact]
    public void BundleRejectsJunctionEvenWhenTargetBytesMatch()
    {
        using var bundle = new BundleFixture();
        var inputs = Path.Combine(bundle.Root, "FingerprintInputs");
        var moved = Path.Combine(bundle.Root, "real-inputs");
        Directory.Move(inputs, moved);
        TempJunction.Create(bundle.Root, inputs, moved);
        try
        {
            Assert.Throws<IOException>(() => CaptureSourceFingerprint.FromBundle(bundle.Root));
        }
        finally { Directory.Delete(inputs); }
    }

    [Fact]
    public void BundleRejectsEveryMissingPackagedInputWithoutSourceFallback()
    {
        using var bundle = new BundleFixture();
        foreach (var path in CaptureSourceFingerprint.CanonicalPaths)
        {
            var file = Path.Combine(bundle.Root, "FingerprintInputs", path + ".input");
            var bytes = File.ReadAllBytes(file);
            File.Delete(file);
            try { Assert.Throws<IOException>(() => CaptureSourceFingerprint.FromBundle(bundle.Root)); }
            finally { File.WriteAllBytes(file, bytes); }
        }
    }

    [Fact]
    public void BundleRejectsEveryModifiedPackagedInput()
    {
        using var bundle = new BundleFixture();
        foreach (var path in CaptureSourceFingerprint.CanonicalPaths)
        {
            var file = Path.Combine(bundle.Root, "FingerprintInputs", path + ".input");
            var bytes = File.ReadAllBytes(file);
            File.AppendAllText(file, "modified");
            try { Assert.Throws<InvalidDataException>(() => CaptureSourceFingerprint.FromBundle(bundle.Root)); }
            finally { File.WriteAllBytes(file, bytes); }
        }
    }

    [Fact]
    public void BundledBytesPreserveCanonicalPathAndContentLengthFraming()
    {
        var inputs = CaptureSourceFingerprint.CanonicalPaths.Select(path => new CaptureSourceInput(path,
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "FingerprintInputs", path + ".input")))).ToArray();
        Assert.Equal(CaptureSourceFingerprint.Compute(inputs), CaptureSourceFingerprint.FromCanonicalFiles());
        foreach (var changedPath in CaptureSourceFingerprint.CanonicalPaths)
        {
            var changed = inputs.Select(input => input.Path == changedPath
                ? new CaptureSourceInput(input.Path, input.Content.Concat(new byte[] { 42 }).ToArray()) : input);
            Assert.NotEqual(CaptureSourceFingerprint.Compute(inputs), CaptureSourceFingerprint.Compute(changed));
        }
        Assert.NotEqual(CaptureSourceFingerprint.Compute(new[] { Input("ab", "c") }),
            CaptureSourceFingerprint.Compute(new[] { Input("a", "bc") }));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("C:relative")]
    [InlineData("C:/temp/./bundle")]
    [InlineData("C:/temp/bundle.")]
    [InlineData("C:/temp/bundle ")]
    [InlineData("C:/temp/bundle:ads")]
    [InlineData("C:/temp/NUL")]
    [InlineData("//server/share/bundle")]
    [InlineData("//?/C:/temp/bundle")]
    public void BundleRejectsAmbiguousRoots(string root) =>
        Assert.Throws<ArgumentException>(() => CaptureSourceFingerprint.FromBundle(root));

    [Theory]
    [InlineData("root")]
    [InlineData("ancestor")]
    [InlineData("nested")]
    [InlineData("file")]
    public void BundleRejectsJunctionAtEveryReadBoundary(string boundary)
    {
        using var bundle = new BundleFixture();
        var alias = Path.Combine(bundle.Root, "alias");
        string readRoot = bundle.Root;
        if (boundary == "root")
        {
            var target = Path.Combine(bundle.Root, "real-bundle");
            Directory.CreateDirectory(target);
            Directory.Move(Path.Combine(bundle.Root, "FingerprintInputs"), Path.Combine(target, "FingerprintInputs"));
            TempJunction.Create(bundle.Root, alias, target);
            readRoot = alias;
        }
        else if (boundary == "ancestor")
        {
            var target = Path.Combine(bundle.Root, "real-parent");
            var child = Path.Combine(target, "child");
            Directory.CreateDirectory(child);
            Directory.Move(Path.Combine(bundle.Root, "FingerprintInputs"), Path.Combine(child, "FingerprintInputs"));
            TempJunction.Create(bundle.Root, alias, target);
            readRoot = Path.Combine(alias, "child");
        }
        else
        {
            alias = Path.Combine(bundle.Root, "FingerprintInputs", boundary == "nested" ? "Data" : "App.xaml.input");
            var target = Path.Combine(bundle.Root, "real-target");
            if (boundary == "nested") Directory.Move(alias, target);
            else { File.Delete(alias); Directory.CreateDirectory(target); }
            TempJunction.Create(bundle.Root, alias, target);
        }
        try { Assert.Throws<IOException>(() => CaptureSourceFingerprint.FromBundle(readRoot)); }
        finally { Directory.Delete(alias); }
    }

    [Fact]
    public void BundleRejectsUnexpectedPackageEntry()
    {
        using var bundle = new BundleFixture();
        File.WriteAllText(Path.Combine(bundle.Root, "FingerprintInputs", "unexpected.input"), "extra");
        Assert.Throws<InvalidDataException>(() => CaptureSourceFingerprint.FromBundle(bundle.Root));
    }

    private sealed class BundleFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "fingerprint-test-" + Guid.NewGuid().ToString("N"));
        internal BundleFixture()
        {
            foreach (var path in CaptureSourceFingerprint.CanonicalPaths)
            {
                var destination = Path.Combine(Root, "FingerprintInputs", path + ".input");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(AppContext.BaseDirectory, "FingerprintInputs", path + ".input"), destination);
            }
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

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
