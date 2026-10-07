using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private const string Usage = """
ActivitySoakCapture

Required:
  --app-pid <positive integer>
  --app-start-utc <ISO-8601 UTC timestamp>
  --game-pid <positive integer>
  --game-start-utc <ISO-8601 UTC timestamp>
  --log-dir <EXE-adjacent activity-logs directory>
  --duration <TimeSpan, e.g. 00:30:00>
  --output <new evidence.json path>

Other:
  --smoke <new evidence.json path>
  --help

PASS requires the app to survive the bounded interval, at least two valid new
JSONL records, no malformed complete lines, no receipt gap over 10 seconds
(including interval boundaries), and no writer loss/error indicator.
Game availability is reported separately. A paused map is not active-game
performance evidence.
""";

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--help"])
            {
                Console.WriteLine(Usage);
                return 0;
            }
            if (args is ["--smoke", var smokeOutput])
                return await SmokeRunner.RunAsync(Path.GetFullPath(smokeOutput));
            var options = Parse(args);
            var report = await RunAsync(options);
            WriteNew(options.OutputPath, report);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                report.Decision.Pass,
                report.Decision.AppSurvived,
                report.Decision.ContinuingValidRecording,
                report.Decision.NoWriterLossOrError,
                report.Decision.GameAvailableAtEnd,
                report.ActivityLog.MaximumReceiptGapSeconds,
                output = options.OutputPath
            }, Json));
            return report.Decision.Pass ? 0 : 1;
        }
        catch (Exception error) when (error is ArgumentException or
            FormatException or IOException or InvalidOperationException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"ERROR: {error.Message}");
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    private static SoakOptions Parse(string[] args)
    {
        if (args.Length != 14) throw new ArgumentException("Exactly seven option/value pairs are required.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException($"Invalid or duplicate option: {args[index]}");
        }
        string Need(string name) => values.TryGetValue(name, out var value)
            ? value : throw new ArgumentException($"Missing required option {name}.");
        if (!int.TryParse(Need("--app-pid"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var appPid) || appPid <= 0)
            throw new ArgumentException("--app-pid must be a positive integer.");
        if (!int.TryParse(Need("--game-pid"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var gamePid) || gamePid <= 0)
            throw new ArgumentException("--game-pid must be a positive integer.");
        var appStart = ParseUtc("--app-start-utc", Need("--app-start-utc"));
        var gameStart = ParseUtc("--game-start-utc", Need("--game-start-utc"));
        if (!TimeSpan.TryParse(Need("--duration"), CultureInfo.InvariantCulture, out var duration) ||
            duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromHours(24))
            throw new ArgumentException("--duration must be between 00:00:01 and 1.00:00:00.");
        var logDirectory = Path.GetFullPath(Need("--log-dir"));
        if (!Directory.Exists(logDirectory))
            throw new DirectoryNotFoundException($"Activity log directory not found: {logDirectory}");
        var output = Path.GetFullPath(Need("--output"));
        if (File.Exists(output)) throw new IOException($"Evidence output already exists: {output}");
        return new(appPid, appStart, gamePid, gameStart, logDirectory, duration, output);
    }

    private static DateTimeOffset ParseUtc(string name, string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed) || parsed.Offset != TimeSpan.Zero)
            throw new ArgumentException($"{name} must be an ISO-8601 UTC timestamp.");
        return parsed;
    }

    private static async Task<SoakReport> RunAsync(SoakOptions options)
    {
        var started = DateTimeOffset.UtcNow;
        var elapsed = Stopwatch.StartNew();
        await using var logs = new ActivityLogObserver(options.LogDirectory,
            measurementStartedAtUtc: started);
        using var app = new ProcessObserver("RandyPick", options.AppPid, options.AppStartUtc);
        using var game = new ProcessObserver("Warcraft", options.GamePid, options.GameStartUtc);
        using var samples = new CancellationTokenSource();
        var appSamples = app.SampleAsync(
            TimeSpan.FromSeconds(1), samples.Token, logs.RequestCatchUp);
        var gameSamples = game.SampleAsync(TimeSpan.FromSeconds(1), samples.Token);
        var duration = Task.Delay(options.Duration);
        await Task.WhenAny(duration, app.ExitTask);
        samples.Cancel();
        await IgnoreCancellation(appSamples);
        await IgnoreCancellation(gameSamples);
        var logMetrics = await logs.CompleteAsync();
        var completed = DateTimeOffset.UtcNow;
        var appMetrics = app.Snapshot(completed);
        var gameMetrics = game.Snapshot(completed);
        var decision = SoakDecisionPolicy.Evaluate(appMetrics, gameMetrics, logMetrics);
        return new(started, completed, options.Duration.TotalSeconds,
            elapsed.Elapsed.TotalSeconds, appMetrics, gameMetrics, logMetrics, decision, true,
            "External process/resource and EXE-adjacent JSONL observation only; no game memory read or input.");
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
    }

    internal static void WriteNew<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, Json);
    }
}
