using System.Text.RegularExpressions;
using Xunit;

namespace OrandOverlay.Tests;

// Deliberately inspect source without constructing/showing a WPF window or reserving an OS hotkey.
public sealed class NonRuntimeWindowSourceTests
{
    private static string Source(string name = "MainWindow.xaml.cs")
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, name));
    }

    [Fact]
    public void NonRuntimeConstructionRequiresSettingsOverrideAndInjectsInertTelemetry()
    {
        var source = Source();
        Assert.Contains("OverlayExecutionContext.Fixture(settingsOverride ?? throw new ArgumentNullException", source);
        Assert.True(source.IndexOf("ArgumentNullException.ThrowIfNull(execution)", StringComparison.Ordinal) < source.IndexOf("InitializeComponent();", StringComparison.Ordinal));
        Assert.Contains("_runtimeEffects = execution.RuntimeEnabled;", source);
        Assert.Contains("execution.CreateTelemetry(_settings.TelemetryEnabled", source);
        Assert.Contains("_persistSettings = execution.RuntimeEnabled && settingsOverride is null;", source);
        Assert.Throws<ArgumentNullException>(() => OverlayExecutionContext.Fixture(null!));
        Assert.False(OverlayExecutionContext.Fixture(new AppSettings()).CreateTelemetry(true).Enabled);
    }

    [Theory]
    [InlineData("ApplyOverlayHotkey")]
    [InlineData("CheckUpdateNow_OnClick")]
    [InlineData("InstallUpdateAsync")]
    [InlineData("RefreshClearDataAsync")]
    [InlineData("Telemetry_OnChanged")]
    [InlineData("AcknowledgeTelemetryDisclosure_OnClick")]
    [InlineData("DeleteTelemetryQueue_OnClick")]
    [InlineData("ScanAsync")]
    [InlineData("AutoScan_OnChanged")]
    public void RuntimeEntryPointGuardsBeforeAnyEffect(string method)
    {
        var source = Source(method is "CheckUpdateNow_OnClick" or "InstallUpdateAsync"
            ? "MainWindow.Updates.cs" : "MainWindow.xaml.cs");
        var match = Regex.Match(source, @"(?:private|protected override)\s+(?:async\s+)?(?:void|Task)\s+" + method + @"\([^)]*\)\s*\{(?<prefix>[^{}]*)");
        if (method is "Telemetry_OnChanged" or "AcknowledgeTelemetryDisclosure_OnClick" or "DeleteTelemetryQueue_OnClick")
        {
            // Mandatory launch consent replaces these optional, callable legacy controls.
            Assert.False(match.Success);
            return;
        }
        Assert.True(match.Success, "Method not found: " + method);
        var prefix = match.Groups["prefix"].Value.TrimStart();

        // Memory entry points use the narrower live-memory capability, not merely runtime effects.
        var expectedGuard = method is "ScanAsync" or "AutoScan_OnChanged"
            ? "if (!_execution.LiveMemoryEnabled) return;"
            : method is "CheckUpdateNow_OnClick" or "InstallUpdateAsync"
                ? "if (!_runtimeEffects || !_execution.HasCurrentConsent || _updateLifecycleClosed) return;"
                : "if (!_runtimeEffects) return;";
        Assert.StartsWith(expectedGuard, prefix);
        Assert.False(OverlayExecutionContext.Fixture(new AppSettings()).LiveMemoryEnabled);
    }

    [Fact]
    public void SourceInitializationKeepsOwnWindowThemeButGatesGlobalHook()
    {
        var source = Source();
        var start = source.IndexOf("protected override void OnSourceInitialized", StringComparison.Ordinal);
        var end = source.IndexOf("private void ApplyOverlayHotkey", start, StringComparison.Ordinal);
        var body = source[start..end];
        var guard = body.IndexOf("if (!_runtimeEffects) return;", StringComparison.Ordinal);
        Assert.True(guard > body.IndexOf("ApplyThemedTitleBar(helper.Handle);", StringComparison.Ordinal));
        Assert.True(guard < body.IndexOf("_hotkeyWindowHandle =", StringComparison.Ordinal));
        Assert.True(guard < body.IndexOf("AddHook", StringComparison.Ordinal));
    }
}
