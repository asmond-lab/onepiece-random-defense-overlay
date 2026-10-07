using System.Diagnostics;
using System.Text.Json;
using OrandOverlay;

var output = Path.GetFullPath(args.Length == 1 ? args[0] : throw new ArgumentException("Expected output JSON path."));
var root = Path.Combine(Path.GetTempPath(), "orand-2322-identity-" + Guid.NewGuid().ToString("N"));
var log = Path.Combine(root, "War3Log.txt");
var archive = @"C:\Users\123\Documents\Warcraft III\Maps\Download\ORDR_S2_2.322[R].w3x";
var pin = new MapArchivePin("", 119516025, "93baeb58a6d7ad7e25c7e8a44a345f0c39b5907aa9c1774995730d4fbd101d9b");
var start = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.FromHours(9));
Directory.CreateDirectory(root);
try
{
    File.WriteAllText(log, "9/24 12:00:00.001  GameMain Started\n9/24 12:00:01.000  Opening map - " + archive.Replace('\\', '/') + "\n");
    long outerBytes = 0, innerBytes = 0;
    int outerCalls = 0, innerCalls = 0;
    using var session = new RuntimeMapIdentitySession((inner, bytes) =>
    {
        if (inner) { innerBytes += bytes; innerCalls++; }
        else { outerBytes += bytes; outerCalls++; }
    });
    var samples = new List<object>();
    var allMatch = true;
    for (var i = 0; i < 5; i++)
    {
        var beforeOuterBytes = outerBytes;
        var beforeInnerBytes = innerBytes;
        var beforeOuterCalls = outerCalls;
        var beforeInnerCalls = innerCalls;
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        var result = RuntimeMapIdentityProvider.Probe(start, log, pin,
            session, Environment.ProcessId, start.UtcTicks);
        watch.Stop();
        var actualLogBytes = outerBytes + innerBytes - beforeOuterBytes - beforeInnerBytes;
        var actualLogCalls = outerCalls + innerCalls - beforeOuterCalls - beforeInnerCalls;
        allMatch &= result.LogBytesRead == actualLogBytes && result.LogReadCalls == actualLogCalls &&
            result.ArchiveBytesRead == (i == 0 ? pin.LengthBytes : 0) && result.ArchiveReadCalls == (i == 0 ? 1 : 0);
        samples.Add(new { actualOuterBytes = outerBytes - beforeOuterBytes, actualOuterCalls = outerCalls - beforeOuterCalls,
            actualInnerBytes = innerBytes - beforeInnerBytes, actualInnerCalls = innerCalls - beforeInnerCalls,
            actualLogBytes, actualLogCalls, accountingMatches = result.LogBytesRead == actualLogBytes && result.LogReadCalls == actualLogCalls, call = i == 0 ? "basic-equivalent" : "full-equivalent-" + i, result.State,
            result.Failure, result.LogBytesRead, result.LogReadCalls, result.ArchiveBytesRead,
            result.ArchiveReadCalls, elapsedMs = watch.Elapsed.TotalMilliseconds,
            cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds });
        if (result.State != RuntimeMapIdentityState.Proven) throw new InvalidOperationException(result.ToString());
    }
    session.Dispose();
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, JsonSerializer.Serialize(new { mode = "session-proof", archive, pin, sessionStart = start,
        processId = Environment.ProcessId, logBytes = new FileInfo(log).Length, samples,
        cleanup = "archive/log handles disposed before JSON write; synthetic log directory deleted in finally; process exits after JSON write" },
        new JsonSerializerOptions { WriteIndented = true }));
    if (!allMatch)
        throw new InvalidOperationException("Log I/O accounting differs from observed reads; receipt written.");
}
finally
{
    Directory.Delete(root, true);
}
