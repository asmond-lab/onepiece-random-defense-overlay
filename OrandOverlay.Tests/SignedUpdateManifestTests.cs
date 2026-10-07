using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

internal sealed class TestSigningKey : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    internal UpdateTrust Trust { get; }
    internal TestSigningKey()
    {
        Trust = UpdateTrust.FromJson(JsonSerializer.Serialize(new { schemaVersion = 1, keys = new[] {
            new { keyId = "ephemeral-test", algorithm = SignedUpdateManifest.SignatureAlgorithm,
                publicKeySpki = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()) } } }));
    }
    internal static JsonObject Payload(string kind = "application", byte[]? body = null, string version = "9.9.9")
    {
        body ??= Encoding.UTF8.GetBytes("abc");
        return new JsonObject {
            ["schemaVersion"] = 1, ["kind"] = kind, ["channel"] = "stable", ["version"] = version,
            ["platform"] = "win-x64", ["minimumUpdaterVersion"] = 1, ["publishedAtUtc"] = "2026-09-13T00:00:00Z",
            ["asset"] = new JsonObject {
                ["path"] = kind == "application" ? $"/downloads/{version}/OrandOverlay.exe" : $"/profiles/{version}/memory-profiles.json",
                ["size"] = body.LongLength, ["sha256"] = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant() } };
    }
    internal string Sign(JsonObject payload) => SignText(payload.ToJsonString());
    internal string SignText(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        return JsonSerializer.Serialize(new { schemaVersion = 1, keyId = "ephemeral-test", payload = Convert.ToBase64String(bytes),
            signature = Convert.ToBase64String(_key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) });
    }
    public void Dispose() => _key.Dispose();
}

public sealed class SignedUpdateManifestTests
{
    [Theory]
    [InlineData("application", "/downloads/9.9.9/OrandOverlay.exe")]
    [InlineData("memory-profiles", "/profiles/9.9.9/memory-profiles.json")]
    public void Verify_AcceptsExactSignedContract(string kind, string path)
    {
        using var key = new TestSigningKey();
        var result = SignedUpdateManifest.Verify(key.Sign(TestSigningKey.Payload(kind)), kind, "stable", key.Trust);
        Assert.Equal(path, result.AssetPath);
        Assert.Equal(SignedUpdateManifest.Origin + path, result.DownloadUrl);
        Assert.Equal(3, result.AssetSize);
        Assert.Equal(1, result.MinimumUpdaterVersion);
    }

    [Fact]
    public void Verify_RejectsTamperUnknownKeyAndDifferentSigner()
    {
        using var key = new TestSigningKey(); using var other = new TestSigningKey();
        var signed = key.Sign(TestSigningKey.Payload());
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(signed, "application", "stable", other.Trust));
        var envelope = JsonNode.Parse(signed)!.AsObject();
        envelope["keyId"] = "unknown";
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(envelope.ToJsonString(), "application", "stable", key.Trust));
        envelope["keyId"] = "ephemeral-test";
        envelope["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(TestSigningKey.Payload(version: "9.9.8").ToJsonString()));
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(envelope.ToJsonString(), "application", "stable", key.Trust));
    }

    [Theory]
    [InlineData(0)] [InlineData(63)] [InlineData(65)] [InlineData(72)]
    public void Verify_RejectsNonP1363SignatureSizes(int size)
    {
        using var key = new TestSigningKey(); var envelope = JsonNode.Parse(key.Sign(TestSigningKey.Payload()))!.AsObject();
        envelope["signature"] = Convert.ToBase64String(new byte[size]);
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(envelope.ToJsonString(), "application", "stable", key.Trust));
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("minimumUpdaterVersion", "2")]
    [InlineData("minimumUpdaterVersion", "0")]
    [InlineData("minimumUpdaterVersion", "1.5")]
    [InlineData("platform", "\"linux-x64\"")]
    [InlineData("kind", "\"memory-profiles\"")]
    [InlineData("channel", "\"test\"")]
    [InlineData("version", "\"v9.9.9\"")]
    [InlineData("version", "\"9.9.9-test\"")]
    [InlineData("version", "\"9.9.9/evil\"")]
    [InlineData("publishedAtUtc", "\"not-a-date\"")]
    [InlineData("extra", "true")]
    public void Verify_RejectsUnsupportedOrMalformedPayload(string field, string value)
    {
        using var key = new TestSigningKey(); var payload = TestSigningKey.Payload(); payload[field] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(key.Sign(payload), "application", "stable", key.Trust));
    }

    [Theory]
    [InlineData("path", "\"https://evil.example/OrandOverlay.exe\"")]
    [InlineData("path", "\"/downloads/9.9.9/Other.exe\"")]
    [InlineData("path", "\"/downloads/9.9.9/OrandOverlay.exe?x=1\"")]
    [InlineData("size", "0")] [InlineData("size", "-1")] [InlineData("size", "536870913")]
    [InlineData("sha256", "\"bad\"")]
    public void Verify_RejectsUnsafeAssets(string field, string value)
    {
        using var key = new TestSigningKey(); var payload = TestSigningKey.Payload(); payload["asset"]![field] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(key.Sign(payload), "application", "stable", key.Trust));
    }

    [Fact]
    public void Verify_RejectsEnvelopeSchemaDuplicatesExtraFieldsAndOversize()
    {
        using var key = new TestSigningKey(); var signed = key.Sign(TestSigningKey.Payload());
        var envelope = JsonNode.Parse(signed)!.AsObject(); envelope["schemaVersion"] = 2;
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(envelope.ToJsonString(), "application", "stable", key.Trust));
        envelope["schemaVersion"] = 1; envelope["extra"] = true;
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(envelope.ToJsonString(), "application", "stable", key.Trust));
        var duplicate = signed.Insert(1, "\"schemaVersion\":1,");
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(duplicate, "application", "stable", key.Trust));
        var duplicatePayload = TestSigningKey.Payload().ToJsonString().Insert(1, "\"version\":\"9.9.9\",");
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(key.SignText(duplicatePayload), "application", "stable", key.Trust));
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(signed + new string(' ', SignedUpdateManifest.MaximumManifestSize), "application", "stable", key.Trust));
    }

    [Fact]
    public void Verify_ProfileLimitIsTwoMiB()
    {
        using var key = new TestSigningKey(); var payload = TestSigningKey.Payload("memory-profiles");
        payload["asset"]!["size"] = SignedUpdateManifest.MaximumProfilesSize + 1;
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(key.Sign(payload), "memory-profiles", "stable", key.Trust));
    }

    [Theory]
    [InlineData("https://evil.example/a", false)]
    [InlineData("http://orand-updates.epic42121.workers.dev/a", false)]
    [InlineData("https://orand-updates.epic42121.workers.dev.evil.example/a", false)]
    [InlineData("https://user@orand-updates.epic42121.workers.dev/a", false)]
    [InlineData("https://orand-updates.epic42121.workers.dev:8443/a", false)]
    [InlineData("https://orand-updates.epic42121.workers.dev/a", true)]
    public void Transport_RestrictsEveryRedirectToOrigin(string url, bool expected) => Assert.Equal(expected, UpdateTransport.IsSameOrigin(new Uri(url)));

    [Fact]
    public void Trust_RejectsWrongCurveAndAlgorithm()
    {
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        string TrustJson(string algorithm) => JsonSerializer.Serialize(new { schemaVersion = 1, keys = new[] {
            new { keyId = "bad", algorithm, publicKeySpki = Convert.ToBase64String(wrongCurve.ExportSubjectPublicKeyInfo()) } } });
        Assert.Throws<InvalidDataException>(() => UpdateTrust.FromJson(TrustJson(SignedUpdateManifest.SignatureAlgorithm)));
        Assert.Throws<InvalidDataException>(() => UpdateTrust.FromJson(TrustJson("ECDSA-P384-SHA384")));
    }

    [Fact]
    public void Verify_RejectsWrongRawSignatureAndMalformedBase64()
    {
        using var key = new TestSigningKey();
        var envelope = JsonNode.Parse(key.Sign(TestSigningKey.Payload()))!.AsObject();
        var signature = Convert.FromBase64String(envelope["signature"]!.GetValue<string>()); signature[0] ^= 1;
        envelope["signature"] = Convert.ToBase64String(signature);
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(envelope.ToJsonString(), "application", "stable", key.Trust));
        envelope["payload"] = "not-base64";
        Assert.Throws<InvalidDataException>(() => SignedUpdateManifest.Verify(envelope.ToJsonString(), "application", "stable", key.Trust));
    }
}
