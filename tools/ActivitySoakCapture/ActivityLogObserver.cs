using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

internal sealed class ActivityLogObserver : IAsyncDisposable
{
    private sealed class FileState(long offset, byte[] tail)
    {
        internal long Offset = offset;
        internal byte[] Tail = tail;
    }

    private readonly string _directory;
    private readonly ActivityLogObserverHooks? _hooks;
    private readonly Func<DateTimeOffset> _clock;
    private readonly DateTimeOffset _measurementStartedAtUtc;
    private readonly FileSystemWatcher _watcher;
    private readonly Channel<string> _changes = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<string, FileState> _files =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _initialFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _kinds =
        new(StringComparer.Ordinal);
    private readonly ActivityCadenceAccumulator _cadence = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _consumer;
    private readonly object _waitGate = new();
    private readonly List<(long Expected, TaskCompletionSource Signal)> _recordWaiters = [];
    private TaskCompletionSource _fileEvent =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _initialBytes;
    private long _validRecords;
    private long _malformed;
    private long _accepted;
    private long _rejected;
    private long _gaps;
    private long _losses;
    private long _errors;
    private readonly List<DateTimeOffset> _receipts = [];
    private DateTimeOffset? _firstRecord;
    private DateTimeOffset? _lastRecord;

    internal ActivityLogObserver(string directory, ActivityLogObserverHooks? hooks = null,
        DateTimeOffset? measurementStartedAtUtc = null, Func<DateTimeOffset>? clock = null)
    {
        _directory = Path.GetFullPath(directory);
        _hooks = hooks;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _measurementStartedAtUtc = measurementStartedAtUtc ?? _clock();
        if (!Directory.Exists(_directory))
            throw new DirectoryNotFoundException($"Activity log directory not found: {_directory}");
        foreach (var path in Files())
        {
            var bytes = ReadAllShared(path);
            var tailStart = LastLineEnd(bytes);
            _files[path] = new(bytes.LongLength, bytes[tailStart..]);
            _initialFiles.Add(path);
            _initialBytes += bytes.LongLength;
        }
        _watcher = new(_directory, "activity-*.jsonl")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size |
                NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.EnableRaisingEvents = true;
        _consumer = ConsumeAsync();
    }

    internal long ValidRecords => Interlocked.Read(ref _validRecords);

    internal Task WaitForRecordsAsync(long expected, CancellationToken token)
    {
        if (ValidRecords >= expected) return Task.CompletedTask;
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waitGate)
        {
            if (ValidRecords >= expected) return Task.CompletedTask;
            _recordWaiters.Add((expected, signal));
        }
        return signal.Task.WaitAsync(token);
    }

    internal Task WaitForFileEventAsync(CancellationToken token)
    {
        lock (_waitGate) return _fileEvent.Task.WaitAsync(token);
    }

    internal void RequestCatchUp() =>
        ActivityLogCatchUp.Queue(_directory, _files.Keys, Queue);

    internal async Task<ActivityLogMetrics> CompleteAsync()
    {
        _watcher.EnableRaisingEvents = false;
        foreach (var path in Files()) await _changes.Writer.WriteAsync(path);
        _changes.Writer.TryComplete();
        await _consumer;
        var finalFiles = Files();
        var finalBytes = finalFiles.Sum(path => new FileInfo(path).Length);
        var partial = _files.Values.Sum(state => (long)state.Tail.Length);
        var maximumGapSeconds = ReceiptCoverage.MaximumGapSeconds(
            _measurementStartedAtUtc, _receipts, _clock());
        return new(_initialBytes, finalBytes, finalBytes - _initialBytes,
            _initialFiles.Count, finalFiles.Length,
            finalFiles.Count(path => !_initialFiles.Contains(path)),
            _validRecords, _malformed, partial,
            _kinds.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            _accepted, _rejected, _gaps, _losses, _errors, _cadence.Snapshot(),
            _firstRecord, _lastRecord, maximumGapSeconds);
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => OnWatcherPath(args.FullPath);
    private void OnRenamed(object sender, RenamedEventArgs args) => OnWatcherPath(args.FullPath);
    private void OnWatcherPath(string path)
    {
        if (_hooks?.SuppressWatcherNotifications != true) Queue(path);
    }

    private void Queue(string path)
    {
        _changes.Writer.TryWrite(Path.GetFullPath(path));
        lock (_waitGate)
        {
            _fileEvent.TrySetResult();
            _fileEvent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private async Task ConsumeAsync()
    {
        await foreach (var path in _changes.Reader.ReadAllAsync(_lifetime.Token))
            Process(path);
    }

    private void Process(string path)
    {
        if (!File.Exists(path)) return;
        if (!_files.TryGetValue(path, out var state))
        {
            state = new(0, []);
            _files[path] = state;
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < state.Offset)
        {
            state.Offset = 0;
            state.Tail = [];
        }
        stream.Position = state.Offset;
        var capturedLength = stream.Length;
        var appended = new byte[checked((int)(capturedLength - state.Offset))];
        _hooks?.AfterLengthCaptured?.Invoke(path, capturedLength);
        stream.ReadExactly(appended);
        state.Offset = stream.Position;
        if (appended.Length == 0)
        {
            _hooks?.AfterRead?.Invoke(path, 0);
            return;
        }
        var combined = new byte[state.Tail.Length + appended.Length];
        state.Tail.CopyTo(combined, 0);
        appended.CopyTo(combined, state.Tail.Length);
        var lineStart = 0;
        for (var index = 0; index < combined.Length; index++)
        {
            if (combined[index] != (byte)'\n') continue;
            var length = index - lineStart;
            if (length > 0 && combined[index - 1] == (byte)'\r') length--;
            Parse(combined.AsSpan(lineStart, length));
            lineStart = index + 1;
        }
        state.Tail = combined[lineStart..];
        _hooks?.AfterRead?.Invoke(path, appended.Length);
    }

    private void Parse(ReadOnlySpan<byte> line)
    {
        if (line.IsEmpty) return;
        try
        {
            using var document = JsonDocument.Parse(line.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("kind", out var kindValue) ||
                kindValue.ValueKind != JsonValueKind.String) throw new JsonException();
            var kind = kindValue.GetString()!;
            _kinds.AddOrUpdate(kind, 1, (_, count) => checked(count + 1));
            _cadence.Add(kind, root);
            Interlocked.Increment(ref _validRecords);
            var now = _clock();
            _receipts.Add(now);
            _firstRecord ??= now;
            _lastRecord = now;
            if (kind == "memory.read" && root.TryGetProperty("data", out var memory) &&
                memory.TryGetProperty("accepted", out var accepted) &&
                accepted.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                if (accepted.GetBoolean()) _accepted++;
                else _rejected++;
            }
            if (kind == "observation.gap")
            {
                _gaps++;
                if (root.TryGetProperty("data", out var gap) &&
                    gap.TryGetProperty("reason", out var reason) &&
                    reason.GetString() == "local-recording-loss") _losses++;
            }
            if (kind == "record-loss") _losses++;
            if (kind is "writer.error" or "recording.error") _errors++;
            if (kind == "app.stop" && root.TryGetProperty("data", out var stop) &&
                stop.TryGetProperty("droppedRecords", out var dropped) &&
                dropped.TryGetInt64(out var count) && count > 0) _losses++;
            ReleaseRecordWaiters();
        }
        catch (JsonException)
        {
            Interlocked.Increment(ref _malformed);
        }
    }

    private void ReleaseRecordWaiters()
    {
        lock (_waitGate)
        {
            for (var index = _recordWaiters.Count - 1; index >= 0; index--)
            {
                if (ValidRecords < _recordWaiters[index].Expected) continue;
                _recordWaiters[index].Signal.TrySetResult();
                _recordWaiters.RemoveAt(index);
            }
        }
    }

    private string[] Files() => Directory.GetFiles(_directory, "activity-*.jsonl")
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static byte[] ReadAllShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static int LastLineEnd(byte[] bytes)
    {
        for (var index = bytes.Length - 1; index >= 0; index--)
            if (bytes[index] == (byte)'\n') return index + 1;
        return 0;
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _changes.Writer.TryComplete();
        _lifetime.Cancel();
        try { await _consumer; }
        catch (OperationCanceledException) { }
        _lifetime.Dispose();
    }
}
