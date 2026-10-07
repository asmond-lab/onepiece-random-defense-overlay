using System.Text;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

// Tests exercise helpers with injected metadata and temporary files only.
// Never launch the apphost, call OnStartup, create a Window, or read production settings.
public sealed class RandyPickPackageProbeTests
{
    private static RandyPickPackageProbe.Metadata GoodMetadata(string version = "1.0.1-test.1") =>
        new(version, "RandyPick.exe", "OrandOverlay", true, true);

    private static string Source(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "App.xaml.cs"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, name));
    }

    [Fact]
    public void StartupDispatchPrecedesAllRuntimeDependenciesAndReturnsAfterShutdown()
    {
        var app = Source("App.xaml.cs");
        var startup = app[app.IndexOf("protected override void OnStartup(", StringComparison.Ordinal)..];
        startup = startup[..startup.IndexOf("private void StartRuntime(", StringComparison.Ordinal)];
        var dispatch = startup.IndexOf("RandyPickPackageProbe.TryHandleStartup(e.Args, out var probeExitCode)", StringComparison.Ordinal);
        Assert.True(dispatch >= 0);
        foreach (var token in new[] { "TelemetryConsentStartup.", "Execution.RuntimeEnabled", "StartRuntime(e)" })
            Assert.True(startup.IndexOf(token, StringComparison.Ordinal) > dispatch, token);
        var branchEnd = startup.IndexOf('}', dispatch);
        var branch = startup[dispatch..branchEnd];
        Assert.Contains("TelemetryConsentStartup.ClearStartupUri(this);", branch);
        Assert.DoesNotContain("StartupUri = null;", branch); // WPF's public setter rejects null, even after writing a successful report.
        var startupClear = Source("TelemetryConsentStartup.cs");
        Assert.Contains("GetField(\"_startupUri\"", startupClear);
        Assert.Contains("field.SetValue(application, null);", startupClear);
        Assert.DoesNotContain("Execution", startupClear);
        Assert.DoesNotContain("ShowDialog", startupClear);
        Assert.Contains("Shutdown(probeExitCode);", branch);
        Assert.True(branch.IndexOf("return;", StringComparison.Ordinal) > branch.IndexOf("Shutdown(probeExitCode);", StringComparison.Ordinal));
        var before = string.Join("\n", startup[..dispatch].Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        foreach (var forbidden in new[] { "Execution.", "new ", "base.OnStartup", "TelemetryConsent", "MainWindow", "StartRuntime(" })
            Assert.DoesNotContain(forbidden, before);
        Assert.DoesNotContain("private OverlayExecutionContext? _execution =", app);
    }

    [Fact]
    public void ProbeSourceDoesNotReachRuntimeServicesOrLaunchAnything()
    {
        var probe = Source("RandyPickPackageProbe.cs");
        foreach (var forbidden in new[] { "new UpdateService", "UpdateService.CanSelfInstall =>", "OverlayExecutionContext",
            "TelemetryConsent", "AppPaths.", "AppSettings", "DataCatalog", "Process.Start", "HttpClient",
            "UpdateTransport", "MainWindow(", "Window(", "GetProcesses", "ReadAllText(", "UpdateTrust.", "BundledTrust.Value" })
            Assert.DoesNotContain(forbidden, probe);
        Assert.Contains("Environment.ProcessPath", probe);
        Assert.Contains("FileMode.CreateNew, FileAccess.Write, FileShare.None", probe);
        Assert.DoesNotContain("Directory.CreateDirectory", probe);
        Assert.DoesNotContain("File.Delete", probe);
    }

    [Fact]
    public void OrdinaryArgumentsDoNotReadMetadataOrCreateOutput()
    {
        foreach (var args in new[] { Array.Empty<string>(), new[] { "--ordinary" } })
        {
            var handled = RandyPickPackageProbe.TryHandleStartup(args,
                () => throw new InvalidOperationException("metadata must not run"),
                _ => throw new InvalidOperationException("output must not run"), out var exit);
            Assert.False(handled);
            Assert.Equal(0, exit);
        }
    }

    public static IEnumerable<object[]> MalformedArguments()
    {
        yield return new object[] { new[] { "--verify-package" } };
        yield return new object[] { new[] { "--verify-package", "" } };
        yield return new object[] { new[] { "--verify-package", "relative.json" } };
        yield return new object[] { new[] { "--verify-package", @"C:relative.json" } };
        yield return new object[] { new[] { "--verify-package", @"\rooted.json" } };
        yield return new object[] { new[] { "--verify-package", @"\\server\share\out.json" } };
        yield return new object[] { new[] { "--verify-package", @"\\?\C:\out.json" } };
        yield return new object[] { new[] { "--verify-package", @"C:\out.json:stream" } };
        yield return new object[] { new[] { "--verify-package", @"C:\NUL.json" } };
        yield return new object[] { new[] { "--verify-package", @"C:\COM1.json" } };
        yield return new object[] { new[] { "--verify-package", @"C:\folder.\out.json" } };
        yield return new object[] { new[] { "--verify-package", @"C:\..\out.json" } };
        yield return new object[] { new[] { "--verify-package", @"C:\out.txt" } };
        yield return new object[] { new[] { "--verify-package", "C:\\out\0.json" } };
        yield return new object[] { new[] { "--verify-package", @"C:\" + new string('x', RandyPickPackageProbe.MaximumOutputPathLength) + ".json" } };
        yield return new object[] { new[] { "--verify-package", @"C:\out.json", "extra" } };
        yield return new object[] { new[] { "extra", "--verify-package", @"C:\out.json" } };
        yield return new object[] { new[] { "--verify-package", "--verify-package" } };
        yield return new object[] { new[] { "--VERIFY-PACKAGE", @"C:\out.json" } };
        yield return new object[] { new[] { @"--verify-package=C:\out.json" } };
    }

    [Theory]
    [MemberData(nameof(MalformedArguments))]
    public void RecognizedMalformedProbeNeverFallsThroughOrTouchesDependencies(string[] args)
    {
        var metadataCalls = 0;
        var outputCalls = 0;
        var handled = RandyPickPackageProbe.TryHandleStartup(args,
            () => { metadataCalls++; return GoodMetadata(); },
            _ => { outputCalls++; return new MemoryStream(); }, out var exit);
        Assert.True(handled); // App must Shutdown(exit), not start its runtime.
        Assert.Equal(RandyPickPackageProbe.InvalidArguments, exit);
        Assert.Equal(0, metadataCalls);
        Assert.Equal(0, outputCalls);
    }

    [Theory]
    [InlineData(@"C:\package proof\new.json")]
    [InlineData("C:/package proof/new.JSON")]
    public void AcceptsOnlyExactFlagAndAbsoluteJsonOutput(string value)
    {
        Assert.True(RandyPickPackageProbe.TryParseOutputPath(new[] { "--verify-package", value }, out var output));
        Assert.Equal(Path.GetFullPath(value), output);
    }

    [Theory]
    [InlineData("1.0.1-test.1", "test", "/v1/channels/test/win-x64", true)]
    [InlineData("1.0.1", "stable", "/v1/channels/stable/win-x64", true)]
    [InlineData("development", null, null, false)]
    public void JsonHasOnlySafeMetadataAndPathLocalFacts(string version, string? channel, string? manifestSuffix, bool supported)
    {
        using var output = new MemoryStream();
        Assert.True(RandyPickPackageProbe.TryHandleStartup(new[] { "--verify-package", @"C:\new.json" },
            () => GoodMetadata(version), _ => output, out var exit));
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output.ToArray());
        var report = json.RootElement;
        Assert.Equal(12, report.EnumerateObject().Count());
        Assert.Equal(version, report.GetProperty("Version").GetString());
        Assert.Equal("RandyPick.exe", report.GetProperty("AssetName").GetString());
        Assert.Equal("OrandOverlay", report.GetProperty("AssemblyName").GetString());
        Assert.Equal(channel, report.GetProperty("SelectedChannel").GetString());
        Assert.Equal(manifestSuffix is null ? null : "https://orand-updates.epic42121.workers.dev" + manifestSuffix,
            report.GetProperty("ManifestUrl").GetString());
        Assert.Equal(supported, report.GetProperty("SupportsAutomaticUpdates").GetBoolean());
        Assert.True(report.GetProperty("CanSelfInstall").GetBoolean());
        Assert.True(report.GetProperty("LogoResourcesVerified").GetBoolean());
        Assert.False(report.GetProperty("RuntimeStarted").GetBoolean());
        foreach (var name in new[] { "NoNetwork", "NoGameReads", "NoUserSettingsOpened" })
            Assert.True(report.GetProperty(name).GetBoolean());
        Assert.DoesNotContain("C:", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void ReportPreservesActualRenamedAssetAndInstallCapability()
    {
        using var output = new MemoryStream();
        Assert.True(RandyPickPackageProbe.TryHandleStartup(new[] { "--verify-package", @"C:\new.json" },
            () => GoodMetadata() with { AssetName = "RenamedRandyPick.exe", CanSelfInstall = false }, _ => output, out var exit));
        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(output.ToArray());
        Assert.Equal("RenamedRandyPick.exe", json.RootElement.GetProperty("AssetName").GetString());
        Assert.False(json.RootElement.GetProperty("CanSelfInstall").GetBoolean());
    }

    [Fact]
    public void MetadataExceptionsAndMissingLogosAreNonzeroWithoutOutput()
    {
        foreach (var read in new Func<RandyPickPackageProbe.Metadata>[] {
            () => throw new IOException("injected metadata failure"),
            () => GoodMetadata() with { LogoResourcesVerified = false },
            () => GoodMetadata() with { AssemblyName = "WrongAssembly" },
            () => GoodMetadata() with { Version = "" } })
        {
            var outputCalls = 0;
            Assert.True(RandyPickPackageProbe.TryHandleStartup(new[] { "--verify-package", @"C:\new.json" }, read,
                _ => { outputCalls++; return new MemoryStream(); }, out var exit));
            Assert.Equal(RandyPickPackageProbe.MetadataFailure, exit);
            Assert.Equal(0, outputCalls);
        }
    }

    [Fact]
    public void OutputExceptionIsNonzeroAndStillHandled()
    {
        Assert.True(RandyPickPackageProbe.TryHandleStartup(new[] { "--verify-package", @"C:\new.json" },
            () => GoodMetadata(), _ => throw new UnauthorizedAccessException(), out var exit));
        Assert.Equal(RandyPickPackageProbe.OutputFailure, exit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOrDisposeFailureCannotReportSuccess(bool failOnDispose)
    {
        Assert.True(RandyPickPackageProbe.TryHandleStartup(new[] { "--verify-package", @"C:\new.json" },
            () => GoodMetadata(), _ => new FailingOutput(failOnDispose), out var exit));
        Assert.Equal(RandyPickPackageProbe.OutputFailure, exit);
    }

    private sealed class FailingOutput(bool failOnDispose) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!failOnDispose) throw new IOException("injected write failure");
            base.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && failOnDispose) throw new IOException("injected dispose failure");
        }
    }

    [Fact]
    public void CreateNewWritesOnceAndNeverOverwritesExistingOutput()
    {
        var folder = Path.Combine(Path.GetTempPath(), "randypick-probe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "new.json");
            var args = new[] { "--verify-package", output };
            Assert.True(RandyPickPackageProbe.TryHandleStartup(args, () => GoodMetadata(), RandyPickPackageProbe.CreateNewOutput, out var exit));
            Assert.Equal(0, exit);
            var before = File.ReadAllBytes(output);
            Assert.True(RandyPickPackageProbe.TryHandleStartup(args, () => GoodMetadata("9.9.9"), RandyPickPackageProbe.CreateNewOutput, out exit));
            Assert.Equal(RandyPickPackageProbe.OutputFailure, exit);
            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Single(Directory.GetFiles(folder));
            var missingParent = Path.Combine(folder, "not-created", "out.json");
            Assert.True(RandyPickPackageProbe.TryHandleStartup(new[] { "--verify-package", missingParent },
                () => GoodMetadata(), RandyPickPackageProbe.CreateNewOutput, out exit));
            Assert.Equal(RandyPickPackageProbe.OutputFailure, exit);
            Assert.False(Directory.Exists(Path.GetDirectoryName(missingParent)));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
