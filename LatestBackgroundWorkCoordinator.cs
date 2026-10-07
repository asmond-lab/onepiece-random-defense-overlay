namespace OrandOverlay;

internal sealed class LatestBackgroundWorkCoordinator
{
    internal static TimeSpan DefaultSettleDelay => TimeSpan.FromMilliseconds(75);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<CancellationToken, Task> _settle;
    private CancellationTokenSource? _latest;

    public LatestBackgroundWorkCoordinator()
        : this(cancellation => Task.Delay(DefaultSettleDelay, cancellation))
    {
    }

    internal LatestBackgroundWorkCoordinator(Func<CancellationToken, Task> settle) =>
        _settle = settle;

    public async Task<T?> RunAsync<T>(Func<T> work) where T : class
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _latest, cancellation);
        previous?.Cancel();
        var entered = false;
        try
        {
            await _settle(cancellation.Token);
            await _gate.WaitAsync(cancellation.Token);
            entered = true;
            if (cancellation.IsCancellationRequested) return null;

            var result = await Task.Run(() =>
            {
                var thread = Thread.CurrentThread;
                var previousPriority = thread.Priority;
                try
                {
                    thread.Priority = ThreadPriority.BelowNormal;
                    return work();
                }
                finally
                {
                    thread.Priority = previousPriority;
                }
            });
            return cancellation.IsCancellationRequested ? null : result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (entered) _gate.Release();
            Interlocked.CompareExchange(ref _latest, null, cancellation);
            cancellation.Dispose();
        }
    }
}
