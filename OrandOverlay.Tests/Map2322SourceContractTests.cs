using System.Security.Cryptography;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322SourceContractTests
{
    [Fact]
    public void ExtractedSourceAndArchiveReceiptMatchContract()
    {
        Assert.Equal("2.322", Map2322SourceContract.MapVersion);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var extracted = Path.Combine(root, "artifacts", "ordr-2322", "map-extracted");
        var script = File.ReadAllBytes(Path.Combine(extracted, "war3map.j"));
        Assert.Equal(Map2322SourceContract.JassSha256, Convert.ToHexString(SHA256.HashData(script)).ToLowerInvariant());
        using var receipt = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(extracted, "extraction-manifest.json")));
        Assert.Equal(Map2322SourceContract.ArchiveSha256, receipt.RootElement.GetProperty("archiveSha256").GetString());
        Assert.Equal(Map2322SourceContract.ArchiveLengthBytes, receipt.RootElement.GetProperty("archiveBytes").GetInt64());
        Assert.Equal(script.LongLength, receipt.RootElement.GetProperty("members")[0].GetProperty("bytes").GetInt64());
    }
}
