using System.Text.Json;
using System.Net.Http;

namespace OrandOverlay;

/// <summary>One explicit authority for the application's external effects. Fixture contexts
/// have no user paths, persistent settings, live reader, cache, or runtime capability.</summary>
internal sealed partial class OverlayExecutionContext
{
    private readonly string? _userRoot;
    private AppSettings? _fixtureSettings;
    private readonly Action<IReadOnlyList<string>> _diagnosticSink;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly CoachJournal? _fixtureJournal;
    private readonly LocalActivityLog? _fixtureActivityLog;
    private readonly string? _executablePath;

    private OverlayExecutionContext(bool runtimeEnabled, string? userRoot, AppSettings? settings,
        Action<IReadOnlyList<string>> diagnosticSink, CoachJournal? fixtureJournal = null, bool syntheticGorosei = false,
        string? replayDirectory = null, LocalActivityLog? fixtureActivityLog = null, string? executablePath = null)
    {
        RuntimeEnabled = runtimeEnabled;
        ReplayDirectory = replayDirectory;
        _syntheticGoroseiAllowed = syntheticGorosei && !runtimeEnabled;
        _userRoot = userRoot;
        _fixtureSettings = settings;
        _diagnosticSink = diagnosticSink;
        _fixtureJournal = fixtureJournal;
        _fixtureActivityLog = fixtureActivityLog;
        _executablePath = executablePath;
    }

    public bool RuntimeEnabled { get; }
    private TelemetryConsentStore ConsentStore => new(Path.Combine(_userRoot!, "settings.json"));
    public bool HasCurrentConsent => RuntimeEnabled && ConsentStore.IsCurrent;
    public bool EnsureConsent(Func<bool?> requestConsent) => RuntimeEnabled && ConsentStore.EnsureConsent(requestConsent);
    public void RequireConsent()
    {
        if (RuntimeEnabled && !HasCurrentConsent)
            throw new InvalidOperationException("Current explicit gameplay telemetry consent is required.");
    }
    public string? CatalogOverride => _userRoot is null ? null : Path.Combine(_userRoot, "game-data.json");
    public string? ClearCacheFile => _userRoot is null ? null : Path.Combine(_userRoot, "tmo-clear-cache.json");

    // Only the normal app composition root evaluates the shared path.
    public static OverlayExecutionContext Production() => Production(AppPaths.UserDataDirectory);
    internal static OverlayExecutionContext Production(string userRoot) =>
        Production(userRoot, Environment.ProcessPath);
    internal static OverlayExecutionContext Production(string userRoot, string? executablePath)
    {
        if (!Path.IsPathFullyQualified(userRoot)) throw new ArgumentException("An absolute owned root is required.", nameof(userRoot));
        return new(true, userRoot, null, lines =>
        {
            Directory.CreateDirectory(userRoot);
            File.AppendAllLines(Path.Combine(userRoot, "unknown-rawcodes.log"), lines);
        }, executablePath: executablePath);
    }

    public static OverlayExecutionContext Fixture(AppSettings settings,
        Action<IReadOnlyList<string>>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(false, null, Clone(settings), diagnostics ?? (_ => { }));
    }

    public static OverlayExecutionContext FixtureWithMemoryJournal(AppSettings settings,
        Action<IReadOnlyList<string>>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(false, null, Clone(settings), diagnostics ?? (_ => { }), CoachJournal.Memory());
    }

    internal static OverlayExecutionContext FixtureWithActivityLog(AppSettings settings, LocalActivityLog log) =>
        new(false, null, Clone(settings), _ => { }, fixtureActivityLog: log);

    internal string? ActivityLogDirectory
    {
        get
        {
            if (!RuntimeEnabled || string.IsNullOrWhiteSpace(_executablePath) ||
                !Path.IsPathFullyQualified(_executablePath))
                return null;
            try
            {
                var directory = Path.GetDirectoryName(_executablePath);
                return string.IsNullOrWhiteSpace(directory)
                    ? null
                    : Path.Combine(directory, "activity-logs");
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }
    }

    internal LocalActivityLog? CreateActivityLog()
    {
        RequireConsent();
        if (!RuntimeEnabled) return _fixtureActivityLog;
        return ActivityLogDirectory is { } directory
            ? new LocalActivityLog(directory, maxFileBytes: 16L * 1024 * 1024, retainedFileCount: 32)
            : null;
    }

    private static AppSettings Clone(AppSettings value) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(value))!;

    public AppSettings LoadSettings()
    {
        RequireConsent();
        return _fixtureSettings is not null
            ? Clone(_fixtureSettings) : SettingsStore.Load(Path.Combine(_userRoot!, "settings.json"));
    }

    public void SaveSettings(AppSettings settings)
    {
        if (_userRoot is null) { _fixtureSettings = Clone(settings); return; }
        RequireConsent();
        TelemetryConsentPolicy.ApplyAgreement(settings);
        SettingsStore.SaveEnsuringDirectory(settings, Path.Combine(_userRoot, "settings.json"));
    }

    public void LogUnknownRawcodes(RecognitionResult result)
    {
        RequireConsent();
        var codes = result.Diagnostics?.UnknownRawcodes;
        if (codes is null || codes.Count == 0) return;
        var fresh = codes.Where(code => _reported.Add(code))
            .Select(code => $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {code}").ToArray();
        if (fresh.Length == 0) return;
        try { _diagnosticSink(fresh); }
        catch { /* Diagnostics never interrupt result application. */ }
    }

    public DataCatalog CreateCatalog() { RequireConsent(); return new(CatalogOverride); }
    public string[] ClearSamplePaths() => ClearCacheFile is { } cache
        ? [Path.Combine(AppContext.BaseDirectory, "Data", "tmo-clear-samples.json"), cache]
        : [Path.Combine(AppContext.BaseDirectory, "Data", "tmo-clear-samples.json")];
    public CoachJournal? CreateCoachJournal()
    {
        RequireConsent();
        return _userRoot is null ? _fixtureJournal : new CoachJournal(Path.Combine(_userRoot, "coach-replays"));
    }
    public TelemetryUploader CreateTelemetry(bool enabled, string? queueOverride = null)
    {
        RequireConsent();
        return new(queueDirectory: RuntimeEnabled ? queueOverride ?? Path.Combine(_userRoot!, "telemetry", "pending") : null,
            enabled: RuntimeEnabled, runtimeEffects: RuntimeEnabled);
    }
    private static readonly Lazy<HttpClient> GameplayHttp = new(() => new HttpClient
    {
        BaseAddress = new Uri("https://orand-telemetry.epic42121.workers.dev"),
        Timeout = TimeSpan.FromSeconds(15)
    });

    public GameplayTelemetryClient? CreateGameplayTelemetry(GameplayTelemetryMetadata metadata,
        DataCatalog catalog, HttpClient? httpClient = null)
    {
        RequireConsent();
        if (!RuntimeEnabled || !MapDatasetRuntimePolicy.AllowsLegacyRuntime(catalog.MapVersion)) return null;
        var http = httpClient ?? GameplayHttp.Value;
        var recorder = new GameplaySessionRecorder(() => HasCurrentConsent, metadata, catalog);
        var outbox = new GameplayTelemetryOutbox(() => HasCurrentConsent, _userRoot!, http,
            new Uri("https://orand-telemetry.epic42121.workers.dev/v3/gameplay"));
        return new GameplayTelemetryClient(() => HasCurrentConsent, recorder, outbox);
    }

    public GameplayRecoveryJournal? CreateGameplayRecoveryJournal(DataCatalog catalog)
    {
        RequireConsent();
        return RuntimeEnabled && MapDatasetRuntimePolicy.AllowsLegacyRuntime(catalog.MapVersion)
            ? new GameplayRecoveryJournal(() => HasCurrentConsent, _userRoot!, catalog) : null;
    }

    public GameplayStatsRefreshService? CreateGameplayStatsRefreshService(HttpClient? httpClient = null)
    {
        RequireConsent();
        if (!RuntimeEnabled) return null;
        return new GameplayStatsRefreshService(httpClient ?? GameplayHttp.Value,
            Path.Combine(_userRoot!, "gameplay-v3", "stats"), () => HasCurrentConsent);
    }

    public IInventoryRecognizer CreateRecognizer(DataCatalog catalog)
    {
        RequireConsent();
        return LiveMemoryEnabled
            ? new WarcraftMemoryRecognitionService(catalog, ReplayDirectory ?? _userRoot!) : new InertRecognizer();
    }

    public UpdateService? CreateUpdateService()
    {
        RequireConsent();
        return RuntimeEnabled ? new UpdateService(updateLogPath: Path.Combine(_userRoot!, "update.log")) : null;
    }
    public ClearSnapshotRefreshService? CreateClearRefreshService()
    {
        RequireConsent();
        return RuntimeEnabled ? new ClearSnapshotRefreshService() : null;
    }
    public Task RefreshProfilesAsync()
    {
        RequireConsent();
        return RuntimeEnabled ? MemoryProfileRefreshService.TryRefreshAsync(_userRoot!) : Task.CompletedTask;
    }

    internal Task RefreshProfilesNowAsync()
    {
        RequireConsent();
        return RuntimeEnabled ? MemoryProfileRefreshService.TryRefreshAsync(_userRoot!, force: true) : Task.CompletedTask;
    }

    // Execute rather than return a capability: callers cannot accidentally retain a live
    // factory result in a fixture. Used for startup IPC/profile refresh and native hotkeys.
    public void RunRuntime(Action action) { RequireConsent(); if (RuntimeEnabled) action(); }
    public bool TryRuntime(Func<bool> action) { RequireConsent(); return RuntimeEnabled && action(); }

    private sealed class InertRecognizer : IInventoryRecognizer
    {
        public Task<RecognitionResult> RecognizeAsync(AppSettings settings, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new RecognitionResult { State = RecognitionState.Waiting, Status = "Controlled result required" });
        }
    }
}
