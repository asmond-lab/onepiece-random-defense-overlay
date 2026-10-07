using System.Text.Json.Serialization;

internal sealed record ActivityLogObserverHooks(
    Action<string, long>? AfterLengthCaptured = null,
    Action<string, int>? AfterRead = null,
    bool SuppressWatcherNotifications = false);

internal sealed record SoakOptions(
    int AppPid,
    DateTimeOffset AppStartUtc,
    int GamePid,
    DateTimeOffset GameStartUtc,
    string LogDirectory,
    TimeSpan Duration,
    string OutputPath);

internal sealed record ProcessIdentity(
    string Role,
    int ProcessId,
    DateTimeOffset ExpectedStartUtc,
    DateTimeOffset ActualStartUtc,
    string ExecutablePath);

internal sealed record ProcessMetrics(
    ProcessIdentity Identity,
    bool AvailableAtStart,
    bool SurvivedInterval,
    DateTimeOffset? ExitedAtUtc,
    int Samples,
    double AverageNormalizedCpuPercent,
    double MaximumNormalizedCpuPercent,
    long AverageWorkingSetBytes,
    long MaximumWorkingSetBytes,
    long AveragePrivateMemoryBytes,
    long MaximumPrivateMemoryBytes,
    int ResponsiveSamples,
    int UnresponsiveSamples);

internal sealed record DistributionMetrics(
    long Count,
    double? Minimum,
    double? Median,
    double? P95,
    double? Maximum,
    double? Average);

internal sealed record LaneReadMetrics(
    string Lane,
    long Samples,
    long Accepted,
    long Rejected,
    long FrameReceived,
    DistributionMetrics SourceStartIntervalMilliseconds,
    DistributionMetrics ReadDurationMilliseconds);

internal sealed record PresentationMetrics(
    long Samples,
    long Current,
    long Noncurrent,
    DistributionMetrics RenderingDurationMilliseconds);

internal sealed record ActivityCadenceMetrics(
    LaneReadMetrics Basic,
    LaneReadMetrics Full,
    PresentationMetrics Presentation);

internal sealed record ActivityLogMetrics(
    long InitialBytes,
    long FinalBytes,
    long GrowthBytes,
    int InitialFiles,
    int FinalFiles,
    int RotatedFiles,
    long ValidRecords,
    long MalformedCompleteLines,
    long PartialTailBytes,
    IReadOnlyDictionary<string, long> KindCounts,
    long MemoryReadAccepted,
    long MemoryReadRejected,
    long ObservationGaps,
    long WriterLossIndicators,
    long WriterErrorIndicators,
    ActivityCadenceMetrics Cadence,
    DateTimeOffset? FirstReceiptAtUtc,
    DateTimeOffset? LastReceiptAtUtc,
    double MaximumReceiptGapSeconds);

internal sealed record SoakDecision(
    bool Pass,
    bool AppSurvived,
    bool ContinuingValidRecording,
    bool NoWriterLossOrError,
    bool GameAvailableAtEnd,
    string GamePerformanceClaim,
    IReadOnlyList<string> Reasons);

internal sealed record SoakReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    double RequestedDurationSeconds,
    double ObservedDurationSeconds,
    ProcessMetrics App,
    ProcessMetrics Game,
    ActivityLogMetrics ActivityLog,
    SoakDecision Decision,
    bool CleanupComplete,
    string Scope);

internal sealed record SmokeReport(
    bool Success,
    bool CoalescedNotificationsCaughtUp,
    bool AppendDuringReadPreserved,
    bool BoundaryGapMeasured,
    bool StalledRecorderRejected,
    bool SplitPartialCompleted,
    bool TerminalPartialExcludedFromMalformed,
    bool RotationObserved,
    bool WriterLossObserved,
    bool ProcessMetricsObserved,
    bool CadenceMetricsObserved,
    bool CleanupComplete,
    ActivityLogMetrics ActivityLog,
    ProcessMetrics ProcessMetrics,
    IReadOnlyList<string> Failures);

[JsonSerializable(typeof(SoakReport))]
[JsonSerializable(typeof(SmokeReport))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;
