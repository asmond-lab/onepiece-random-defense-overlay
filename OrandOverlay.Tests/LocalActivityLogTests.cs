using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class LocalActivityLogTests
{
    [Fact]
    public async Task FlushWritesChronologicalJsonLinesWithStableEnvelope()
    {
        using var directory = new TempDirectory();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero));
        await using var log = new LocalActivityLog(directory.FullName, time);

        time.Advance(TimeSpan.FromMilliseconds(125));
        Assert.True(log.TryRecord("recognition", new { UnitId = "luffy", Count = 2 }, 7, 11));
        time.Advance(TimeSpan.FromMilliseconds(25));
        Assert.True(log.TryRecord("round", new { Round = 20 }));
        await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(log.CurrentPath);
        Assert.Null(log.LastError);
        var lines = ReadAllLines(log.CurrentPath!);
        using var first = JsonDocument.Parse(lines[0]);
        using var second = JsonDocument.Parse(lines[1]);
        Assert.Equal(1, first.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(first.RootElement.GetProperty("applicationSessionId").GetGuid(),
            second.RootElement.GetProperty("applicationSessionId").GetGuid());
        Assert.Equal(1, first.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(2, second.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 10, 0, 0, 125, TimeSpan.Zero),
            first.RootElement.GetProperty("receivedAtUtc").GetDateTimeOffset());
        Assert.Equal(125, first.RootElement.GetProperty("elapsedMilliseconds").GetDouble());
        Assert.Equal("recognition", first.RootElement.GetProperty("kind").GetString());
        Assert.Equal(7, first.RootElement.GetProperty("matchGeneration").GetInt64());
        Assert.Equal(11, first.RootElement.GetProperty("recognitionRevision").GetInt64());
        Assert.Equal("luffy", first.RootElement.GetProperty("data").GetProperty("unitId").GetString());
        Assert.False(second.RootElement.TryGetProperty("matchGeneration", out _));
    }

    [Fact]
    public async Task ConcurrentProducersRetainEveryRecordInAssignedSequenceOrder()
    {
        using var directory = new TempDirectory();
        await using var log = new LocalActivityLog(directory.FullName, queueCapacity: 2048);
        const int producers = 8;
        const int perProducer = 100;

        var admitted = new ConcurrentBag<string>();
        await Task.WhenAll(Enumerable.Range(0, producers).Select(producer => Task.Run(() =>
        {
            for (var index = 0; index < perProducer; index++)
            {
                var id = $"{producer}:{index}";
                Assert.True(log.TryRecord("concurrent", new { Id = id }));
                admitted.Add(id);
            }
        })));
        await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var rows = ReadAllLines(log.CurrentPath!).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.Equal(producers * perProducer, rows.Length);
            Assert.Equal(Enumerable.Range(1, rows.Length).Select(value => (long)value),
                rows.Select(row => row.RootElement.GetProperty("sequence").GetInt64()));
            Assert.Equal(admitted.Order(StringComparer.Ordinal), rows.Select(row =>
                row.RootElement.GetProperty("data").GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
            foreach (var producer in Enumerable.Range(0, producers))
                Assert.Equal(Enumerable.Range(0, perProducer), rows.Where(row =>
                    row.RootElement.GetProperty("data").GetProperty("id").GetString()!.StartsWith(producer + ":", StringComparison.Ordinal))
                    .Select(row => int.Parse(row.RootElement.GetProperty("data").GetProperty("id").GetString()!.Split(':')[1])));
        }
        finally
        {
            foreach (var row in rows) row.Dispose();
        }
    }

    [Fact]
    public async Task DisposeDrainsAdmittedRecordsAndClosesTheFile()
    {
        using var directory = new TempDirectory();
        var log = new LocalActivityLog(directory.FullName);
        Assert.True(log.TryRecord("before-dispose", new { Value = 1 }));

        await log.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(File.ReadAllLines(log.CurrentPath!));
        using var exclusive = new FileStream(log.CurrentPath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.False(log.TryRecord("after-dispose", new { Value = 2 }));
        await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FlushDoesNotWaitForARecordAdmittedAfterItsBarrier()
    {
        using var directory = new TempDirectory();
        var first = new BlockingPayload();
        var later = new BlockingPayload();
        await using var log = new LocalActivityLog(directory.FullName);
        try
        {
            Assert.True(log.TryRecord("before-barrier", first));
            await first.SerializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var flush = log.FlushAsync();
            Assert.True(log.TryRecord("after-barrier", later));
            first.AllowSerialization.TrySetResult(1);
            await later.SerializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(flush.IsCompletedSuccessfully);
        }
        finally
        {
            first.AllowSerialization.TrySetResult(1);
            later.AllowSerialization.TrySetResult(1);
        }
    }

    [Fact]
    public async Task BoundedOverloadNeverBlocksProducerAndWritesAnExplicitLossRecord()
    {
        using var directory = new TempDirectory();
        var blocker = new BlockingPayload();
        await using var log = new LocalActivityLog(directory.FullName, queueCapacity: 2);
        try
        {
            Assert.True(log.TryRecord("blocking", blocker));
            await blocker.SerializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(log.TryRecord("accepted", new { Value = 2 }));
            Assert.True(log.TryRecord("accepted", new { Value = 3 }));
            Assert.False(log.TryRecord("dropped", new { Value = 4 }));
            Assert.Equal(1, log.DroppedRecords);
            Assert.True(log.IsBackpressured);

            blocker.AllowSerialization.SetResult(1);
            await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var rows = ReadAllLines(log.CurrentPath!).Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var loss = Assert.Single(rows, row => row.RootElement.GetProperty("kind").GetString() == "record-loss");
                Assert.Equal(4, loss.RootElement.GetProperty("sequence").GetInt64());
                Assert.Equal(1, loss.RootElement.GetProperty("data").GetProperty("count").GetInt64());
                Assert.Equal(4, loss.RootElement.GetProperty("data").GetProperty("firstSequence").GetInt64());
                Assert.Equal(4, loss.RootElement.GetProperty("data").GetProperty("lastSequence").GetInt64());
            }
            finally
            {
                foreach (var row in rows) row.Dispose();
            }
        }
        finally
        {
            blocker.AllowSerialization.TrySetResult(1);
        }
    }

    [Fact]
    public async Task FlushCompletesWithObservableIoFailure()
    {
        using var root = new TempDirectory();
        var occupiedPath = Path.Combine(root.FullName, "not-a-directory");
        await File.WriteAllTextAsync(occupiedPath, "occupied");
        await using var log = new LocalActivityLog(occupiedPath);
        Assert.True(log.TryRecord("cannot-write", new { Value = 1 }));

        await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(log.LastError);
        Assert.Null(log.CurrentPath);
    }

    [Fact]
    public async Task RotationRetainsOnlyThisSessionsConfiguredFiles()
    {
        using var directory = new TempDirectory();
        var unrelated = Path.Combine(directory.FullName, "activity-another-running-session-0001.jsonl");
        await File.WriteAllTextAsync(unrelated, "do-not-touch");
        await using var log = new LocalActivityLog(directory.FullName, maxFileBytes: 1, retainedFileCount: 2);
        var rotatedPaths = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < 6; index++)
        {
            Assert.True(log.TryRecord("large", new { Index = index, Text = new string('x', 160) }));
            await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(log.CurrentPath);
            Assert.True(rotatedPaths.Add(log.CurrentPath!),
                $"Record {index} did not rotate to a new file: {log.CurrentPath}");
        }

        Assert.Equal("do-not-touch", await File.ReadAllTextAsync(unrelated));
        var owned = Directory.GetFiles(directory.FullName, "activity-*.jsonl")
            .Where(path => !StringComparer.Ordinal.Equals(path, unrelated)).ToArray();
        Assert.Equal(2, owned.Length);
        Assert.Contains(log.CurrentPath!, owned);
        Assert.Null(log.LastError);
    }

    private static string[] ReadAllLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines.ToArray();
    }

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory() => FullName = Directory.CreateTempSubdirectory("orand-activity-").FullName;
        internal string FullName { get; }
        public void Dispose() => Directory.Delete(FullName, recursive: true);
    }

    private sealed class BlockingPayload
    {
        internal TaskCompletionSource SerializationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<int> AllowSerialization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Value
        {
            get
            {
                SerializationStarted.TrySetResult();
                return AllowSerialization.Task.GetAwaiter().GetResult();
            }
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(TimeSpan duration)
        {
            _utcNow += duration;
            _timestamp += duration.Ticks;
        }
    }
}
