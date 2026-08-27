using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

/// <summary>
/// SettingsStore의 원자적 저장과 손상 파일 격리를 검증한다.
/// 경로 오버로드(internal, InternalsVisibleTo)로 실제 %LocalAppData%를 건드리지 않는다.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orand-settings-tests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void SaveLoad_RoundTrip_PreservesValues()
    {
        var path = PathFor("settings.json");
        Directory.CreateDirectory(_dir);
        var settings = new AppSettings
        {
            GoalUnitId = "yamato_transcendent",
            TelemetryAnonId = "anon-123",
            AutoRecommendNavigation = false
        };

        SettingsStore.Save(settings, path);
        var loaded = SettingsStore.Load(path);

        Assert.Equal("yamato_transcendent", loaded.GoalUnitId);
        Assert.Equal("anon-123", loaded.TelemetryAnonId);
        Assert.False(loaded.AutoRecommendNavigation);
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        var path = PathFor("settings.json");
        Directory.CreateDirectory(_dir);

        SettingsStore.Save(new AppSettings { GoalUnitId = "g" }, path);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults_AndQuarantinesOriginal()
    {
        var path = PathFor("settings.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ not valid json {{{");

        var loaded = SettingsStore.Load(path);

        // 기본값은 빈 문자열이 아니라 앱 기본 목표(yamato_transcendent)다.
        Assert.Equal(new AppSettings().GoalUnitId, loaded.GoalUnitId);
        Assert.True(loaded.AutoRecommendNavigation);
        Assert.False(File.Exists(path), "손상 원본은 그 자리에 남지 않아야 한다(격리됨)");
        var quarantined = Directory.GetFiles(_dir, "settings.json.corrupt-*");
        Assert.Single(quarantined);
    }

    [Fact]
    public void Load_ValidFile_IsNotQuarantined()
    {
        var path = PathFor("settings.json");
        Directory.CreateDirectory(_dir);
        SettingsStore.Save(new AppSettings { GoalUnitId = "keep" }, path);

        SettingsStore.Load(path);

        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Save_ToInvalidDirectory_ThrowsLikeBefore()
    {
        // 기존 계약 유지 확인: 저장 실패는 조용히 삼키지 않고 호출부로 전파된다.
        var blocker = PathFor("blocker.txt");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocker, "file used as directory");
        var path = Path.Combine(blocker, "nested", "settings.json");

        Assert.ThrowsAny<Exception>(() => SettingsStore.Save(new AppSettings(), path));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 정리 실패 무시 */ }
    }
}
