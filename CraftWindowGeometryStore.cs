using System.Text.Json;

namespace OrandOverlay;

internal sealed partial class OverlayExecutionContext
{
    internal string? CraftWindowGeometryPath => RuntimeEnabled && _userRoot is not null
        ? Path.Combine(_userRoot, "craft-window-vertical-e-v1.json") : null;
}

internal sealed record CraftWindowGeometry(double LeftPixels, double TopPixels, double WidthDip, double HeightDip)
{
    public int SchemaVersion { get; init; } = 1;
    public string Layout { get; init; } = "vertical-e";
    public double DpiScale { get; init; } = 1;

    internal bool IsValid => SchemaVersion == 1 && Layout == "vertical-e" &&
        double.IsFinite(LeftPixels) && Math.Abs(LeftPixels) <= 10_000_000 &&
        double.IsFinite(TopPixels) && Math.Abs(TopPixels) <= 10_000_000 &&
        double.IsFinite(WidthDip) && WidthDip > 0 && WidthDip <= 100_000 &&
        double.IsFinite(HeightDip) && HeightDip > 0 && HeightDip <= 100_000 &&
        double.IsFinite(DpiScale) && DpiScale > 0 && DpiScale <= 16;
}

// Dedicated, versioned storage keeps the old horizontal layout and shared AppSettings separate.
// Creating the store with runtime effects disabled never resolves or touches a user-data path.
internal sealed class CraftWindowGeometryStore
{
    private readonly string _path;
    internal CraftWindowGeometryStore(string path) => _path = path;

    internal static CraftWindowGeometryStore? Create(bool runtimeEffects, string? path = null)
    {
        if (!runtimeEffects) return null;
        return new(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrandOverlay", "craft-window-vertical-e-v1.json"));
    }

    internal CraftWindowGeometry? Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<CraftWindowGeometry>(File.ReadAllText(_path));
            return value is { IsValid: true } ? value : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    internal bool Save(CraftWindowGeometry geometry)
    {
        if (!geometry.IsValid) return false;
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(geometry));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Craft window geometry could not be saved: {0}", error.Message);
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
