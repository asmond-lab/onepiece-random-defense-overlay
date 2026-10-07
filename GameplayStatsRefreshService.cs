using System.Net.Http;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>Same-cohort cache and public v3 aggregate refresh. Authorization is checked
/// before effects and again before accepting an asynchronous response.</summary>
public sealed class GameplayStatsRefreshService(HttpClient http, string cacheDirectory, Func<bool> authorized)
{
    private readonly SemaphoreSlim _serial = new(1, 1);
    private volatile LiveStats _snapshot = new();
    private volatile LiveStats _bulletSnapshot = new();

    public LiveStats GetSnapshot(string mapScriptSha256, string difficulty)
    {
        var snapshot = _snapshot;
        return authorized() && snapshot.MatchesCohort(mapScriptSha256, difficulty) ? snapshot : new LiveStats();
    }

    public LiveStats GetBulletSnapshot(string mapScriptSha256, string difficulty)
    {
        var snapshot = _bulletSnapshot;
        return authorized() && snapshot.MatchesCohort(mapScriptSha256, difficulty, LiveStats.BulletProfile)
            ? snapshot : new LiveStats();
    }

    public Task<bool> RefreshBulletAsync(string mapScriptSha256, string difficulty,
        CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(mapScriptSha256, difficulty, null, LiveStats.BulletProfile, cancellationToken);

    public Task<bool> RefreshAsync(string mapScriptSha256, string difficulty,
        RecommendationEngine engine, CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(mapScriptSha256, difficulty, engine, null, cancellationToken);

    private async Task<bool> RefreshCoreAsync(string mapScriptSha256, string difficulty,
        RecommendationEngine? engine, string? profile, CancellationToken cancellationToken)
    {
        engine?.SetGameplayCohort(mapScriptSha256, difficulty);
        if (!authorized() || !LiveStats.IsCohort(mapScriptSha256, difficulty)) return false;
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!authorized()) return false;
            var path = Path.Combine(cacheDirectory, mapScriptSha256 + "-" + difficulty +
                (profile is null ? "" : "-" + profile) + ".json");
            // Cache carries its own binding, not merely a filename supplied by the caller.
            if (!(profile is null ? _snapshot : _bulletSnapshot).MatchesCohort(mapScriptSha256, difficulty, profile) && File.Exists(path))
            {
                try
                {
                    using var cached = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
                    var root = cached.RootElement;
                    if (root.EnumerateObject().Count() == (profile is null ? 4 : 5) && root.GetProperty("schemaVersion").GetInt32() == 3 &&
                        (profile is null ? !root.TryGetProperty("profile", out _) : root.GetProperty("profile").GetString() == profile) &&
                        root.GetProperty("mapScriptSha256").GetString() == mapScriptSha256 &&
                        root.GetProperty("difficulty").GetString() == difficulty &&
                        LiveStats.TryParse(root.GetProperty("stats").GetRawText(), mapScriptSha256, difficulty, out var stats, profile) && authorized())
                    {
                        if (profile is null) _snapshot = stats; else _bulletSnapshot = stats;
                        engine?.SetLiveStats(stats);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
                    InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    System.Diagnostics.Trace.TraceWarning("Gameplay cache rejected: {0}", error.GetType().Name);
                }
            }
            if (!authorized()) return false;
            engine?.SetLiveStats(GetSnapshot(mapScriptSha256, difficulty));
            var url = "/v3/live-stats?mapScriptSha256=" + mapScriptSha256 + "&difficulty=" + Uri.EscapeDataString(difficulty) +
                (profile is null ? "" : "&profile=" + profile);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (profile is null && response.Headers.Contains("X-Orand-Stats-Profile")) return false;
            if (profile is not null && (!response.Headers.TryGetValues("X-Orand-Stats-Profile", out var profiles) ||
                !profiles.SequenceEqual([profile]))) return false;
            const int maximum = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximum) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maximum) return false;
                buffer.Write(chunk, 0, read);
            }
            var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            if (!LiveStats.TryParse(json, mapScriptSha256, difficulty, out var fresh, profile) || !authorized()) return false;
            using var document = JsonDocument.Parse(json);
            var envelope = profile is null
                ? JsonSerializer.Serialize(new { schemaVersion = 3, mapScriptSha256, difficulty, stats = document.RootElement })
                : JsonSerializer.Serialize(new { schemaVersion = 3, mapScriptSha256, difficulty, profile, stats = document.RootElement });
            Directory.CreateDirectory(cacheDirectory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, envelope, cancellationToken).ConfigureAwait(false);
                if (!authorized()) return false;
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            if (profile is null) _snapshot = fresh; else _bulletSnapshot = fresh;
            engine?.SetLiveStats(fresh); // Engine rejects responses for a superseded cohort.
            return true;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            System.Diagnostics.Trace.TraceWarning("Gameplay refresh unavailable: {0}", error.GetType().Name);
            return false;
        }
        finally { _serial.Release(); }
    }
}
