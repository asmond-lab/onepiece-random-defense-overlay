using System.Net.Http;

namespace OrandOverlay;

/// <summary>Checkpoints gameplay to disk independently from bounded, best-effort transport.</summary>
public sealed class GameplayTelemetryClient(Func<bool> permission, GameplaySessionRecorder recorder,
    GameplayTelemetryOutbox outbox, TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _persistenceSignal = new(0, 1);
    private readonly SemaphoreSlim _transportSignal = new(0, 1);
    private readonly CancellationTokenSource _timerStop = new();
    private readonly CancellationTokenSource _transportStop = new();
    private readonly Queue<GameplayTelemetryPacket> _pendingSealed = new();
    private readonly HashSet<string> _pendingIds = new(StringComparer.Ordinal);
    private GameplayTelemetryPacket? _pendingDraft;
    private Task? _persistenceWorker, _transportWorker, _timer;
    private int _flushRequested, _started, _shutdown, _finalPersistRequested, _backpressured, _pendingMemoryPackets;
    private long _backpressureCount, _persistenceFailureCount, _transportFailureCount;

    public GameplaySessionRecorder Recorder => recorder;
    public Exception? LastError { get; private set; }
    public bool IsBackpressured => Volatile.Read(ref _backpressured) != 0;
    public int PendingMemoryPacketCount => Volatile.Read(ref _pendingMemoryPackets);
    public long BackpressureCount => Interlocked.Read(ref _backpressureCount);
    public long PersistenceFailureCount => Interlocked.Read(ref _persistenceFailureCount);
    public long TransportFailureCount => Interlocked.Read(ref _transportFailureCount);
    public long PausedObservationCount => recorder.PausedObservationCount;
    public long ExpiredFileCount => outbox.ExpiredFileCount;
    public long CapacityRefusalCount => outbox.CapacityRefusalCount;

    public void Start()
    {
        if (!permission() || Volatile.Read(ref _shutdown) != 0 || Interlocked.Exchange(ref _started, 1) != 0) return;
        _persistenceWorker = Task.Run(PersistenceWorkerAsync);
        _transportWorker = Task.Run(TransportWorkerAsync);
        _timer = Task.Run(TimerAsync);
        // Preserve startup retry behavior. This seals only work captured before Start, never each observation wake.
        Interlocked.Exchange(ref _flushRequested, 1);
        SignalPersistence();
    }

    public void NotifyObservation() => SignalPersistence();

    public void RequestFlush()
    {
        Interlocked.Exchange(ref _flushRequested, 1);
        SignalPersistence();
    }

    private void SignalPersistence()
    {
        if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _shutdown) != 0) return;
        Release(_persistenceSignal);
    }

    private void SignalTransport()
    {
        if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _shutdown) != 0) return;
        Release(_transportSignal);
    }

    private static void Release(SemaphoreSlim signal)
    {
        try { signal.Release(); }
        catch (SemaphoreFullException) { }
    }

    private async Task TimerAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(_timerStop.Token).ConfigureAwait(false)) RequestFlush();
        }
        catch (OperationCanceledException) when (_timerStop.IsCancellationRequested) { }
    }

    private async Task PersistenceWorkerAsync()
    {
        var startup = true;
        try
        {
            if (permission())
            {
                var recoveryErrors = outbox.ErrorCount;
                await outbox.RecoverDraftsAsync().ConfigureAwait(false);
                if (outbox.ErrorCount != recoveryErrors && outbox.LastError is { } recoveryError)
                    RecordPersistenceError(recoveryError);
            }
            while (true)
            {
                await _persistenceSignal.WaitAsync().ConfigureAwait(false);
                var finalPass = Interlocked.Exchange(ref _finalPersistRequested, 0) != 0;
                var seal = finalPass || Interlocked.Exchange(ref _flushRequested, 0) != 0;
                var wasBackpressured = IsBackpressured;
                var result = await PersistCheckpointAsync(seal).ConfigureAwait(false);
                // Existing durable packets must still be retried when new persistence is full.
                // Only the first backpressure edge wakes HTTP, avoiding an offline busy loop.
                if (startup || seal || result.SealedBecameDurable || !wasBackpressured && IsBackpressured)
                    SignalTransport();
                startup = false;
                if (finalPass) break;
                // A pass already in progress when Complete ran must not consume the final checkpoint request.
                if (Volatile.Read(ref _shutdown) != 0) continue;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        {
            RecordPersistenceError(e);
            SetBackpressure(true);
        }
    }

    private async Task<(bool Succeeded, bool SealedBecameDurable)> PersistCheckpointAsync(bool seal)
    {
        try
        {
            if (!permission())
            {
                recorder.Checkpoint(false);
                _pendingSealed.Clear();
                _pendingIds.Clear();
                _pendingDraft = null;
                UpdatePendingCount();
                SetBackpressure(false);
                return (false, false);
            }

            var recoveryErrors = outbox.ErrorCount;
            var restoredPackets = seal ? await outbox.RestorePendingPacketsAsync().ConfigureAwait(false) : 0;
            if (outbox.ErrorCount != recoveryErrors && outbox.LastError is { } recoveryError)
                RecordPersistenceError(recoveryError);
            if (PendingMemoryPacketCount >= GameplayTelemetryOutbox.MaxPackets - 1) SetBackpressure(true);
            var checkpoint = recorder.Checkpoint(seal);
            foreach (var packet in checkpoint.SealedPackets)
            {
                if (_pendingDraft?.PacketId == packet.PacketId) _pendingDraft = null;
                if (_pendingIds.Add(packet.PacketId)) _pendingSealed.Enqueue(packet);
            }
            if (checkpoint.Draft is { } draft && !_pendingIds.Contains(draft.PacketId))
            {
                if (_pendingDraft is null || _pendingDraft.PacketId == draft.PacketId ||
                    _pendingDraft.ChunkIndex <= draft.ChunkIndex) _pendingDraft = draft;
            }
            UpdatePendingCount();
            if (PendingMemoryPacketCount >= GameplayTelemetryOutbox.MaxPackets - 1) SetBackpressure(true);

            var sealedDurable = restoredPackets > 0;
            while (_pendingSealed.TryPeek(out var packet))
            {
                if (!permission()) return (false, sealedDurable);
                if (!await outbox.EnqueueAsync(packet).ConfigureAwait(false))
                {
                    if (!permission()) return (false, sealedDurable);
                    RecordPersistenceError(outbox.LastError ?? new IOException("Gameplay sealed packet could not be persisted."));
                    SetBackpressure(true);
                    return (false, sealedDurable);
                }
                _pendingSealed.Dequeue();
                _pendingIds.Remove(packet.PacketId);
                sealedDurable = true;
                UpdatePendingCount();
            }

            if (_pendingDraft is { } pendingDraft)
            {
                if (!await outbox.SaveDraftAsync(pendingDraft).ConfigureAwait(false))
                {
                    if (!permission()) return (false, sealedDurable);
                    RecordPersistenceError(outbox.LastError ?? new IOException("Gameplay draft checkpoint could not be persisted."));
                    SetBackpressure(true);
                    return (false, sealedDurable);
                }
                _pendingDraft = null;
                UpdatePendingCount();
            }

            SetBackpressure(false);
            return (true, sealedDurable);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        {
            RecordPersistenceError(e);
            SetBackpressure(true);
            return (false, false);
        }
    }

    private async Task TransportWorkerAsync()
    {
        try
        {
            while (true)
            {
                await _transportSignal.WaitAsync(_transportStop.Token).ConfigureAwait(false);
                if (!permission()) continue;
                var errors = outbox.ErrorCount;
                await outbox.FlushAsync(_transportStop.Token).ConfigureAwait(false);
                if (outbox.ErrorCount != errors)
                {
                    Interlocked.Increment(ref _transportFailureCount);
                    if (outbox.LastError is { } error) RecordError(error);
                }
                // A successful acknowledgement may have made room for the exact RAM packet we retained.
                if (IsBackpressured) SignalPersistence();
            }
        }
        catch (OperationCanceledException) when (_transportStop.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or
                                       ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Interlocked.Increment(ref _transportFailureCount);
            RecordError(e);
        }
    }

    private void UpdatePendingCount()
    {
        var count = _pendingSealed.Count + (_pendingDraft is null ? 0 : 1);
        Volatile.Write(ref _pendingMemoryPackets, count);
    }

    private void SetBackpressure(bool value)
    {
        var next = value ? 1 : 0;
        if (Interlocked.Exchange(ref _backpressured, next) != next && value)
            Interlocked.Increment(ref _backpressureCount);
        recorder.CapturePaused = value;
    }

    private void RecordPersistenceError(Exception error)
    {
        Interlocked.Increment(ref _persistenceFailureCount);
        RecordError(error);
    }

    private void RecordError(Exception error) => LastError = error;

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _started) == 0 && permission()) Start();
        if (Interlocked.Exchange(ref _shutdown, 1) != 0) return;

        recorder.Complete("interrupted", "appExit");
        Interlocked.Exchange(ref _finalPersistRequested, 1);
        await _timerStop.CancelAsync().ConfigureAwait(false);
        Release(_persistenceSignal);

        if (_persistenceWorker is not null) await _persistenceWorker.ConfigureAwait(false);
        if (_timer is not null) await _timer.ConfigureAwait(false);

        try
        {
        await _transportStop.CancelAsync().ConfigureAwait(false);
        Release(_transportSignal);
        if (_transportWorker is not null)
        {
            try { await _transportWorker.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException) { RecordError(new TimeoutException("Gameplay transport stop timed out; durable packets remain queued.")); return; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { RecordError(new TimeoutException("Gameplay transport stop cancelled; durable packets remain queued.")); return; }
        }

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var errors = outbox.ErrorCount;
            await outbox.FlushAsync(bounded.Token).ConfigureAwait(false);
            if (outbox.ErrorCount != errors && outbox.LastError is { } error)
            {
                Interlocked.Increment(ref _transportFailureCount);
                RecordError(error);
            }
        }
        catch (OperationCanceledException)
        { RecordError(new TimeoutException("Gameplay shutdown transport timed out; durable packets remain queued.")); }
        }
        finally
        {
            // The bounded upload may have freed capacity after the first final save failed.
            // Always give retained RAM packets a final durable hand-off, even on timeout.
            await PersistCheckpointAsync(true).ConfigureAwait(false);
        }
    }

    /// <summary>Exit-only snapshot, after the persistence worker has stopped. The caller
    /// may escrow these exact sealed packets locally if the primary queue is still full.</summary>
    public IReadOnlyList<GameplayTelemetryPacket> UnpersistedPacketsAfterShutdown()
    {
        if (Volatile.Read(ref _shutdown) == 0 || _persistenceWorker is { IsCompleted: false })
            throw new InvalidOperationException("Pending recovery is available only after shutdown persistence has stopped.");
        return _pendingSealed.Concat(_pendingDraft is { } draft ? new[] { draft } : Array.Empty<GameplayTelemetryPacket>()).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        // Workers are stopped before these are eligible for collection; avoid dispose races with a misbehaving handler.
    }
}
