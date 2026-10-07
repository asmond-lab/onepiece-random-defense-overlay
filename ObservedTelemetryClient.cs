using System.Collections.Immutable;
using System.Text.Json;

namespace OrandOverlay;

public sealed class ObservedTelemetryClient : IAsyncDisposable
{
    public const int MaxPendingMemoryPackets = 256;
    private readonly Func<bool> _permission;
    private readonly ObservedTelemetryMetadata _metadata;
    private readonly ObservedTelemetryOutbox _outbox;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Queue<ObservedTelemetryPacket> _pending = new();
    private readonly Dictionary<string, (string Key, DateTimeOffset At)> _last = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _persistSignal = new(0, 1), _transportSignal = new(0, 1), _persistGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new(), _timerStop = new();
    private Task? _writer, _sender, _timer;
    private string _sessionId = Guid.NewGuid().ToString("N");
    private DateTimeOffset _sessionStarted;
    private long _sequence;
    private int _started, _shutdown, _backpressured;
    private Exception? _lastError;
    public string SessionId { get { lock (_gate) return _sessionId; } }
    public bool IsBackpressured => Volatile.Read(ref _backpressured) != 0;
    public Exception? LastError => _lastError ?? _outbox.LastError;
    public int PendingMemoryPacketCount { get { lock (_gate) return _pending.Count; } }
    public long AcceptedPacketCount => _outbox.AcceptedPacketCount;
    public DateTimeOffset? LastAcknowledgedAt => _outbox.LastAcknowledgedAt;

    public ObservedTelemetryClient(Func<bool> permission, ObservedTelemetryMetadata metadata,
        ObservedTelemetryOutbox outbox, TimeProvider? timeProvider = null)
    {
        _permission = permission;
        _metadata = metadata;
        _outbox = outbox;
        _time = timeProvider ?? TimeProvider.System;
        _sessionStarted = _time.GetUtcNow();
        ObservedTelemetryWire.Validate(Packet(new() { Kind = "session", State = "session-start", Sequence = 1 }));
    }

    public void Start()
    {
        if (!_permission() || Volatile.Read(ref _shutdown) != 0 || Interlocked.Exchange(ref _started, 1) != 0) return;
        Record(new() { Kind = "session", State = "session-start" });
        _writer = Task.Run(WriterAsync);
        _sender = Task.Run(SenderAsync);
        _timer = Task.Run(TimerAsync);
        Signal(_persistSignal);
        Signal(_transportSignal);
    }

    public bool Record(ObservedTelemetryEvent observation)
    {
        if (Volatile.Read(ref _shutdown) != 0 || !_permission()) return false;
        lock (_gate)
        {
            if (Volatile.Read(ref _shutdown) != 0) return false;
            var now = _time.GetUtcNow();
            var value = observation with { Sequence = _sequence + 1,
                ElapsedMs = Math.Max(0, (long)(now - _sessionStarted).TotalMilliseconds) };
            if (!ObservedTelemetryWire.IsValidEvent(value)) return false;
            var contentKey = JsonSerializer.Serialize(value with { Sequence = 1, ElapsedMs = 0,
                SourceRevision = null, DurationMs = null, AgeMs = null, Inventory = null }, ObservedTelemetryWire.JsonOptions);
            if (value.Inventory is not null) contentKey += string.Join(";", value.Inventory.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
            var lane = value.Kind + ":" + value.Lane;
            if (_last.TryGetValue(lane, out var previous) && previous.Key == contentKey && now - previous.At < TimeSpan.FromSeconds(30)) return false;
            if (_pending.Count >= MaxPendingMemoryPackets)
            { Volatile.Write(ref _backpressured, 1); return false; }
            _pending.Enqueue(Packet(value));
            _sequence++;
            _last[lane] = (contentKey, now);
        }
        Signal(_persistSignal);
        return true;
    }

    public void ResetSession()
    {
        lock (_gate)
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _sessionStarted = _time.GetUtcNow();
            _sequence = 0;
            _last.Clear();
            if (_started != 0) Record(new() { Kind = "session", State = "session-start" });
        }
    }

    private ObservedTelemetryPacket Packet(ObservedTelemetryEvent value) => new()
    {
        PacketId = Guid.NewGuid().ToString("N"), SessionId = _sessionId,
        AppVersion = _metadata.AppVersion, DatasetFingerprint = _metadata.DatasetFingerprint,
        GameVersion = _metadata.GameVersion, Source = _metadata.Source, Events = [value]
    };

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await PersistPendingAsync(cancellationToken).ConfigureAwait(false);
        await _outbox.FlushAsync(cancellationToken).ConfigureAwait(false);
        await PersistPendingAsync(cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    private async Task PersistPendingAsync(CancellationToken token)
    {
        await _persistGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            while (_permission())
            {
                ObservedTelemetryPacket? packet;
                lock (_gate) packet = _pending.TryPeek(out var first) ? first : null;
                if (packet is null) { Volatile.Write(ref _backpressured, 0); return; }
                if (!await _outbox.StageAsync(packet, token).ConfigureAwait(false))
                { _lastError = _outbox.LastError; Volatile.Write(ref _backpressured, 1); return; }
                lock (_gate) _pending.Dequeue();
                _lastError = null;
                Volatile.Write(ref _backpressured, 0);
            }
        }
        finally { _persistGate.Release(); }
    }

    private async Task WriterAsync()
    {
        try
        {
            while (true)
            {
                await _persistSignal.WaitAsync(_stop.Token).ConfigureAwait(false);
                await PersistPendingAsync(_stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { _lastError = error; }
    }

    private async Task SenderAsync()
    {
        try
        {
            while (true)
            {
                await _transportSignal.WaitAsync(_stop.Token).ConfigureAwait(false);
                await _outbox.FlushAsync(_stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { _lastError = error; }
    }

    private async Task TimerAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(_timerStop.Token).ConfigureAwait(false))
            {
                Signal(_persistSignal);
                Signal(_transportSignal);
            }
        }
        catch (OperationCanceledException) when (_timerStop.IsCancellationRequested) { }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_shutdown != 0) return;
            if (_started == 0) { _shutdown = 1; return; }
            Record(new() { Kind = "session", State = "app-exit" });
            _shutdown = 1;
        }
        _timerStop.Cancel();
        try { await FlushAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            _stop.Cancel();
            var workers = new[] { _writer, _sender, _timer }.Where(task => task is not null).Cast<Task>();
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
    private static void Signal(SemaphoreSlim signal)
    {
        try { signal.Release(); }
        catch (SemaphoreFullException) { }
    }
}
