using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrandOverlay;

/// <summary>Wire contract shared with the offline publisher. Sign the exact UTF-8 payload bytes,
/// using ECDSA P-256 / SHA-256 and IEEE P1363 (64-byte r || s), never DER signatures.</summary>
public sealed record SignedUpdateManifest(string Kind, string Channel, string Version, string Platform,
    int MinimumUpdaterVersion, DateTimeOffset PublishedAtUtc, string AssetPath, long AssetSize, string Sha256)
{
    public const int SchemaVersion = 1;
    public const int UpdaterVersion = 1;
    public const string SignatureAlgorithm = "ECDSA-P256-SHA256-P1363";
    public const string Origin = "https://orand-updates.epic42121.workers.dev";
    public const string ApplicationManifestUrl = Origin + "/v1/channels/stable/win-x64";
    public const string TestApplicationManifestUrl = Origin + "/v1/channels/test/win-x64";
    public const string ProfilesManifestUrl = Origin + "/v1/profiles/win-x64";
    public const int MaximumManifestSize = 64 * 1024;
    public const long MaximumApplicationSize = 512L * 1024 * 1024;
    public const int MaximumProfilesSize = 2 * 1024 * 1024;
    private static readonly Regex VersionPattern = new(@"\A(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){1,3}\z", RegexOptions.CultureInvariant);
    private static readonly Regex HashPattern = new(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Lazy<UpdateTrust> BundledTrust = new(() =>
    {
        // Trust belongs to the application bundle, never to the writable user cache or server.
        using var embedded = typeof(SignedUpdateManifest).Assembly.GetManifestResourceStream("OrandOverlay.UpdateTrust.json");
        if (embedded is null) throw new InvalidDataException("Bundled update trust resource is missing.");
        using var reader = new StreamReader(embedded, StrictUtf8);
        return UpdateTrust.FromJson(reader.ReadToEnd());
    });

    public string DownloadUrl => Origin + AssetPath;
    // Derived from the signed canonical path, not supplied by an unsigned response field.
    public string AssetName => AssetPath[(AssetPath.LastIndexOf('/') + 1)..];
    public static SignedUpdateManifest Verify(string envelope, string expectedKind, string expectedChannel = "stable") =>
        Verify(envelope, expectedKind, expectedChannel, BundledTrust.Value);

    internal static SignedUpdateManifest Verify(string envelope, string expectedKind, string expectedChannel, UpdateTrust trust)
    {
        try
        {
            if (StrictUtf8.GetByteCount(envelope) > MaximumManifestSize) throw Invalid();
            using var outer = JsonDocument.Parse(envelope);
            var e = outer.RootElement;
            RequireFields(e, "schemaVersion", "keyId", "payload", "signature");
            if (e.GetProperty("schemaVersion").GetInt32() != SchemaVersion) throw Invalid();
            var keyId = Text(e, "keyId");
            var payload = Decode(Text(e, "payload"));
            var signature = Decode(Text(e, "signature"));
            if (payload.Length == 0 || signature.Length != 64) throw Invalid();
            using var key = trust.OpenKey(keyId);
            if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw Invalid();
            using var inner = JsonDocument.Parse(StrictUtf8.GetString(payload));
            var p = inner.RootElement;
            RequireFields(p, "schemaVersion", "kind", "channel", "version", "platform", "minimumUpdaterVersion", "publishedAtUtc", "asset");
            var kind = Text(p, "kind"); var channel = Text(p, "channel"); var version = Text(p, "version");
            var minimum = p.GetProperty("minimumUpdaterVersion").GetInt32();
            var platform = Text(p, "platform");
            if (p.GetProperty("schemaVersion").GetInt32() != SchemaVersion ||
                kind is not ("application" or "memory-profiles") || kind != expectedKind ||
                channel is not ("stable" or "test") || channel != expectedChannel ||
                !IsArtifactVersion(kind, channel, version) || platform != "win-x64" || minimum < 1 || minimum > UpdaterVersion) throw Invalid();
            var publishedText = Text(p, "publishedAtUtc");
            if (!publishedText.EndsWith("Z", StringComparison.Ordinal) || !publishedText.Contains('T') ||
                !DateTimeOffset.TryParseExact(publishedText, new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var published) || published.Offset != TimeSpan.Zero) throw Invalid();
            var asset = p.GetProperty("asset"); RequireFields(asset, "path", "size", "sha256");
            var assetPath = Text(asset, "path"); var size = asset.GetProperty("size").GetInt64(); var sha256 = Text(asset, "sha256");
            var validPath = kind == "application"
                ? UpdateAssetPolicy.TryGetApplicationName(version, assetPath, out _)
                : assetPath == $"/profiles/{version}/memory-profiles.json";
            var maximum = kind == "application" ? MaximumApplicationSize : MaximumProfilesSize;
            if (!validPath || size <= 0 || size > maximum || !HashPattern.IsMatch(sha256)) throw Invalid();
            return new(kind, channel, version, platform, minimum, published, assetPath, size, sha256.ToLowerInvariant());
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or CryptographicException or ArgumentException or OverflowException)
        { throw new InvalidDataException("Invalid signed update manifest.", ex); }
    }

    private static bool IsArtifactVersion(string kind, string channel, string version)
    {
        if (kind == "memory-profiles") return channel == "stable" && TryVersion(version, out _);
        return UpdateChannelVersion.TryParse(version, out var parsed) && parsed.Channel == channel;
    }

    internal static bool TryVersion(string? text, out System.Version version)
    {
        version = new System.Version();
        if (text is not { Length: > 0 and <= 64 } || !VersionPattern.IsMatch(text) || !System.Version.TryParse(text, out var parsed)) return false;
        version = Normalize(parsed); return true;
    }
    internal static System.Version Normalize(System.Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
    internal static bool IsSha256(string value) => HashPattern.IsMatch(value);
    internal static InvalidDataException Invalid() => new("Invalid signed update manifest.");
    internal static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw Invalid();
    internal static byte[] Decode(string text)
    {
        var bytes = Convert.FromBase64String(text);
        if (Convert.ToBase64String(bytes) != text) throw Invalid();
        return bytes;
    }
    internal static void RequireFields(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var remaining = new HashSet<string>(fields, StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject()) if (!remaining.Remove(field.Name)) throw Invalid();
        if (remaining.Count != 0) throw Invalid();
    }
}

/// <summary>Immutable public-only trust store. Tests inject ephemeral keys through this internal API.</summary>
internal sealed class UpdateTrust
{
    private readonly Dictionary<string, byte[]> _keys;
    private UpdateTrust(Dictionary<string, byte[]> keys) => _keys = keys;
    internal static UpdateTrust FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement; SignedUpdateManifest.RequireFields(root, "schemaVersion", "keys");
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw SignedUpdateManifest.Invalid();
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in root.GetProperty("keys").EnumerateArray())
        {
            SignedUpdateManifest.RequireFields(entry, "keyId", "algorithm", "publicKeySpki");
            var id = SignedUpdateManifest.Text(entry, "keyId");
            if (id.Length is < 1 or > 128 || SignedUpdateManifest.Text(entry, "algorithm") != SignedUpdateManifest.SignatureAlgorithm) throw SignedUpdateManifest.Invalid();
            var spki = SignedUpdateManifest.Decode(SignedUpdateManifest.Text(entry, "publicKeySpki"));
            if (spki.Length is < 1 or > 1024 || !keys.TryAdd(id, spki)) throw SignedUpdateManifest.Invalid();
            using var key = Open(spki);
        }
        if (keys.Count == 0) throw SignedUpdateManifest.Invalid();
        return new(keys);
    }
    internal ECDsa OpenKey(string id) => _keys.TryGetValue(id, out var key) ? Open(key) : throw SignedUpdateManifest.Invalid();
    private static ECDsa Open(byte[] spki)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(spki, out var consumed);
            if (consumed != spki.Length || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7") throw SignedUpdateManifest.Invalid();
            return key;
        }
        catch { key.Dispose(); throw; }
    }
}

/// <summary>One origin, bounded streaming and no automatic redirects for every update request.</summary>
internal static class UpdateTransport
{
    private static readonly HttpClient Client = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OrandOverlay-Updater/1");
        return client;
    }
    internal static bool IsSameOrigin(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https" &&
        uri.Host.Equals(new Uri(SignedUpdateManifest.Origin).Host, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
    internal static async Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken)
    {
        var current = new Uri(url, UriKind.Absolute);
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!IsSameOrigin(current)) throw new InvalidDataException("Update URL is outside the trusted origin.");
            var response = await Client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is < 300 or > 399)
            {
                try { response.EnsureSuccessStatusCode(); return response; }
                catch { response.Dispose(); throw; }
            }
            var next = response.Headers.Location is { } location ? new Uri(current, location) : null;
            response.Dispose();
            if (next is null || !IsSameOrigin(next)) throw new InvalidDataException("Update redirected outside the trusted origin.");
            current = next;
        }
        throw new InvalidDataException("Update exceeded redirect limit.");
    }
    internal static async Task<byte[]> ReadBoundedAsync(string url, int maximumSize, CancellationToken cancellationToken)
    {
        using var response = await GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is { } length && (length <= 0 || length > maximumSize)) throw new InvalidDataException("Update response is too large or empty.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var target = new MemoryStream(); var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumSize - (int)target.Length + 1)), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (read > maximumSize - target.Length) throw new InvalidDataException("Update response exceeded its size limit.");
            target.Write(buffer, 0, read);
        }
        return target.ToArray();
    }
    internal static async Task<string> FetchManifestAsync(string url, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); linked.CancelAfter(TimeSpan.FromSeconds(15));
        return SignedUpdateManifest.StrictUtf8.GetString(await ReadBoundedAsync(url, SignedUpdateManifest.MaximumManifestSize, linked.Token).ConfigureAwait(false));
    }
}
