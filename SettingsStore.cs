using System.Text.Json;

namespace OrandOverlay;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load() => Load(AppPaths.SettingsFile);

    internal static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var raw = File.ReadAllText(path);
            var (migrated, changed) = LegacySettingsMigration.Run(raw);
            if (changed) File.WriteAllText(path, migrated);
            try
            {
                return Normalize(
                    JsonSerializer.Deserialize<AppSettings>(migrated, Options) ?? new());
            }
            catch (JsonException)
            {
                // 파일 자체가 깨졌을 때만 격리한다. 매 로드마다 조용히 기본값으로
                // 덮어쓰면 손상 원인을 진단할 수 없으므로 원본을 sidecar로 남긴다.
                QuarantineCorruptFile(path);
                return new();
            }
        }
        catch
        {
            return new();
        }
    }

    private static void QuarantineCorruptFile(string path)
    {
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Move(path, path + ".corrupt-" + stamp);
        }
        catch { /* 격리 실패는 무시 — 다음 로드에서 다시 시도 */ }
    }

    /// <summary>익명 텔레메트리 ID가 없으면 만들어 저장한다(설치 후 1회).</summary>
    public static AppSettings EnsureTelemetryAnonId(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.TelemetryAnonId)) return settings;
        settings.TelemetryAnonId = Guid.NewGuid().ToString();
        try { Save(settings); } catch { /* 다음 저장 때 함께 */ }
        return settings;
    }

    public static void Save(AppSettings settings) => Save(settings, AppPaths.SettingsFile);

    /// <summary>temp 쓰기 후 원자적 이름 교체 — 중간 크래시에도 반쯤 쓰인 설정이 남지 않는다.</summary>
    internal static void Save(AppSettings settings, string path)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (settings.LastVisibleOverlayDisplayMode == OverlayDisplayMode.Hidden)
            settings.LastVisibleOverlayDisplayMode = OverlayDisplayMode.Full;
        return settings;
    }
}
