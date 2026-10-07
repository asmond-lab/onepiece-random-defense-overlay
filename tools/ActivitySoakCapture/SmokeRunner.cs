using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static class SmokeRunner
{
    internal static async Task<int> RunAsync(string output)
    {
        if (File.Exists(output)) throw new IOException($"Evidence output already exists: {output}");
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        var work = Path.Combine(parent, "smoke-work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        ActivityLogMetrics? logs = null;
        ActivityLogMetrics? coalescedLogs = null;
        ProcessMetrics? processMetrics = null;
        var failures = new List<string>();
        var cleanup = false;
        try
        {
            var first = Path.Combine(work, "activity-smoke-0001.jsonl");
            var coalescedDirectory = Path.Combine(work, "coalesced");
            Directory.CreateDirectory(coalescedDirectory);
            var raceLine = Line("memory.read", """{"lane":"basic","accepted":false,"startedAt":"2026-09-21T00:00:01.2500000Z","durationMs":12.5}""");
            var timelineStart = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);
            var clockTicks = timelineStart.UtcTicks;
            DateTimeOffset Clock() => new(Interlocked.Read(ref clockTicks), TimeSpan.Zero);
            void SetClock(int seconds) => Interlocked.Exchange(
                ref clockTicks, timelineStart.AddSeconds(seconds).UtcTicks);
            var raceInjected = 0;
            var firstReads = 0;
            var secondRead = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var hooks = new ActivityLogObserverHooks(
                AfterLengthCaptured: (path, _) =>
                {
                    if (path.Equals(first, StringComparison.OrdinalIgnoreCase) &&
                        Interlocked.CompareExchange(ref raceInjected, 1, 0) == 0)
                        File.AppendAllText(path, raceLine, Encoding.UTF8);
                },
                AfterRead: (path, _) =>
                {
                    if (path.Equals(first, StringComparison.OrdinalIgnoreCase) &&
                        Interlocked.Increment(ref firstReads) == 2)
                        secondRead.TrySetResult();
                });
            await using var observer = new ActivityLogObserver(
                work, hooks, timelineStart, Clock);
            await using var coalescedObserver = new ActivityLogObserver(
                coalescedDirectory,
                new ActivityLogObserverHooks(SuppressWatcherNotifications: true),
                timelineStart, Clock);
            using var process = Process.GetCurrentProcess();
            var start = process.StartTime.ToUniversalTime();
            using var processObserver = new ProcessObserver("smoke", process.Id, start);
            using var sampleStop = new CancellationTokenSource();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var coalescedFile = Path.Combine(
                coalescedDirectory, "activity-coalesced-0001.jsonl");
            await AppendAsync(coalescedFile,
                Line("memory.read", """{"lane":"full","accepted":true,"startedAt":"2026-09-21T00:00:01.0000000Z","durationMs":40}""") +
                Line("ui.presentation", """{"current":true,"renderingMs":2}"""));
            var measurementTick = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var samples = processObserver.SampleAsync(
                TimeSpan.FromMilliseconds(20), sampleStop.Token,
                () =>
                {
                    coalescedObserver.RequestCatchUp();
                    measurementTick.TrySetResult();
                });
            await measurementTick.Task.WaitAsync(timeout.Token);
            await coalescedObserver.WaitForRecordsAsync(2, timeout.Token);

            SetClock(1);
            await AppendAsync(first, Line("memory.read", """{"lane":"basic","accepted":true,"startedAt":"2026-09-21T00:00:01.0000000Z","durationMs":10.5}"""));
            await secondRead.Task.WaitAsync(timeout.Token);
            if (observer.ValidRecords < 2)
                throw new InvalidOperationException("append-during-read record was skipped.");

            var gap = Line("observation.gap", """{"reason":"context-reset"}""");
            var middle = gap.Length / 2;
            SetClock(2);
            var fileEvent = observer.WaitForFileEventAsync(timeout.Token);
            await AppendAsync(first, gap[..middle]);
            await fileEvent;
            await AppendAsync(first, gap[middle..]);
            await observer.WaitForRecordsAsync(3, timeout.Token);

            var second = Path.Combine(work, "activity-smoke-0002.jsonl");
            SetClock(3);
            await AppendAsync(second,
                Line("record-loss", """{"count":1,"firstSequence":3,"lastSequence":3}""") +
                Line("ui.presentation", """{"current":true,"renderingMs":1.5}""") + "{");
            await observer.WaitForRecordsAsync(5, timeout.Token);

            sampleStop.Cancel();
            try { await samples; } catch (OperationCanceledException) { }
            processMetrics = processObserver.Snapshot(DateTimeOffset.UtcNow);
            SetClock(4);
            logs = await observer.CompleteAsync();
            coalescedLogs = await coalescedObserver.CompleteAsync();
            Check(logs.ValidRecords == 5, "expected five complete valid records", failures);
            Check(logs.ObservationGaps == 1, "split partial gap was not completed", failures);
            Check(logs.MalformedCompleteLines == 0,
                "terminal partial line was labeled malformed", failures);
            Check(logs.PartialTailBytes == 1, "terminal partial tail was not retained", failures);
            Check(logs.RotatedFiles == 2, "created rotation files were not counted", failures);
            Check(logs.WriterLossIndicators == 1, "record-loss indicator was not counted", failures);
            Check(processMetrics.Samples > 0, "process metrics were not sampled", failures);
            Check(coalescedLogs.ValidRecords == 2,
                "coalesced notification records were not caught up", failures);
            Check(logs.MemoryReadAccepted == 1 && logs.MemoryReadRejected == 1,
                "append-during-read record was not preserved", failures);
            Check(logs.Cadence.Basic.Samples == 2 && logs.Cadence.Basic.Accepted == 1 &&
                logs.Cadence.Basic.Rejected == 1 &&
                logs.Cadence.Basic.SourceStartIntervalMilliseconds.Count == 1 &&
                logs.Cadence.Basic.SourceStartIntervalMilliseconds.Median == 250 &&
                logs.Cadence.Basic.ReadDurationMilliseconds.Median == 11.5,
                "basic source cadence or duration metrics were not derived", failures);
            Check(logs.Cadence.Presentation.Samples == 1 &&
                logs.Cadence.Presentation.Current == 1 &&
                logs.Cadence.Presentation.RenderingDurationMilliseconds.Median == 1.5,
                "presentation bookkeeping metrics were not derived", failures);
            Check(Math.Abs(logs.MaximumReceiptGapSeconds - 1) < 0.000001,
                "boundary-inclusive maximum receipt gap was not measured", failures);
            var cleanLogs = logs with
            {
                WriterLossIndicators = 0,
                WriterErrorIndicators = 0
            };
            Check(SoakDecisionPolicy.Evaluate(processMetrics, processMetrics, cleanLogs).Pass,
                "active synthetic receipt coverage did not pass", failures);
            var stalledLogs = cleanLogs with
            {
                ValidRecords = 2,
                FirstReceiptAtUtc = timelineStart.AddSeconds(1),
                LastReceiptAtUtc = timelineStart.AddSeconds(2),
                MaximumReceiptGapSeconds = SoakDecisionPolicy.AllowedRecordingGapSeconds + 0.001
            };
            var stalled = SoakDecisionPolicy.Evaluate(
                processMetrics, processMetrics, stalledLogs);
            Check(!stalled.Pass && stalled.Reasons.Contains("recording-gap-exceeded"),
                "two early records followed by a stall incorrectly passed", failures);
        }
        finally
        {
            Directory.Delete(work, true);
            cleanup = !Directory.Exists(work);
        }

        if (!cleanup) failures.Add("smoke work directory remained");
        var report = new SmokeReport(failures.Count == 0,
            coalescedLogs?.ValidRecords == 2,
            logs?.MemoryReadAccepted == 1 && logs?.MemoryReadRejected == 1,
            logs is not null && Math.Abs(logs.MaximumReceiptGapSeconds - 1) < 0.000001,
            logs is not null && !SoakDecisionPolicy.Evaluate(
                processMetrics!, processMetrics!, logs with
                {
                    WriterLossIndicators = 0,
                    WriterErrorIndicators = 0,
                    ValidRecords = 2,
                    MaximumReceiptGapSeconds =
                        SoakDecisionPolicy.AllowedRecordingGapSeconds + 0.001
                }).Pass,
            logs?.ObservationGaps == 1,
            logs?.MalformedCompleteLines == 0 && logs?.PartialTailBytes == 1,
            logs?.RotatedFiles == 2,
            logs?.WriterLossIndicators == 1,
            processMetrics?.Samples > 0,
            logs?.Cadence.Basic.SourceStartIntervalMilliseconds.Median == 250 &&
                logs.Cadence.Presentation.Samples == 1,
            cleanup,
            logs ?? throw new InvalidOperationException("Smoke log metrics missing."),
            processMetrics ?? throw new InvalidOperationException("Smoke process metrics missing."),
            failures);
        Program.WriteNew(output, report);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            report.Success,
            report.CoalescedNotificationsCaughtUp,
            report.AppendDuringReadPreserved,
            report.BoundaryGapMeasured,
            report.StalledRecorderRejected,
            report.SplitPartialCompleted,
            report.TerminalPartialExcludedFromMalformed,
            report.RotationObserved,
            report.WriterLossObserved,
            report.ProcessMetricsObserved,
            report.CadenceMetricsObserved,
            report.CleanupComplete,
            output
        }));
        return report.Success ? 0 : 1;
    }

    private static string Line(string kind, string data) =>
        $$"""{"schemaVersion":1,"applicationSessionId":"smoke","sequence":1,"receivedAtUtc":"2026-09-21T00:00:00Z","elapsedMilliseconds":1,"kind":"{{kind}}","matchGeneration":0,"data":{{data}}}""" + "\n";

    private static async Task AppendAsync(string path, string value)
    {
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        var bytes = Encoding.UTF8.GetBytes(value);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static void Check(bool condition, string failure, List<string> failures)
    {
        if (!condition) failures.Add(failure);
    }
}
