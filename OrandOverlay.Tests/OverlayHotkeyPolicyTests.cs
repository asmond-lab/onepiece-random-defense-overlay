using System.Reflection;
using System.Text.Json;
using System.Windows.Input;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class OverlayHotkeyPolicyTests
{
    [Fact]
    public void MainWindowWiresMigrationCaptureAndMessageGuardToProductionPolicy()
    {
        Assert.Contains("OverlayHotkeyPolicy.Normalize", Calls("OnSourceInitialized"));
        Assert.Contains("OverlayExecutionContext.SaveSettings", Calls("OnSourceInitialized"));
        Assert.Contains("OverlayHotkeyPolicy.ResolveKey", Calls("ApplyOverlayHotkey"));
        Assert.Contains("AppSettings.set_OverlayToggleKeyCustomized", Calls("Hotkey_OnPreviewKeyDown"));
        Assert.Contains("OverlayHotkeyPolicy.ShouldToggle", Calls("OnWindowMessage"));
    }

    private static List<string> Calls(string method) =>
        (List<string>)typeof(BulletAbilityWiringTests)
            .GetMethod("Calls", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [typeof(MainWindow), method])!;

    [Fact]
    public void NewSettingsUseF1()
    {
        Assert.Equal("F1", new AppSettings().OverlayToggleKey);
    }

    [Fact]
    public void NativeRegistrationSuppressesHeldKeyRepeats()
    {
        Assert.Equal(0x4000u, OverlayHotkeyPolicy.RegistrationModifiers);
    }

    [Theory]
    [InlineData("Capital")]
    [InlineData("CapsLock")]
    [InlineData("Scroll")]
    [InlineData("capital")]
    [InlineData("")]
    [InlineData(" ")]
    public void LegacyDefaultsMigrateToF1AndSecondPassDoesNotRewrite(string oldKey)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(new { OverlayToggleKey = oldKey }))!;

        Assert.True(OverlayHotkeyPolicy.Normalize(settings));
        Assert.Equal("F1", settings.OverlayToggleKey);
        Assert.False(OverlayHotkeyPolicy.Normalize(settings));
    }

    [Theory]
    [InlineData("F2", false)]
    [InlineData("OemTilde", false)]
    [InlineData("Capital", true)]
    [InlineData("Scroll", true)]
    public void DeliberateCustomKeysSurviveNormalizationAndSerialization(string key, bool customized)
    {
        var settings = new AppSettings
        {
            OverlayToggleKey = key,
            OverlayToggleKeyCustomized = customized
        };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;

        Assert.False(OverlayHotkeyPolicy.Normalize(loaded));
        Assert.Equal(key, loaded.OverlayToggleKey);
        Assert.Equal(customized, loaded.OverlayToggleKeyCustomized);
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("999")]
    [InlineData("None")]
    [InlineData("System")]
    [InlineData("ImeProcessed")]
    [InlineData("DeadCharProcessed")]
    public void InvalidKeyFallsBackToF1(string keyName)
    {
        Assert.Equal(Key.F1, OverlayHotkeyPolicy.ResolveKey(keyName));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void OnlyRegisteredHotkeysOutsideCaptureToggle(bool registered, bool capturing, bool expected)
    {
        Assert.Equal(expected, OverlayHotkeyPolicy.ShouldToggle(registered, capturing));
    }
}
