using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OrandOverlay;
public enum RuntimeMapIdentityState { Unknown, Proven }
public enum RuntimeMapIdentityFailure
{
    None,
    LogMissing,
    LogReadFailed,
    LogRotated,
    LogClockUnverified,
    PartialOpeningRecord,
    NoPostStartOpeningRecord,
    AmbiguousLatestOpeningRecord,
    OpeningMapPathNotAbsolute,
    ArchiveMissing,
    ArchiveLengthMismatch,
    ArchiveHashMismatch
}
public sealed record RuntimeMapIdentityResult(
    RuntimeMapIdentityState State,
    RuntimeMapIdentityFailure Failure,
    string Reason,
    DateTimeOffset? RecordTimestamp,
    string RuntimePathField,
    string ActualArchivePath,
    long ArchiveLengthBytes,
    string ActualArchiveSha256,
    long LogBytesRead,
    int LogReadCalls,
    long ArchiveBytesRead,
    int ArchiveReadCalls);

public static class RuntimeMapIdentityProvider
{
    public const int MaximumLogTailBytes = 4 * 1024 * 1024;
    public const string PathField =
        "War3Log.txt latest complete Opening map record after session start";

    private static readonly Regex OpeningMapPattern = new(
        "^(?<month>[0-9]{1,2})/(?<day>[0-9]{1,2}) " +
        "(?<clock>[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3})  " +
        "Opening map - (?<path>.+)$",
        RegexOptions.CultureInvariant);

    public static RuntimeMapIdentityResult Probe(
        DateTimeOffset sessionStartedAt,
        string war3LogPath,
        MapArchivePin expected) => Probe(sessionStartedAt, war3LogPath, expected, null, 0, 0);

    public static RuntimeMapIdentityResult Probe(
        DateTimeOffset sessionStartedAt, string war3LogPath, MapArchivePin expected,
        RuntimeMapIdentitySession? session, int processId, long processCreationTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(war3LogPath);
        ArgumentNullException.ThrowIfNull(expected);
        if (!File.Exists(war3LogPath))
        {
            session?.Reset();
            return Unknown(RuntimeMapIdentityFailure.LogMissing, "War3Log source is missing.");
        }
        var log = ReadTail(war3LogPath, session);
        if (log.Failure != RuntimeMapIdentityFailure.None || log.HasPartialOpeningRecord)
        {
            session?.Reset();
            return log.HasPartialOpeningRecord
                ? Unknown(RuntimeMapIdentityFailure.PartialOpeningRecord,
                    "War3Log ends with a partial Opening map record.", log.BytesRead, log.ReadCalls)
                : Unknown(log.Failure, log.Reason, log.BytesRead, log.ReadCalls);
        }
        if (session is not null && !HasSessionClockAnchor(log.Text, sessionStartedAt))
        {
            session.Reset();
            return Unknown(RuntimeMapIdentityFailure.LogClockUnverified,
                "No GameMain Started clock anchor matches this process's local start.",
                log.BytesRead, log.ReadCalls);
        }
        var records = ParseRecords(log.Text, sessionStartedAt);
        if (records.Count == 0)
        {
            session?.Reset();
            return Unknown(RuntimeMapIdentityFailure.NoPostStartOpeningRecord,
                "No complete Opening map record is strictly after the session start.",
                log.BytesRead, log.ReadCalls);
        }

        var latestTimestamp = records.Max(record => record.Timestamp);
        var latest = records.Where(record => record.Timestamp == latestTimestamp).ToArray();
        var paths = latest.Select(record => record.Path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length != 1)
        {
            session?.Reset();
            return Unknown(RuntimeMapIdentityFailure.AmbiguousLatestOpeningRecord,
                "The latest Opening map timestamp names multiple paths.", log.BytesRead, log.ReadCalls);
        }
        var loggedPath = paths[0].Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathFullyQualified(loggedPath))
        {
            session?.Reset();
            return Unknown(RuntimeMapIdentityFailure.OpeningMapPathNotAbsolute,
                "The latest Opening map value is not an absolute archive path.", log.BytesRead, log.ReadCalls);
        }

        string archivePath;
        try { archivePath = Path.GetFullPath(loggedPath); }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException or PathTooLongException)
        {
            session?.Reset();
            return Unknown(RuntimeMapIdentityFailure.OpeningMapPathNotAbsolute,
                "The latest Opening map path cannot be resolved.",
                log.BytesRead, log.ReadCalls);
        }
        if (!File.Exists(archivePath))
        {
            session?.Reset();
            return Unknown(RuntimeMapIdentityFailure.ArchiveMissing,
                "The archive named by the latest Opening map record is missing.",
                log.BytesRead, log.ReadCalls, latestTimestamp, archivePath);
        }
        if (session is not null)
            return session.Verify(processId, processCreationTicks, sessionStartedAt,
                war3LogPath, log.Creation, log.Length, log.TailSha256, archivePath,
                latestTimestamp, expected, log.BytesRead, log.ReadCalls);

        try
        {
            var length = new FileInfo(archivePath).Length;
            if (length != expected.LengthBytes)
                return Unknown(RuntimeMapIdentityFailure.ArchiveLengthMismatch,
                    $"Archive length {length} does not match pinned length {expected.LengthBytes}.",
                    log.BytesRead, log.ReadCalls, latestTimestamp, archivePath,
                    length);

            using var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read,
                FileShare.Read);
            var actualSha = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
            if (!actualSha.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                return Unknown(RuntimeMapIdentityFailure.ArchiveHashMismatch,
                    "Archive SHA-256 does not match the pinned map archive.",
                    log.BytesRead, log.ReadCalls, latestTimestamp, archivePath,
                    length, actualSha, length, 1);

            return new RuntimeMapIdentityResult(
                RuntimeMapIdentityState.Proven, RuntimeMapIdentityFailure.None,
                "Latest post-start Opening map path resolves to the pinned archive bytes.",
                latestTimestamp, PathField, archivePath, length, actualSha,
                log.BytesRead, log.ReadCalls, length, 1);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or SecurityException)
        {
            return Unknown(RuntimeMapIdentityFailure.ArchiveMissing,
                "The archive could not be read.", log.BytesRead, log.ReadCalls,
                latestTimestamp, archivePath);
        }
    }

    private static LogTail ReadTail(string path, RuntimeMapIdentitySession? session)
    {
        try
        {
            var before = new FileInfo(path);
            before.Refresh();
            var creation = before.CreationTimeUtc;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            var count = (int)Math.Min(length, MaximumLogTailBytes);
            var start = length - count;
            stream.Position = start;
            var bytes = new byte[count];
            stream.ReadExactly(bytes);
            session?.ObserveOuterRead(count);

            var after = new FileInfo(path);
            after.Refresh();
            var continuity = ValidateLogContinuity(
                creation, length, after.CreationTimeUtc, after.Length);
            if (!after.Exists || continuity != RuntimeMapIdentityFailure.None)
                return LogTail.Failed(RuntimeMapIdentityFailure.LogRotated,
                    "War3Log rotated or shrank during the read.", count);

            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (start > 0)
            {
                var firstNewline = text.IndexOf('\n');
                text = firstNewline < 0 ? "" : text[(firstNewline + 1)..];
            }
            var partial = text.Length > 0 && text[^1] != '\n' &&
                text[(text.LastIndexOf('\n') + 1)..].Contains(
                    "Opening map - ", StringComparison.Ordinal);
            return new LogTail(text, partial, RuntimeMapIdentityFailure.None,
                "", count, 1, creation, length, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or DecoderFallbackException)
        {
            return LogTail.Failed(RuntimeMapIdentityFailure.LogReadFailed,
                "War3Log could not be read consistently.", 0);
        }
    }

    private static bool HasSessionClockAnchor(string text, DateTimeOffset start)
    {
        foreach (var line in text.Split('\n'))
        {
            var match = Regex.Match(line.TrimEnd('\r'),
                "^(?<month>[0-9]{1,2})/(?<day>[0-9]{1,2}) (?<clock>[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3})  GameMain Started$",
                RegexOptions.CultureInvariant);
            if (match.Success && TryTimestamp(match, start, out var at) &&
                at >= start.AddSeconds(-2) && at <= start.AddMinutes(2)) return true;
        }
        return false;
    }

    private static List<OpeningRecord> ParseRecords(
        string text, DateTimeOffset sessionStartedAt)
    {
        var result = new List<OpeningRecord>();
        foreach (var rawLine in text.Split('\n'))
        {
            var match = OpeningMapPattern.Match(rawLine.TrimEnd('\r'));
            if (!match.Success) continue;
            if (!TryTimestamp(match, sessionStartedAt, out var timestamp) ||
                timestamp <= sessionStartedAt) continue;
            result.Add(new OpeningRecord(timestamp, match.Groups["path"].Value));
        }
        return result;
    }

    internal static RuntimeMapIdentityFailure ValidateLogContinuity(
        DateTime creationBefore, long lengthBefore,
        DateTime creationAfter, long lengthAfter) =>
        creationAfter != creationBefore || lengthAfter < lengthBefore
            ? RuntimeMapIdentityFailure.LogRotated
            : RuntimeMapIdentityFailure.None;

    private static bool TryTimestamp(
        Match match, DateTimeOffset boundary, out DateTimeOffset timestamp)
    {
        var source = $"{boundary.Year}/{match.Groups["month"].Value}/" +
                     $"{match.Groups["day"].Value} {match.Groups["clock"].Value}";
        if (!DateTime.TryParseExact(source, "yyyy/M/d HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            timestamp = default;
            return false;
        }
        timestamp = new DateTimeOffset(parsed, boundary.Offset);
        if (timestamp < boundary.AddMonths(-6)) timestamp = timestamp.AddYears(1);
        else if (timestamp > boundary.AddMonths(6)) timestamp = timestamp.AddYears(-1);
        return true;
    }

    private static RuntimeMapIdentityResult Unknown(
        RuntimeMapIdentityFailure failure, string reason,
        long logBytes = 0, int logCalls = 0,
        DateTimeOffset? timestamp = null, string archivePath = "",
        long archiveLength = 0, string archiveSha = "",
        long archiveBytes = 0, int archiveCalls = 0) =>
        new(RuntimeMapIdentityState.Unknown, failure, reason, timestamp,
            PathField, archivePath, archiveLength, archiveSha,
            logBytes, logCalls, archiveBytes, archiveCalls);

    private sealed record OpeningRecord(DateTimeOffset Timestamp, string Path);
    private sealed record LogTail(
        string Text, bool HasPartialOpeningRecord,
        RuntimeMapIdentityFailure Failure, string Reason,
        long BytesRead, int ReadCalls, DateTime Creation, long Length, string TailSha256)
    {
        public static LogTail Failed(
            RuntimeMapIdentityFailure failure, string reason, long bytes) =>
            new("", false, failure, reason, bytes, 1, default, 0, "");
    }
}

public static class RuntimeAdaptivePlanningReadiness
{
    public static bool IsReady(
        RuntimeSignalFeasibilityProfile profile,
        RuntimeMapIdentityResult currentIdentity) =>
        profile.AdaptivePlanningCapable &&
        profile.LiveReadinessRequiresCurrentMapIdentityProof &&
        currentIdentity.State == RuntimeMapIdentityState.Proven;
}
