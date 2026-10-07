using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OrandOverlay;

// Existing three-argument construction remains valid, but unsigned records cannot be installed.
public sealed record UpdateInfo(Version Latest, string Tag, string DownloadUrl, long AssetSize = 0, string? Sha256 = null)
{
    internal string? SignedEnvelope { get; init; }
    public string AssetName { get; init; } = UpdateAssetPolicy.LegacyName;
}

public sealed class UpdateService(Func<string, Task<string>>? fetcher = null,
    Func<string, Task<string?>>? redirectLocator = null, string? updateLogPath = null)
{
    public const string LatestApiUrl = SignedUpdateManifest.ApplicationManifestUrl;
    public const string ReleasesPageUrl = SignedUpdateManifest.Origin;
    public const string DownloadUrlPrefix = SignedUpdateManifest.Origin + "/downloads/";
    public const string CanonicalAssetName = UpdateAssetPolicy.CurrentName;
    public const string LegacyAssetName = UpdateAssetPolicy.LegacyName;
    public const long MaximumAssetSize = SignedUpdateManifest.MaximumApplicationSize;
    private static readonly Regex ShaPattern = new(@"\Asha256:[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);
    private readonly Func<string, Task<string>> _fetch = fetcher ?? (url => UpdateTransport.FetchManifestAsync(url));
    private readonly UpdateTrust? _trust;
    private readonly string _currentIdentity = CurrentBuildVersion;
    private static int _installing;
    internal Func<bool>? AutomaticCommitAllowed { get; set; }
    internal bool InstallDeferred { get; private set; }
    internal bool ServiceCanSelfInstall => AutomaticUpdatesCapable && CanSelfInstall;
    private bool AutomaticUpdatesCapable => UpdateChannelVersion.TryParse(_currentIdentity, out _);
    public static string? SelectedChannel => UpdateChannelVersion.TryParse(CurrentBuildVersion, out var v) ? v.Channel : null;
    public static string? SelectedManifestUrl => UpdateChannelVersion.TryParse(CurrentBuildVersion, out var v) ? v.ManifestUrl : null;
    public static bool SupportsAutomaticUpdates => SelectedChannel is not null;
    public static bool CanAutomaticallyInstall => SupportsAutomaticUpdates && CanSelfInstall;
    internal string UpdateLogPath => updateLogPath ?? Path.Combine(AppPaths.UserDataDirectory, "update.log");
    // Public construction always uses bundled trust. No trust from the network or user cache.
    internal UpdateService(Func<string, Task<string>> fetcher, UpdateTrust trust, string? updateLogPath = null, string? currentIdentity = null)
        : this(fetcher, redirectLocator: null, updateLogPath: updateLogPath)
    { _trust = trust; _currentIdentity = currentIdentity ?? CurrentBuildVersion; }

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public static string CurrentBuildVersion => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
    public static bool IsTestBuild => SelectedChannel == "test";
    public async Task<UpdateInfo?> CheckAsync() => (await CheckDetailedAsync().ConfigureAwait(false)).Update;

    public async Task<(UpdateInfo? Update, bool Failed)> CheckDetailedAsync()
    {
        if (!UpdateChannelVersion.TryParse(_currentIdentity, out var current)) return (null, true);
        try
        {
            var envelope = await _fetch(current.ManifestUrl).ConfigureAwait(false);
            var manifest = VerifyEnvelope(envelope, _trust, current.Channel);
            if (!UpdateChannelVersion.TryParse(manifest.Version, out var offered)) throw new InvalidDataException();
            if (!offered.IsUpgradeFrom(current)) return (null, false);
            var latest = offered.Core;
            return (new UpdateInfo(latest, manifest.Version, manifest.DownloadUrl, manifest.AssetSize, "sha256:" + manifest.Sha256)
                { SignedEnvelope = envelope, AssetName = manifest.AssetName }, false);
        }
        catch { return (null, true); }
    }

    // Legacy parsing entrypoints are deliberately fail-closed and have no runtime fallback.
    public static UpdateInfo? ParseLatest(string json, Version current) => null;
    public static UpdateInfo? ParseRedirectLocation(string? location, Version current) => null;

    private static SignedUpdateManifest VerifyEnvelope(string envelope, UpdateTrust? trust, string channel) => trust is null
        ? SignedUpdateManifest.Verify(envelope, "application", channel)
        : SignedUpdateManifest.Verify(envelope, "application", channel, trust);
    internal static bool IsTrustedUpdate(UpdateInfo update) => IsTrustedUpdate(update, null);
    internal static bool IsTrustedUpdate(UpdateInfo update, UpdateTrust? trust, string? currentIdentity = null)
    {
        try
        {
            if (!UpdateChannelVersion.TryParse(currentIdentity ?? CurrentBuildVersion, out var current) ||
                update.SignedEnvelope is not { Length: > 0 } envelope) return false;
            var manifest = VerifyEnvelope(envelope, trust, current.Channel);
            return UpdateChannelVersion.TryParse(manifest.Version, out var offered) && offered.IsUpgradeFrom(current) &&
                update.Latest == offered.Core && update.Tag == manifest.Version &&
                UpdateAssetPolicy.IsAllowedApplicationName(update.AssetName) && update.AssetName == manifest.AssetName &&
                update.DownloadUrl == manifest.DownloadUrl &&
                update.AssetSize == manifest.AssetSize && update.Sha256 == "sha256:" + manifest.Sha256;
        }
        catch { return false; }
    }
    internal bool ReverifyOffer(UpdateInfo update) => IsTrustedUpdate(update, _trust, _currentIdentity);

    internal static async Task VerifyDownloadedBodyAsync(Stream source, Stream target, long expectedSize, string expectedDigest, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (expectedSize is <= 0 or > MaximumAssetSize || !ShaPattern.IsMatch(expectedDigest)) throw new InvalidDataException("Invalid release metadata.");
        var expectedHash = Convert.FromHexString(expectedDigest["sha256:".Length..]);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (read > expectedSize - copied) throw new InvalidDataException("Asset exceeded declared size.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read); copied += read; progress?.Report((double)copied / expectedSize);
        }
        if (copied != expectedSize || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expectedHash)) throw new InvalidDataException("Asset did not match release metadata.");
    }

    public static bool CanSelfInstall
    {
        get
        {
            var application = typeof(UpdateService).Assembly;
            var entry = Assembly.GetEntryAssembly();
            // Check authority before touching host paths. Assembly.Location is deliberately irrelevant:
            // IncludeAllContentForSelfExtract can give the bundled application a nonempty location.
            if (!ReferenceEquals(entry, application)) return false;
            try
            {
                var processPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(processPath)) return false;
                return UpdateHostPolicy.CanSelfInstall(entry, application, processPath, File.Exists(processPath),
                    File.Exists(Path.Combine(Path.GetDirectoryName(processPath) ?? "", "OrandOverlay.dll")));
            }
            catch { return false; }
        }
    }

    public async Task DownloadAndInstallAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!ReverifyOffer(update)) throw new InvalidDataException("Untrusted release metadata.");
        if (!ServiceCanSelfInstall) throw new InvalidOperationException("This installation cannot self-update.");
        if (System.Threading.Interlocked.CompareExchange(ref _installing, 1, 0) != 0) throw new InvalidOperationException("Update already in progress.");
        try { await DownloadAndCommitAsync(update, progress, cancellationToken).ConfigureAwait(false); }
        catch { System.Threading.Interlocked.Exchange(ref _installing, 0); throw; }
    }

    private async Task DownloadAndCommitAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        InstallDeferred = false;
        RequireCommitAllowed();
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable.");
        var newPath = exePath + ".new";
        try
        {
            using var response = await UpdateTransport.GetAsync(update.DownloadUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength != update.AssetSize) throw new InvalidDataException("Asset length did not match release metadata.");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await VerifyDownloadedBodyAsync(source, target, update.AssetSize, update.Sha256!, progress, cancellationToken).ConfigureAwait(false);
            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            target.Flush(flushToDisk: true);
        }
        catch { TryDelete(newPath); throw; }
        try { cancellationToken.ThrowIfCancellationRequested(); RequireCommitAllowed();
            if (!ReverifyOffer(update)) throw new InvalidDataException("Untrusted release metadata."); }
        catch { TryDelete(newPath); throw; }
        var logPath = UpdateLogPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var script = BuildInstallScript(exePath, newPath, logPath, update.Tag, Environment.ProcessId, update.AssetSize, update.Sha256!, _currentIdentity, update.AssetName);
        try { cancellationToken.ThrowIfCancellationRequested(); RequireCommitAllowed(); }
        catch { TryDelete(newPath); throw; }
        _ = Process.Start(new ProcessStartInfo { FileName = "powershell.exe", Arguments = $"-NoProfile -WindowStyle Hidden -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new IOException("Updater process could not be started.");
    }

    private void RequireCommitAllowed()
    {
        if (AutomaticCommitAllowed is { } allowed && !allowed())
        { InstallDeferred = true; throw new OperationCanceledException("Automatic update deferred or disabled."); }
    }

    // Legacy callers cannot generate an executable installer without authenticated byte metadata.
    internal static string BuildInstallScript(string exePath, string newPath, string logPath, string tag, int processId) =>
        throw new ArgumentException("Verified size and digest are required.");

    internal static string BuildInstallScript(string exePath, string newPath, string logPath, string tag, int processId,
        long expectedSize, string expectedDigest, string? currentIdentity = null, string assetName = LegacyAssetName)
    {
        if (!UpdateChannelVersion.TryParse(tag, out var offered) ||
            !UpdateChannelVersion.TryParse(currentIdentity ?? CurrentBuildVersion, out var current) || !offered.IsUpgradeFrom(current) ||
            processId <= 0 || expectedSize is <= 0 or > MaximumAssetSize || !ShaPattern.IsMatch(expectedDigest) ||
            !UpdateAssetPolicy.IsAllowedApplicationName(assetName))
            throw new ArgumentException("Invalid updater script input.");
        var backup = exePath + ".previous"; var failed = exePath + ".failed";
        return $$"""
function Log($m) { try { Add-Content -LiteralPath '{{Ps(logPath)}}' -Value ((Get-Date).ToString('HH:mm:ss') + ' ' + $m) } catch {} }
Log 'begin {{Ps(tag)}} asset {{Ps(assetName)}}'
$exited = $false
for ($i = 0; $i -lt 120; $i++) { if (-not (Get-Process -Id {{processId}} -ErrorAction SilentlyContinue)) { $exited = $true; break }; Start-Sleep -Milliseconds 500 }
if (-not $exited) { Log 'wait timeout'; exit 1 }
try {
    $staged = [System.IO.File]::Open('{{Ps(newPath)}}', [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        if ($staged.Length -ne {{expectedSize}}) { throw 'staged size mismatch' }
        $hasher = [System.Security.Cryptography.SHA256]::Create()
        try { $actual = [System.BitConverter]::ToString($hasher.ComputeHash($staged)).Replace('-', '').ToLowerInvariant() } finally { $hasher.Dispose() }
        if ($actual -cne '{{expectedDigest[7..].ToLowerInvariant()}}') { throw 'staged hash mismatch' }
    } finally { $staged.Dispose() }
} catch { Log ('staged verification failed: ' + $_.Exception.Message); exit 1 }
try { Remove-Item -LiteralPath '{{Ps(backup)}}' -Force -ErrorAction SilentlyContinue; [System.IO.File]::Replace('{{Ps(newPath)}}', '{{Ps(exePath)}}', '{{Ps(backup)}}', $true); Log 'replaced' }
catch { Log ('replace failed: ' + $_.Exception.Message); exit 1 }
try { Start-Process -FilePath '{{Ps(exePath)}}' -ErrorAction Stop; Log 'started' }
catch { Log ('start failed: ' + $_.Exception.Message); try { if (Test-Path -LiteralPath '{{Ps(backup)}}') { [System.IO.File]::Replace('{{Ps(backup)}}', '{{Ps(exePath)}}', '{{Ps(failed)}}', $true); Log 'rolled back after start failure'; Start-Process -FilePath '{{Ps(exePath)}}' -ErrorAction Stop; Log 'previous version restarted' } } catch { Log ('rollback failed: ' + $_.Exception.Message) } }
""";
    }
    private static string Ps(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    public static void OpenReleasesPage() => Process.Start(new ProcessStartInfo { FileName = ReleasesPageUrl, UseShellExecute = true });
}
