using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace OrandOverlay;

internal static class DiagnosticRecognitionStream
{
    internal static IAsyncEnumerable<T> Run<T>(Action<Action<T>, CancellationToken> producer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(producer);
        return Run<T>((emit, _, token) => producer(emit, token), cancellationToken);
    }

    internal static async IAsyncEnumerable<T> Run<T>(
        Action<Action<T>, Func<Func<T>, bool>, CancellationToken> producer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(producer);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var admission = new SemaphoreSlim(1, 1);
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
        {
            SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        var worker = Task.Run(() =>
        {
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();
                void Emit(T frame)
                {
                    admission.Wait(lifetime.Token);
                    var enqueued = false;
                    try
                    {
                        if (!channel.Writer.TryWrite(frame))
                            throw new InvalidOperationException("Reserved diagnostic delivery capacity was unavailable.");
                        enqueued = true;
                    }
                    finally
                    {
                        if (!enqueued) admission.Release();
                    }
                }
                bool TryEmit(Func<T> factory)
                {
                    ArgumentNullException.ThrowIfNull(factory);
                    if (!admission.Wait(0, lifetime.Token)) return false;
                    var enqueued = false;
                    try
                    {
                        var frame = factory();
                        if (!channel.Writer.TryWrite(frame))
                            throw new InvalidOperationException("Reserved diagnostic delivery capacity was unavailable.");
                        enqueued = true;
                        return true;
                    }
                    finally
                    {
                        if (!enqueued) admission.Release();
                    }
                }
                producer(Emit, TryEmit, lifetime.Token);
                channel.Writer.TryComplete();
            }
            catch (Exception error) { channel.Writer.TryComplete(error); }
        });
        try
        {
            while (await channel.Reader.WaitToReadAsync(lifetime.Token).ConfigureAwait(false))
                while (channel.Reader.TryRead(out var frame))
                {
                    admission.Release();
                    yield return frame;
                }
        }
        finally
        {
            lifetime.Cancel();
            await worker.ConfigureAwait(false);
        }
    }
}

internal sealed class DiagnosticDiscoveryCheckpoint
{
    // Basic inventory is cheap compared with full owner discovery. Keep it responsive
    // during that sweep without queuing reads when the consumer has no capacity.
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private readonly Func<bool> _checkpoint;
    private readonly Func<TimeSpan> _elapsed;
    private TimeSpan _next;

    internal DiagnosticDiscoveryCheckpoint(Action checkpoint, Func<TimeSpan>? elapsed = null)
        : this(() =>
        {
            checkpoint();
            return true;
        }, elapsed)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
    }

    internal DiagnosticDiscoveryCheckpoint(Func<bool> checkpoint, Func<TimeSpan>? elapsed = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        _checkpoint = checkpoint;
        var started = Stopwatch.GetTimestamp();
        _elapsed = elapsed ?? (() => Stopwatch.GetElapsedTime(started));
        _next = _elapsed() + Interval;
    }

    internal void Poll()
    {
        var started = _elapsed();
        if (started < _next) return;
        if (!_checkpoint()) return;
        var completed = _elapsed();
        // On larger worlds, leave discovery time between basic reads rather than
        // increasing load indefinitely. Never exceed the previous 250 ms cooldown.
        var cooldown = TimeSpan.FromMilliseconds(Math.Clamp((completed - started).TotalMilliseconds * 4, 100, 250));
        _next = completed + cooldown;
    }
}
