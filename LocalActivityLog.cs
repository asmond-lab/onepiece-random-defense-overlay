using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

/// <summary>
/// A local-only, bounded activity stream. Producers only enqueue immutable snapshots; one worker
/// serializes and appends them. Files rotate at 4 MiB and this process session retains four files.
/// </summary>
internal sealed class LocalActivityLog : IAsyncDisposable
{
    internal const int DefaultQueueCapacity = 256;
    internal const long DefaultMaxFileBytes = 4L * 1024 * 1024;
    internal const int DefaultRetainedFileCount = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly int _queueCapacity;
    private readonly long _maxFileBytes;
    private readonly int _retainedFileCount;
    private readonly Guid _applicationSessionId = Guid.NewGuid();
    private readonly long _startedTimestamp;
    private readonly Queue<WorkItem> _work = new();
    private readonly List<FlushWaiter> _flushWaiters = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Task _worker;
    private FileStream? _stream;
    private string? _currentPath;
    private Exception? _lastError;
    private long _latestSequence;
    private long _completedThrough;
    private long _flushedThrough;
    private long _droppedRecords;
    private int _admittedCount;
    private int _fileIndex;
    private bool _isBackpressured;
    private bool _disposed;

    internal LocalActivityLog(
        string absoluteDirectory,
        TimeProvider? timeProvider = null,
        int queueCapacity = DefaultQueueCapacity,
        long maxFileBytes = DefaultMaxFileBytes,
        int retainedFileCount = DefaultRetainedFileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteDirectory);
        if (!Path.IsPathFullyQualified(absoluteDirectory))
            throw new ArgumentException("An absolute activity-log directory is required.", nameof(absoluteDirectory));
        if (queueCapacity < 1) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        if (maxFileBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        if (retainedFileCount < 1) throw new ArgumentOutOfRangeException(nameof(retainedFileCount));

        _directory = absoluteDirectory;
        _time = timeProvider ?? TimeProvider.System;
        _queueCapacity = queueCapacity;
        _maxFileBytes = maxFileBytes;
        _retainedFileCount = retainedFileCount;
        _startedTimestamp = _time.GetTimestamp();
        _worker = Task.Run(WriteLoopAsync);
    }

    internal string? CurrentPath { get { lock (_gate) return _currentPath; } }
    internal Exception? LastError { get { lock (_gate) return _lastError; } }
    internal long DroppedRecords { get { lock (_gate) return _droppedRecords; } }
    internal bool IsBackpressured { get { lock (_gate) return _isBackpressured; } }

    internal bool TryRecord(
        string kind,
        object data,
        long? matchGeneration = null,
        long? recognitionRevision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(data);

        lock (_gate)
        {
            if (_disposed) return false;
            var sequence = ++_latestSequence;
            var receivedAtUtc = _time.GetUtcNow();
            var elapsed = _time.GetElapsedTime(_startedTimestamp, _time.GetTimestamp()).TotalMilliseconds;
            if (_admittedCount >= _queueCapacity)
            {
                _droppedRecords++;
                _isBackpressured = true;
                if (_work.LastOrDefault() is LossWork loss && loss.LastSequence + 1 == sequence)
                    loss.Extend(sequence);
                else
                    _work.Enqueue(new LossWork(sequence, receivedAtUtc, elapsed));
                SignalWorker();
                return false;
            }

            _work.Enqueue(new RecordWork(sequence, receivedAtUtc, elapsed, kind, data,
                matchGeneration, recognitionRevision));
            _admittedCount++;
            SignalWorker();
            return true;
        }
    }

    internal Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (_gate)
        {
            if (_flushedThrough >= _latestSequence || _disposed && _worker.IsCompleted)
                return Task.CompletedTask;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _flushWaiters.Add(new FlushWaiter(_latestSequence, completion));
            task = completion.Task;
            SignalWorker();
        }
        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    public async ValueTask DisposeAsync()
    {
        Task flush;
        lock (_gate)
        {
            if (_disposed)
            {
                flush = _worker;
            }
            else
            {
                _disposed = true;
                if (_flushedThrough >= _latestSequence)
                    flush = Task.CompletedTask;
                else
                {
                    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _flushWaiters.Add(new FlushWaiter(_latestSequence, completion));
                    flush = completion.Task;
                }
                SignalWorker();
            }
        }

        await flush.ConfigureAwait(false);
        await _worker.ConfigureAwait(false);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync().ConfigureAwait(false);
                while (true)
                {
                    WorkItem? work = null;
                    long flushTarget = 0;
                    lock (_gate)
                    {
                        flushTarget = HighestReadyFlushTarget();
                        if (flushTarget == 0 && _work.TryDequeue(out work) && work is RecordWork)
                            _admittedCount--;
                        if (work is null && flushTarget == 0 && _disposed &&
                            _completedThrough >= _latestSequence)
                            return;
                        if (work is null && flushTarget == 0) break;
                    }

                    if (work is not null)
                    {
                        try { await WriteAsync(work).ConfigureAwait(false); }
                        catch (Exception exception) { RecordFailure(exception); }
                        lock (_gate)
                        {
                            _completedThrough = work.LastSequence;
                            if (work is LossWork && !_work.Any(item => item is LossWork))
                                _isBackpressured = false;
                        }
                    }
                    else
                    {
                        try
                        {
                            if (_stream is not null) await _stream.FlushAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception) { RecordFailure(exception); }
                        lock (_gate)
                        {
                            _flushedThrough = Math.Max(_flushedThrough, flushTarget);
                            CompleteFlushWaiters();
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
        }
        finally
        {
            try
            {
                if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
            }
            lock (_gate)
            {
                // A terminal worker or close failure satisfies barriers through observable LastError,
                // rather than leaving shutdown callers waiting for work that can no longer run.
                _disposed = true;
                _completedThrough = Math.Max(_completedThrough, _latestSequence);
                _flushedThrough = Math.Max(_flushedThrough, _completedThrough);
                CompleteFlushWaiters();
            }
        }
    }

    private long HighestReadyFlushTarget()
    {
        var target = 0L;
        foreach (var waiter in _flushWaiters)
            if (waiter.Target <= _completedThrough) target = Math.Max(target, waiter.Target);
        return target > _flushedThrough ? target : 0;
    }

    private void CompleteFlushWaiters()
    {
        for (var index = _flushWaiters.Count - 1; index >= 0; index--)
        {
            if (_flushWaiters[index].Target > _flushedThrough) continue;
            _flushWaiters[index].Completion.TrySetResult();
            _flushWaiters.RemoveAt(index);
        }
    }

    private async Task WriteAsync(WorkItem work)
    {
        object data;
        string kind;
        long? matchGeneration;
        long? recognitionRevision;
        if (work is RecordWork record)
        {
            data = record.Data;
            kind = record.Kind;
            matchGeneration = record.MatchGeneration;
            recognitionRevision = record.RecognitionRevision;
        }
        else
        {
            var loss = (LossWork)work;
            data = new
            {
                count = loss.LastSequence - loss.Sequence + 1,
                firstSequence = loss.Sequence,
                lastSequence = loss.LastSequence
            };
            kind = "record-loss";
            matchGeneration = null;
            recognitionRevision = null;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            applicationSessionId = _applicationSessionId,
            sequence = work.Sequence,
            receivedAtUtc = work.ReceivedAtUtc,
            elapsedMilliseconds = work.ElapsedMilliseconds,
            kind,
            matchGeneration,
            recognitionRevision,
            data
        }, JsonOptions);
        await EnsureStreamAsync(bytes.LongLength + 1).ConfigureAwait(false);
        await _stream!.WriteAsync(bytes).ConfigureAwait(false);
        await _stream.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
    }

    private async Task EnsureStreamAsync(long nextRecordBytes)
    {
        if (_stream is not null && _stream.Length > 0 && _stream.Length + nextRecordBytes > _maxFileBytes)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }
        if (_stream is not null) return;

        Directory.CreateDirectory(_directory);
        var index = ++_fileIndex;
        var fileName = $"activity-{_applicationSessionId:N}-{index:D4}.jsonl";
        var path = Path.Combine(_directory, fileName);
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        lock (_gate) _currentPath = path;

        var expiredIndex = index - _retainedFileCount;
        if (expiredIndex > 0)
        {
            var expired = Path.Combine(_directory,
                $"activity-{_applicationSessionId:N}-{expiredIndex:D4}.jsonl");
            if (File.Exists(expired)) File.Delete(expired);
        }
    }

    private void RecordFailure(Exception exception)
    {
        lock (_gate) _lastError = exception;
    }

    private void SignalWorker()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { }
    }

    private abstract class WorkItem(long sequence, DateTimeOffset receivedAtUtc, double elapsedMilliseconds)
    {
        internal long Sequence { get; } = sequence;
        internal virtual long LastSequence => Sequence;
        internal DateTimeOffset ReceivedAtUtc { get; } = receivedAtUtc;
        internal double ElapsedMilliseconds { get; } = elapsedMilliseconds;
    }

    private sealed class RecordWork(
        long sequence,
        DateTimeOffset receivedAtUtc,
        double elapsedMilliseconds,
        string kind,
        object data,
        long? matchGeneration,
        long? recognitionRevision) : WorkItem(sequence, receivedAtUtc, elapsedMilliseconds)
    {
        internal string Kind { get; } = kind;
        internal object Data { get; } = data;
        internal long? MatchGeneration { get; } = matchGeneration;
        internal long? RecognitionRevision { get; } = recognitionRevision;
    }

    private sealed class LossWork(long sequence, DateTimeOffset receivedAtUtc, double elapsedMilliseconds)
        : WorkItem(sequence, receivedAtUtc, elapsedMilliseconds)
    {
        private long _lastSequence = sequence;
        internal override long LastSequence => _lastSequence;
        internal void Extend(long sequence) => _lastSequence = sequence;
    }

    private sealed record FlushWaiter(long Target, TaskCompletionSource Completion);
}
