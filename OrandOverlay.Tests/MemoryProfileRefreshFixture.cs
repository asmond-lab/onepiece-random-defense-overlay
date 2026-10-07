using System.Text;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

internal static class MemoryProfileRefreshFixture
{
    internal static byte[] ReadBundledBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "memory-profiles.json"));

    internal static byte[] ReadVerifiedLegacyBytes()
    {
        using var document = JsonDocument.Parse(ReadBundledBytes());
        var legacy = Assert.Single(document.RootElement.EnumerateArray(),
            profile => profile.GetProperty("profileId").GetString() == "war3-2.0.4.23745");
        Assert.True(legacy.GetProperty("enabled").GetBoolean());
        Assert.True(legacy.GetProperty("verified").GetBoolean());
        return Encoding.UTF8.GetBytes("[" + legacy.GetRawText() + "]");
    }
}
