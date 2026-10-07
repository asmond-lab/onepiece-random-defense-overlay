using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryWireTests
{
    internal static ObservedTelemetryPacket Packet(ObservedTelemetryEvent? value = null) => new()
    {
        PacketId = Guid.NewGuid().ToString("N"), SessionId = Guid.NewGuid().ToString("N"),
        AppVersion = "1.0.2-test.9", DatasetFingerprint = new string('a', 64), Source = "synthetic-validation",
        Events = [value ?? new() { Sequence = 1, Kind = "recognition", State = "expired", AgeMs = 3000, Lane = "presentation" }]
    };

    [Theory]
    [InlineData("playerName", "tester")]
    [InlineData("nickname", "tester")]
    [InlineData("path", "private")]
    [InlineData("address", "1234")]
    [InlineData("processId", "123")]
    [InlineData("reason", "private text")]
    public void UnknownPacketFieldsNeverEnterTheWire(string field, string value)
    {
        var packet = JsonSerializer.Serialize(Packet(), ObservedTelemetryWire.JsonOptions);
        var changed = packet.Insert(1, JsonSerializer.Serialize(field) + ":" + JsonSerializer.Serialize(value) + ",");
        Assert.Throws<JsonException>(() => ObservedTelemetryWire.Deserialize(Encoding.UTF8.GetBytes(changed)));
    }

    [Theory]
    [InlineData("sessionId")]
    [InlineData("schemaVersion")]
    [InlineData("source")]
    public void DuplicatePacketFieldsAreRejected(string field)
    {
        var packet = JsonSerializer.Serialize(Packet(), ObservedTelemetryWire.JsonOptions);
        using var doc = JsonDocument.Parse(packet);
        var changed = packet.Insert(1, JsonSerializer.Serialize(field) + ":" + doc.RootElement.GetProperty(field).GetRawText() + ",");
        Assert.Throws<JsonException>(() => ObservedTelemetryWire.Deserialize(Encoding.UTF8.GetBytes(changed)));
    }

    [Fact]
    public void OnlyFreshInventoryMayCarryUnitCounts()
    {
        var counts = ImmutableDictionary<string, int>.Empty.Add("luffy", 2);
        Assert.False(ObservedTelemetryWire.IsValidEvent(new() { Sequence = 1, Kind = "recognition", State = "fresh", Inventory = counts }));
        Assert.False(ObservedTelemetryWire.IsValidEvent(new() { Sequence = 1, Kind = "inventory", State = "expired", Inventory = counts }));
        Assert.False(ObservedTelemetryWire.IsValidEvent(new() { Sequence = 1, Kind = "inventory", State = "fresh" }));
        Assert.True(ObservedTelemetryWire.IsValidEvent(new() { Sequence = 1, Kind = "inventory", State = "fresh", Inventory = counts }));
    }

    [Fact]
    public void UnknownRoundIsOmittedAndNeverSentAsZero()
    {
        var bytes = ObservedTelemetryWire.Serialize(Packet());
        using var document = JsonDocument.Parse(bytes);
        Assert.False(document.RootElement.GetProperty("events")[0].TryGetProperty("round", out _));
        Assert.False(ObservedTelemetryWire.IsValidEvent(Packet().Events[0] with { Round = 0 }));
    }

    [Theory]
    [InlineData("C:/private/file")]
    [InlineData("private reason with spaces")]
    [InlineData("0x12345678")]
    public void DiagnosticReasonAcceptsOnlyExplicitSafeCodes(string reason)
    {
        Assert.False(ObservedTelemetryWire.IsValidEvent(Packet().Events[0] with { ReasonCode = reason }));
    }

    [Fact]
    public void UnitsCannotIncludePathsOrInvalidQuantities()
    {
        var value = new ObservedTelemetryEvent { Sequence = 1, Kind = "inventory", State = "fresh" };
        Assert.False(ObservedTelemetryWire.IsValidEvent(value with { Inventory = ImmutableDictionary<string, int>.Empty.Add("C:/private", 1) }));
        Assert.False(ObservedTelemetryWire.IsValidEvent(value with { Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy", 0) }));
        Assert.False(ObservedTelemetryWire.IsValidEvent(value with { Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy", 10001) }));
    }

    [Fact]
    public void SourceAndFrozenMetadataCannotBeRelabelled()
    {
        Assert.Throws<ArgumentException>(() => ObservedTelemetryWire.Serialize(Packet() with { MapVersion = "2.314" }));
        Assert.Throws<ArgumentException>(() => ObservedTelemetryWire.Serialize(Packet() with { Source = "unknown" }));
        Assert.Throws<ArgumentException>(() => ObservedTelemetryWire.Serialize(Packet() with { GameVersion = "2.0.4" }));
        Assert.Throws<ArgumentException>(() => ObservedTelemetryWire.Serialize(Packet() with { DatasetFingerprint = "unverified" }));
        Assert.Throws<ArgumentException>(() => ObservedTelemetryWire.Serialize(Packet() with { ConsentVersion = 3 }));
    }

    [Fact]
    public void MissingWireFieldsAndNullOptionalFieldsAreRejected()
    {
        var text = Encoding.UTF8.GetString(ObservedTelemetryWire.Serialize(Packet()));
        Assert.Throws<JsonException>(() => ObservedTelemetryWire.Deserialize(Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":4,", ""))));
        var withNull = text.Replace("\"ageMs\":3000", "\"ageMs\":null");
        Assert.Throws<JsonException>(() => ObservedTelemetryWire.Deserialize(Encoding.UTF8.GetBytes(withNull)));
    }
}
