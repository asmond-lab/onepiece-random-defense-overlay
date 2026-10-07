using System.Text.Json;

namespace OrandOverlay;

/// <summary>Refreshes authenticated profile bytes independently of application updates.
/// Any network/signature/content failure preserves the existing cache and bundled profiles.</summary>
public static class MemoryProfileRefreshService
{
    public const string ProfilesUrl = SignedUpdateManifest.ProfilesManifestUrl;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    public static Task TryRefreshAsync(CancellationToken cancellationToken = default) =>
        TryRefreshAsync(AppPaths.UserDataDirectory, cancellationToken);

    internal static async Task TryRefreshAsync(string userRoot, CancellationToken cancellationToken = default,
        Func<CancellationToken, Task<string>>? fetch = null,
        Func<string, CancellationToken, Task<byte[]>>? fetchAsset = null, UpdateTrust? trust = null, bool force = false)
    {
        var userProfilePath = Path.Combine(userRoot, "memory-profiles.json");
        var stampPath = Path.Combine(userRoot, "memory-profiles.cloudflare.last-check");
        var manifestPath = Path.Combine(userRoot, "memory-profiles.signed.json");
        var temp = userProfilePath + ".tmp";
        try { await RefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        try
        {
            if (!force && !ShouldRefresh(stampPath)) return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(RequestTimeout);
            // Injected fetches still supply a signed envelope and authenticated artifact bytes.
            var envelope = fetch is null
                ? await UpdateTransport.FetchManifestAsync(ProfilesUrl, linked.Token).ConfigureAwait(false)
                : await fetch(linked.Token).ConfigureAwait(false);
            var manifest = trust is null ? SignedUpdateManifest.Verify(envelope, "memory-profiles")
                : SignedUpdateManifest.Verify(envelope, "memory-profiles", "stable", trust);
            if (File.Exists(manifestPath) && new FileInfo(manifestPath).Length <= SignedUpdateManifest.MaximumManifestSize)
            {
                SignedUpdateManifest? prior = null;
                try
                {
                    var previousEnvelope = await File.ReadAllTextAsync(manifestPath, linked.Token).ConfigureAwait(false);
                    prior = trust is null ? SignedUpdateManifest.Verify(previousEnvelope, "memory-profiles")
                        : SignedUpdateManifest.Verify(previousEnvelope, "memory-profiles", "stable", trust);
                }
                catch (InvalidDataException) { /* A valid new signature may repair corrupt local metadata. */ }
                if (prior is not null)
                {
                    SignedUpdateManifest.TryVersion(prior.Version, out var previousVersion);
                    SignedUpdateManifest.TryVersion(manifest.Version, out var nextVersion);
                    if (nextVersion < previousVersion || nextVersion == previousVersion && manifest.Sha256 != prior.Sha256) return;
                }
            }
            var bytes = fetchAsset is null
                ? await UpdateTransport.ReadBoundedAsync(manifest.DownloadUrl, (int)manifest.AssetSize, linked.Token).ConfigureAwait(false)
                : await fetchAsset(manifest.DownloadUrl, linked.Token).ConfigureAwait(false);
            using var source = new MemoryStream(bytes, writable: false);
            using var verified = new MemoryStream();
            await UpdateService.VerifyDownloadedBodyAsync(source, verified, manifest.AssetSize, "sha256:" + manifest.Sha256,
                cancellationToken: linked.Token).ConfigureAwait(false);
            var json = SignedUpdateManifest.StrictUtf8.GetString(bytes);
            if (!TryValidate(json)) return;
            linked.Token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(userRoot);
            // Advance the authenticated rollback floor before changing active profile bytes.
            // If the later write fails, the same signed version can be retried without accepting an older one.
            await File.WriteAllTextAsync(manifestPath + ".tmp", envelope, linked.Token).ConfigureAwait(false);
            File.Move(manifestPath + ".tmp", manifestPath, overwrite: true);
            await File.WriteAllBytesAsync(temp, bytes, linked.Token).ConfigureAwait(false);
            File.Move(temp, userProfilePath, overwrite: true);
            File.WriteAllText(stampPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // No unsigned runtime fallback, and failed refreshes do not advance the check stamp.
        }
        finally
        {
            try { File.Delete(temp); File.Delete(manifestPath + ".tmp"); } catch { }
            RefreshGate.Release();
        }
    }

    private static bool ShouldRefresh(string stampPath)
    {
        try
        {
            if (!File.Exists(stampPath)) return true;
            if (!DateTimeOffset.TryParse(File.ReadAllText(stampPath), out var checkedAt)) return true;
            return DateTimeOffset.UtcNow - checkedAt >= RefreshInterval;
        }
        catch
        {
            return true;
        }
    }

    internal static bool TryValidate(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            var profiles = JsonSerializer.Deserialize<List<MemoryProfile>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
            if (profiles is null || profiles.Count == 0) return false;
            if (profiles.GroupBy(x => $"{x.FileVersion}|{x.ModuleName}", StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1)) return false;
            return profiles.All(profile => MemoryProfileValidator.Validate(profile).Count == 0 &&
                                           profile.Enabled && profile.Verified);
        }
        catch
        {
            return false;
        }
    }
}
