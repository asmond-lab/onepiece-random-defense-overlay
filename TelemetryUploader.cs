using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>
/// 식별자 없는 v2 집계만 큐에 저장한다. 서버가 OPTIONS로 schema v2를 명시적으로
/// 승인하기 전에는 payload를 전송하지 않으며, 전송 실패는 추천 경로로 전파하지 않는다.
/// </summary>
public sealed class TelemetryUploader
{
    public const string DefaultEndpoint =
        "https://orand-telemetry.epic42121.workers.dev/v2/aggregates";
    public const string SchemaHeader = "X-Orand-Telemetry-Schema";
    public const string AcceptedHeader = "X-Orand-Telemetry-Accepted";
    public const string EnabledHeader = "X-Orand-Telemetry-Enabled";
    private const int MaxQueued = 50;
    private const int MaxAgeDays = 30;
    private static readonly HttpClient SharedHttp = CreateClient();

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _queueDirectory;

    public TelemetryUploader(string? endpoint = null, string? queueDirectory = null,
        HttpClient? httpClient = null, bool enabled = true)
    {
        _endpoint = endpoint ?? DefaultEndpoint;
        _queueDirectory = queueDirectory
                          ?? Path.Combine(AppPaths.UserDataDirectory, "telemetry", "pending");
        _http = httpClient ?? SharedHttp;
        Enabled = enabled;
        try
        {
            Directory.CreateDirectory(_queueDirectory);
            DiscardLegacyAndUnsafeFiles();
            if (!Enabled) DeletePending();
        }
        catch { /* fail-closed */ }
    }

    public bool Enabled { get; private set; }

    public int PendingCount
    {
        get
        {
            try { return Directory.GetFiles(_queueDirectory, "*.v2.json").Length; }
            catch { return 0; }
        }
    }

    public void SetEnabled(bool enabled, bool deletePending = false)
    {
        Enabled = enabled;
        if (deletePending) DeletePending();
    }

    public void Enqueue(TelemetryRecord record)
    {
        if (!Enabled || !TelemetryPrivacyContract.IsSafe(record)) return;
        try
        {
            var name = $"{Guid.NewGuid():N}.v2.json";
            var path = Path.Combine(_queueDirectory, name);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(record));
            File.Move(temp, path);
            TrimQueue();
        }
        catch { /* fail-closed */ }
    }


    public async Task FlushPendingAsync()
    {
        if (!Enabled) return;
        try
        {
            DiscardLegacyAndUnsafeFiles();
            if (!await EndpointAcceptsV2Async()) return;
            foreach (var path in Directory.GetFiles(_queueDirectory, "*.v2.json")
                         .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                if (!TryReadSafePayload(path, out var payload))
                {
                    Delete(path);
                    continue;
                }

                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try { response = await _http.PostAsync(_endpoint, content); }
                catch { return; }
                using (response)
                {
                    if (RemoteDisabled(response)) return;
                    if (response.IsSuccessStatusCode && ResponseAcceptsV2(response))
                    {
                        Delete(path);
                        continue;
                    }
                    if ((int)response.StatusCode is 400 or 404 or 409 or 410 or 422)
                    {
                        Delete(path);
                        continue;
                    }
                    return;
                }
            }
        }
        catch { /* fail-closed */ }
    }

    public void DeletePending()
    {
        try
        {
            foreach (var path in Directory.GetFiles(_queueDirectory)) Delete(path);
        }
        catch { /* fail-closed */ }
    }

    public void TrimQueue()
    {
        try
        {
            DiscardLegacyAndUnsafeFiles();
            var files = Directory.GetFiles(_queueDirectory, "*.v2.json")
                .OrderBy(File.GetCreationTimeUtc).ToList();
            foreach (var path in files.Where(path =>
                         DateTime.UtcNow - File.GetCreationTimeUtc(path) >
                         TimeSpan.FromDays(MaxAgeDays)))
                Delete(path);
            files = Directory.GetFiles(_queueDirectory, "*.v2.json")
                .OrderBy(File.GetCreationTimeUtc).ToList();
            foreach (var path in files.Take(Math.Max(0, files.Count - MaxQueued)))
                Delete(path);
        }
        catch { /* fail-closed */ }
    }

    private async Task<bool> EndpointAcceptsV2Async()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, _endpoint);
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request); }
        catch { return false; }
        using (response)
            return response.IsSuccessStatusCode &&
                   !RemoteDisabled(response) &&
                   ResponseAcceptsV2(response);
    }

    private static bool ResponseAcceptsV2(HttpResponseMessage response) =>
        HeaderEquals(response, SchemaHeader, "2") &&
        HeaderEquals(response, AcceptedHeader, "true");

    private static bool RemoteDisabled(HttpResponseMessage response) =>
        HeaderEquals(response, EnabledHeader, "false");

    private static bool HeaderEquals(HttpResponseMessage response,
        string name, string expected) =>
        response.Headers.TryGetValues(name, out var values) &&
        values.Any(value => value.Equals(expected, StringComparison.OrdinalIgnoreCase));

    private bool TryReadSafePayload(string path, out string payload)
    {
        payload = "";
        try
        {
            payload = File.ReadAllText(path);
            var record = JsonSerializer.Deserialize<TelemetryRecord>(payload);
            return record is { SchemaVersion: 2 } &&
                   TelemetryPrivacyContract.IsSafeJson(payload);
        }
        catch { return false; }
    }

    private void DiscardLegacyAndUnsafeFiles()
    {
        foreach (var path in Directory.GetFiles(_queueDirectory, "*.json"))
            if (!path.EndsWith(".v2.json", StringComparison.OrdinalIgnoreCase) ||
                !TryReadSafePayload(path, out _))
                Delete(path);
        foreach (var path in Directory.GetFiles(_queueDirectory, "*.tmp")) Delete(path);
        foreach (var path in Directory.GetFiles(_queueDirectory, "*.retry")) Delete(path);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OrandOverlay/2.0");
        return client;
    }

    private static void Delete(string path)
    {
        try { File.Delete(path); } catch { /* fail-closed */ }
        try { File.Delete(path + ".retry"); } catch { /* fail-closed */ }
    }
}
