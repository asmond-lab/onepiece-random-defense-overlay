using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrandOverlay;

var options = ReadOptions(args);
try
{
    var manifest = MapSourceManifestVerifier.VerifyDirectory(options.Root);
    var signals = RuntimeSignalFeasibilityProfileLoader.LoadFromDirectory(
        Path.Combine(options.Root, "Data"));
    var manifestPath = Path.Combine(options.Root, "Data", MapSourceManifestVerifier.FileName);
    RuntimeMapIdentityResult? liveIdentity = null;
    int? warcraftPid = null;
    DateTimeOffset? warcraftStartedAt = null;
    if (options.WarcraftPid is int pid && options.War3LogPath is { } logPath)
    {
        using var process = Process.GetProcessById(pid);
        if (!process.ProcessName.Equals("Warcraft III", StringComparison.Ordinal))
            throw new ArgumentException("The supplied PID is not Warcraft III.");
        warcraftPid = process.Id;
        warcraftStartedAt = new DateTimeOffset(process.StartTime);
        var source = MapStoryProfileLoader.LoadFromDirectory(
            Path.Combine(options.Root, "Data"));
        liveIdentity = RuntimeMapIdentityProvider.Probe(
            warcraftStartedAt.Value, logPath, source.Source.Archive);
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Valid = true,
        manifest.MapVersion,
        ManifestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))).ToLowerInvariant(),
        Members = manifest.Members.Select(member => new { member.Identifier, member.Path, member.Sha256 }),
        signals.AdaptivePlanningCapable,
        signals.LiveReadinessRequiresCurrentMapIdentityProof,
        WarcraftPid = warcraftPid,
        WarcraftStartedAt = warcraftStartedAt,
        LiveMapIdentity = liveIdentity,
        CurrentSessionReady = liveIdentity is not null &&
            RuntimeAdaptivePlanningReadiness.IsReady(signals, liveIdentity),
        MandatorySignals = signals.MandatorySignals.Select(signal => new
        {
            signal.Identifier,
            Status = signal.Status.ToString(),
            Disposition = signal.Disposition.ToString()
        })
    }, new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    }));
    return liveIdentity is null || liveIdentity.State == RuntimeMapIdentityState.Proven
        ? 0
        : 3;
}
catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        Valid = false,
        Error = exception.Message
    }));
    return 2;
}

static Options ReadOptions(string[] arguments)
{
    var root = Directory.GetCurrentDirectory();
    int? pid = null;
    string? logPath = null;
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length)
            throw new ArgumentException("Every verifier option requires a value.");
        switch (arguments[index])
        {
            case "--root": root = Path.GetFullPath(arguments[index + 1]); break;
            case "--warcraft-pid" when int.TryParse(arguments[index + 1], out var value):
                pid = value;
                break;
            case "--war3-log": logPath = Path.GetFullPath(arguments[index + 1]); break;
            default: throw new ArgumentException("Unknown verifier option.");
        }
    }
    if ((pid is null) != (logPath is null))
        throw new ArgumentException(
            "Live verification requires both --warcraft-pid and --war3-log.");
    return new Options(root, pid, logPath);
}

internal sealed record Options(string Root, int? WarcraftPid, string? War3LogPath);
