using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

[JsonConverter(typeof(SafeOverlayDisplayModeConverter))]
public enum OverlayDisplayMode
{
    Full,
    StatsOnlyCompact,
    Hidden
}

public sealed class SafeOverlayDisplayModeConverter
    : JsonConverter<OverlayDisplayMode>
{
    public override OverlayDisplayMode Read(ref Utf8JsonReader reader,
        Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<OverlayDisplayMode>(reader.GetString(),
                ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
            return parsed;
        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var value) &&
            Enum.IsDefined(typeof(OverlayDisplayMode), value))
            return (OverlayDisplayMode)value;
        if (reader.TokenType is JsonTokenType.StartObject or
            JsonTokenType.StartArray)
            using (JsonDocument.ParseValue(ref reader)) { }
        return OverlayDisplayMode.Full;
    }

    public override void Write(Utf8JsonWriter writer,
        OverlayDisplayMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
