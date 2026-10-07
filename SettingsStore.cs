using System.Text.Json;

namespace OrandOverlay;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // Consent inspection must never migrate, quarantine, create directories or write.
    internal static AppSettings? ReadForConsent(string path)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options); }
        catch (IOException) { return null; } // Missing/unreadable consent is not authorization.
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    public static AppSettings Load() => Load(AppPaths.SettingsFile);

    internal static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var raw = File.ReadAllText(path);
            var (migrated, changed) = LegacySettingsMigration.Run(raw);
            AppSettings settings;
            try
            {
                settings = Normalize(
                    JsonSerializer.Deserialize<AppSettings>(migrated, Options) ?? new());
            }
            catch (JsonException)
            {
                // 파일 자체가 깨졌을 때만 격리한다. 매 로드마다 조용히 기본값으로
                // 덮어쓰면 손상 원인을 진단할 수 없으므로 원본을 sidecar로 남긴다.
                QuarantineCorruptFile(path);
                return new();
            }
            if (changed)
            {
                try { WriteAtomic(path, migrated); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        "Settings migration could not be saved; loaded settings retained: {0}", error.Message);
                }
            }
            return settings;
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


    public static void Save(AppSettings settings) => SaveEnsuringDirectory(settings, AppPaths.SettingsFile);

    internal static void SaveEnsuringDirectory(AppSettings settings, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Save(settings, path);
    }

    /// <summary>temp 쓰기 후 원자적 이름 교체 — 중간 크래시에도 반쯤 쓰인 설정이 남지 않는다.</summary>
    internal static void Save(AppSettings settings, string path)
        => WriteAtomic(path, JsonSerializer.Serialize(settings, Options));

    private static void WriteAtomic(string path, string json)
    {
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        PlayModes.Normalize(settings);
        if (TelemetryConsentPolicy.IsCurrent(settings)) settings.TelemetryEnabled = true;
        if (settings.LastVisibleOverlayDisplayMode == OverlayDisplayMode.Hidden)
            settings.LastVisibleOverlayDisplayMode = OverlayDisplayMode.Full;
        return settings;
    }
}
