using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using OrandOverlay;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal const string ProfileJson = """
    [{"ProfileSchemaVersion":2,"Layout":"Warcraft30024268Diagnostic","OwnerFieldBytes":4,
    "ProfileId":"explicit-live-validation-only","ProfileRevision":2,
    "FileVersion":"3.0.0.24268","sha256":"BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12",
    "ModuleName":"Warcraft III.exe","LocatorKind":2,"CountOffset":3080,"EntriesPointerOffset":3088,
    "OwnerOffset":448,"RawcodeOffset":376,"MinimumUnitObjects":1,"Enabled":true,"Verified":false}]
    """;

    private static async Task<int> Main(string[] args)
    {
        string stage = "arguments";
        string? output = null;
        string? previousVerification = null;
        bool environmentChanged = false;
        int completed = 0;
        try
        {
            if (args.Length > 0 && args[0] is "record-inventory" or "replay-inventory" or "--help")
                return InventoryTraceCommand.Run(args);
            var options = Parse(args);
            if (options.ScalarsOnly)
            {
                // No output/copy/data I/O, recognition service, profile, UI or installed-file query before this guard.
                BoundReadSession.Metadata(options.Target);
                using var standaloneTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                using var binding = BoundReadSession.Open(options.Target, options.AuthorizedCopy!, standaloneTimeout.Token);
                output = CreateOutput(options.Output);
                int reportsWritten = 0, blockedReports = 0;
                for (int sample = 1; sample <= options.Samples; sample++)
                {
                    binding.BeginSampleBudget();
                    binding.Revalidate();
                    var report = StandaloneScalarRunner.Read(binding, standaloneTimeout.Token);
                    WriteNew(Path.Combine(output, "sample-" + sample + "-standalone-scalars.json"), report);
                    reportsWritten++;
                    if (report.Status is "Blocked" or "BlockedComparison") blockedReports++;
                }
                WriteNew(Path.Combine(output, "complete.json"), new { ReportsWritten = reportsWritten, BlockedReports = blockedReports,
                    ExitMeaning = "Zero means requested diagnostic reports were written, not successful validation",
                    Scope = "Standalone diagnostic hypotheses only", ExperimentalLayoutVerified = false,
                    VerifiedLiveGameplay = false, CanCoach = false, GameplayReady = false });
                return 0;
            }
            stage = "new-output-directory";
            output = CreateOutput(options.Output);
            stage = "process-identity-and-public-executable-pins";
            var identity = VerifyProcess(options.Target, verifyFile: true);
            stage = "explicit-profile";
            var profile = JsonSerializer.Deserialize<MemoryProfile[]>(ProfileJson)!.Single();
            AssertProductionClosed(profile);
            var userRoot = Path.Combine(output, "user-root");
            Directory.CreateDirectory(userRoot);
            WriteNewText(Path.Combine(userRoot, "memory-profiles.json"), ProfileJson);
            var loaded = new MemoryProfileRepository(Path.Combine(userRoot, "memory-profiles.json")).GetProfiles();
            var effective = loaded.Profiles.Single(p => p.FileVersion == Warcraft300Diagnostic.Version);
            if (loaded.Error is not null || effective.ProfileId != profile.ProfileId || effective.Verified ||
                MemoryProfileValidator.Validate(effective).Count != 0 || !Warcraft300Diagnostic.SessionAllows(effective, true))
                throw new InvalidDataException();
            AssertProductionClosed(effective);
            stage = "packaged-map-data";
            var catalog = new DataCatalog();
            catalog.Load(mapVersion: "2.320");
            var settings = new AppSettings { Mode = PlayMode.Normal, TelemetryEnabled = false,
                TelemetryConsentAccepted = false, AutoUpdateEnabled = false, BeginnerCoachEnabled = false };
            var reader = new WarcraftMemoryRecognitionService(catalog, userRoot, options.Target);
            previousVerification = Environment.GetEnvironmentVariable("ORAND_VERIFY_DIR", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("ORAND_VERIFY_DIR", output, EnvironmentVariableTarget.Process);
            environmentChanged = true;
            WriteNew(Path.Combine(output, "session.json"), new {
                Scope = "Explicit diagnostic current-view validation only; never a verified live-game claim",
                SelectedMapVersion = "2.320", identity.ProcessVersion, identity.ExecutableSha256,
                RequestedSamples = options.Samples, CaptureUiRequested = options.CaptureUi,
                ScalarProbeRequested = options.ProbeScalars,
                ProductionSessionAllows = false, Verified = false,
                CanCoach = false, LocalOwnership = "Unknown", Alive = "Unknown", RuntimeMapProof = "Unknown",
                FreshnessBudgetSeconds = 3, PerSampleCancellationSeconds = 45
            });
            // Prepare all WPF initialization BEFORE native reads start their immutable 3-second TTL.
            stage = "prepare-optional-ui-host";
            await using var ui = options.CaptureUi ? await LiveReferenceUiProof.PrepareAsync(settings) : null;
            for (int i = 1; i <= options.Samples; i++)
            {
                stage = "sample-" + i + "-preflight";
                VerifyProcess(options.Target, verifyFile: false);
                AssertProductionClosed(effective);
                stage = "sample-" + i + "-recognize";
                var timer = Stopwatch.StartNew();
                RecognitionResult? result = null;
                Exception? failure = null;
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                {
                    try { result = await reader.RecognizeAsync(settings, timeout.Token); }
                    catch (Exception error) { failure = error; }
                }
                timer.Stop();
                // Always recheck the live target, including cancellation and exceptions.
                stage = "sample-" + i + "-postflight";
                VerifyProcess(options.Target, verifyFile: false);
                AssertProductionClosed(effective);
                if (failure is not null)
                {
                    WriteNew(Path.Combine(output, "sample-" + i + ".json"), new {
                        Sample = i, Stage = "recognize", ExceptionType = failure.GetType().Name,
                        ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds, TargetStillMatches = true,
                        CanCoach = false, HasRound = false, Round = (int?)null
                    });
                    throw new SampleReadException();
                }
                var r = result!;
                var d = r.Diagnostics;
                var o = r.DiagnosticObservation;
                // Fail closed if the diagnostic branch ever starts emitting gameplay identity/round proof.
                if (r.VerifiedLocalPlayerSlot is not null || r.NativeUnitPointers.Count != 0 ||
                    d.MapState is not null || r.ConfirmsSessionBoundary ||
                    o is { CanProvideCoachCurrent: true } || o is { GameplayReady: true })
                    throw new InvalidDataException();
                stage = "sample-" + i + "-optional-ui-capture";
                // This is the exact producer result, with no new observation or timestamp.
                var uiValid = ui is null || await ui.CaptureAsync(r, output, i);
                if (ui is not null)
                {
                    VerifyProcess(options.Target, verifyFile: false);
                    AssertProductionClosed(effective);
                }
                var now = DateTimeOffset.UtcNow;
                stage = "sample-" + i + "-redacted-output";
                WriteNew(Path.Combine(output, "sample-" + i + ".json"), new {
                    Sample = i, State = r.State.ToString(), Source = d.Source,
                    ProcessVersion = d.ProcessVersion, ExecutableSha256 = d.ExecutableSha256,
                    ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
                    Units = r.Entries.Select(e => new { Id = e.UnitId, Name = catalog.Unit(e.UnitId).Name,
                        e.Count, GrowthReserved = d.GrowthUnitIds.Contains(e.UnitId) }).ToArray(),
                    TotalCount = r.Entries.Sum(e => e.Count), GrowthUnitIds = d.GrowthUnitIds,
                    d.ObservedObjects, d.MappedObjects, d.UnknownObjects, d.ForeignObjects,
                    UnknownRawcodes = d.UnknownRawcodes,
                    d.ExcludedSourceHelperObjects, d.EligibleObjects, d.EligibleMappedObjects, d.EligibleUnknownObjects,
                    TypedAvailability = o?.Availability.ToString() ?? "Missing",
                    Reason = o?.Reason ?? "No typed diagnostic observation",
                    ReadFailure = r.State == RecognitionState.TransientReadError
                        ? System.Text.RegularExpressions.Regex.Replace(d.Detail ?? "", @"0x[0-9a-fA-F]+", "[address]") : null,
                    ReferenceRound = o?.ObservedRound,
                    ReadDurationMilliseconds = o?.ReadDuration.TotalMilliseconds,
                    StartedAgeMilliseconds = o is null ? (double?)null : (now - o.StartedAt).TotalMilliseconds,
                    CompletedAgeMilliseconds = o is null ? (double?)null : (now - o.CompletedAt).TotalMilliseconds,
                    ContextHash = o is not null && DiagnosticInventoryObservation.IsContextId(o.ContextId) ? o.ContextId : null,
                    TypedTotalCount = o?.Entries.Sum(e => e.Count),
                    TargetStillMatches = true, ProductionSessionAllows = false,
                    CanCoach = false, GameplayReady = false, LocalOwnership = "Unknown", Alive = "Unknown",
                    Completeness = "Unknown", RuntimeMapProof = "Unknown", HasRound = false, Round = (int?)null,
                    Scope = "Diagnostic current-view reference only; State Ready does not mean verified live gameplay"
                });
                // Run AFTER exact-result UI capture and main JSON output. Never refresh r/o timestamps.
                if (options.ProbeScalars)
                {
                    stage = "sample-" + i + "-optional-scalar-probe";
                    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var probe = r.State == RecognitionState.Ready
                        ? NativeScalarProbe.Run(reader, r, options.Target,
                            () => { VerifyProcess(options.Target, verifyFile: true); AssertProductionClosed(effective); }, probeTimeout.Token)
                        : (object)new { Status = "Blocked", Reason = "Main result not Ready",
                            ExperimentalLayoutVerified = false, CanCoach = false };
                    WriteNew(Path.Combine(output, "sample-" + i + "-scalar-probe.json"), probe);
                    VerifyProcess(options.Target, verifyFile: false);
                    AssertProductionClosed(effective);
                }
                completed++;
                if (!uiValid) throw new UiCaptureUnavailableException();
                if (i < options.Samples) await Task.Delay(300);
            }
            stage = "final-profile-isolation";
            if (Directory.EnumerateFileSystemEntries(userRoot).Count() != 1) throw new InvalidDataException();
            AssertProductionClosed(effective);
            stage = "final-ui-shutdown-and-join";
            if (ui is not null) await ui.DisposeAsync();
            WriteNew(Path.Combine(output, "complete.json"), new { CompletedSamples = completed,
                CaptureUiRequested = options.CaptureUi, UiHostClosedAndJoined = ui is not null,
                ProductionSessionAllows = false, VerifiedLiveGameplay = false, CanCoach = false });
            Console.WriteLine("Bounded diagnostic validation finished; no verified live-game claim.");
            return 0;
        }
        catch (Exception error)
        {
            var redacted = new { Stage = stage, ExceptionType = error.GetType().Name,
                CompletedSamples = completed, VerifiedLiveGameplay = false, CanCoach = false };
            if (output is not null)
            {
                try { WriteNew(Path.Combine(output, "failure.json"), redacted); } catch { }
            }
            Console.Error.WriteLine(JsonSerializer.Serialize(redacted));
            return 1;
        }
        finally
        {
            if (environmentChanged)
                Environment.SetEnvironmentVariable("ORAND_VERIFY_DIR", previousVerification, EnvironmentVariableTarget.Process);
        }
    }

    private static void AssertProductionClosed(MemoryProfile p)
    {
        if (p.Verified || Warcraft300Diagnostic.SessionAllows(p, false) || MemoryProfileValidator.CanActivate(p, out _))
            throw new InvalidDataException();
    }

    private sealed record Options(ExpectedReadTarget Target, string Output, int Samples, bool CaptureUi, bool ProbeScalars, bool ScalarsOnly, string? AuthorizedCopy);
    private static Options Parse(string[] args)
    {
        if (args.Length < 8 || args.Length > 14) throw new ArgumentException();
        bool captureUi = false, probeScalars = false, scalarsOnly = false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length;)
        {
            if (args[i] == "--probe-scalars-only")
            {
                if (scalarsOnly) throw new ArgumentException();
                scalarsOnly = true; i++; continue;
            }
            if (args[i] == "--probe-scalars")
            {
                if (probeScalars) throw new ArgumentException();
                probeScalars = true; i++; continue;
            }
            if (args[i] == "--capture-ui")
            {
                if (captureUi) throw new ArgumentException();
                captureUi = true; i++; continue;
            }
            if (i + 1 >= args.Length || args[i] is not ("--pid" or "--started-at" or "--output" or "--samples" or "--authorized-exe-copy") ||
                !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException();
            i += 2;
        }
        if (scalarsOnly ? captureUi || probeScalars || !values.ContainsKey("--authorized-exe-copy") : values.ContainsKey("--authorized-exe-copy"))
            throw new ArgumentException();
        if (values.Count != (scalarsOnly ? 5 : 4) || !int.TryParse(values["--pid"], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) ||
            !int.TryParse(values["--samples"], NumberStyles.None, CultureInfo.InvariantCulture, out int samples) || samples is < 1 or > 3)
            throw new ArgumentException();
        var start = values["--started-at"];
        // Explicit UTC ISO timestamp only; never infer machine-local time.
        if (!start.EndsWith("Z", StringComparison.Ordinal) || !DateTimeOffset.TryParseExact(start,
            new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" },
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var started))
            throw new ArgumentException();
        return new(new ExpectedReadTarget(pid, started), values["--output"], samples, captureUi, probeScalars, scalarsOnly, values.GetValueOrDefault("--authorized-exe-copy"));
    }

    private sealed record PublicIdentity(string ProcessVersion, string ExecutableSha256);
    private static PublicIdentity VerifyProcess(ExpectedReadTarget target, bool verifyFile)
    {
        // Metadata only. Reject extra/newer processes before invoking the production memory reader.
        var processes = Process.GetProcessesByName("Warcraft III").Concat(Process.GetProcessesByName("WarcraftIII")).ToArray();
        try
        {
            if (processes.Length != 1) throw new ReadTargetMismatchException();
            var p = processes[0];
            if (p.HasExited) throw new ReadTargetMismatchException();
            target.EnsureMatches(p.Id, p.StartTime.ToUniversalTime().Ticks);
            if (!verifyFile) return new("", "");
            var module = p.MainModule ?? throw new InvalidDataException();
            var version = module.FileVersionInfo.FileVersion ?? "";
            if (version != Warcraft300Diagnostic.Version || module.ModuleName != "Warcraft III.exe")
                throw new InvalidDataException();
            using var executable = new FileStream(module.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hash = Convert.ToHexString(SHA256.HashData(executable));
            if (!hash.Equals(Warcraft300Diagnostic.Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            if (p.HasExited) throw new ReadTargetMismatchException();
            target.EnsureMatches(p.Id, p.StartTime.ToUniversalTime().Ticks);
            return new(version, hash);
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    private static string CreateOutput(string requested)
    {
        if (!Path.IsPathFullyQualified(requested) || requested.StartsWith(@"\", StringComparison.Ordinal))
            throw new ArgumentException();
        var full = Path.GetFullPath(requested).TrimEnd(Path.DirectorySeparatorChar);
        var sessions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aside", "u", "0", "sessions")
            + Path.DirectorySeparatorChar;
        if (!full.StartsWith(sessions, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException();
        var components = full[sessions.Length..].Split(Path.DirectorySeparatorChar);
        if (components.Length < 3 || components[1] != "tmp" || components.Any(x => x.Length == 0)) throw new ArgumentException();
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException();
        var parent = Directory.GetParent(full) ?? throw new ArgumentException();
        if (!parent.Exists) throw new DirectoryNotFoundException();
        for (DirectoryInfo? node = parent; node is not null; node = node.Parent)
            if ((node.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException();
        Directory.CreateDirectory(full);
        if (Directory.EnumerateFileSystemEntries(full).Any()) throw new IOException();
        return full;
    }
    private static void WriteNew(string file, object value) => WriteNewText(file, JsonSerializer.Serialize(value, Json));
    private static void WriteNewText(string file, string text)
    {
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(text);
    }
    private sealed class SampleReadException : Exception { }
    private sealed class UiCaptureUnavailableException : Exception { }
}
