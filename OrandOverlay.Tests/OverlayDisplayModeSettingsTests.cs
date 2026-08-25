using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class OverlayDisplayModeSettingsTests
{
    [Theory]
    [InlineData(OverlayDisplayMode.Full)]
    [InlineData(OverlayDisplayMode.StatsOnly)]
    [InlineData(OverlayDisplayMode.Hidden)]
    public void DisplayModeRoundTrips(OverlayDisplayMode mode)
    {
        var path = TempPath();
        try
        {
            var settings = new AppSettings
            {
                OverlayDisplayMode = mode,
                LastVisibleOverlayDisplayMode =
                    mode == OverlayDisplayMode.Hidden
                        ? OverlayDisplayMode.StatsOnly
                        : mode
            };

            SettingsStore.Save(settings, path);
            var loaded = SettingsStore.Load(path);

            Assert.Equal(mode, loaded.OverlayDisplayMode);
            Assert.Equal(settings.LastVisibleOverlayDisplayMode,
                loaded.LastVisibleOverlayDisplayMode);
            Assert.Contains($"\"{mode}\"", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyJsonDefaultsToFull()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{"GoalUnitId":"rawcode:A90H"}""");

            var loaded = SettingsStore.Load(path);

            Assert.Equal(OverlayDisplayMode.Full, loaded.OverlayDisplayMode);
            Assert.Equal(OverlayDisplayMode.Full,
                loaded.LastVisibleOverlayDisplayMode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnknownAndHiddenLastVisibleNormalizeSafely()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path,
                """
                {
                  "OverlayDisplayMode": "future-mode",
                  "LastVisibleOverlayDisplayMode": "Hidden"
                }
                """);

            var loaded = SettingsStore.Load(path);

            Assert.Equal(OverlayDisplayMode.Full, loaded.OverlayDisplayMode);
            Assert.Equal(OverlayDisplayMode.Full,
                loaded.LastVisibleOverlayDisplayMode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(), "orand-overlay-mode-" + Guid.NewGuid() + ".json");
}
