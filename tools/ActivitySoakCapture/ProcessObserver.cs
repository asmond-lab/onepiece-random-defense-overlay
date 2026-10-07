using System.Diagnostics;

internal sealed class ProcessObserver : IDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource<DateTimeOffset> _exited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<double> _cpu = [];
    private readonly List<long> _working = [];
    private readonly List<long> _private = [];
    private int _responsive;
    private int _unresponsive;

    internal ProcessIdentity Identity { get; }
    internal Task<DateTimeOffset> ExitTask => _exited.Task;

    internal ProcessObserver(string role, int pid, DateTimeOffset expectedStartUtc)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid), "PID must be positive.");
        _process = Process.GetProcessById(pid);
        _process.Refresh();
        var actual = _process.StartTime.ToUniversalTime();
        if (actual != expectedStartUtc.UtcDateTime)
            throw new InvalidOperationException(
                $"{role} identity mismatch: PID {pid} started {actual:o}, expected {expectedStartUtc.UtcDateTime:o}.");
        Identity = new(role, pid, expectedStartUtc.ToUniversalTime(), actual,
            _process.MainModule?.FileName ?? "");
        _process.EnableRaisingEvents = true;
        _process.Exited += OnExited;
        if (_process.HasExited) OnExited(_process, EventArgs.Empty);
    }

    internal async Task SampleAsync(
        TimeSpan interval, CancellationToken token, Action? afterTick = null)
    {
        using var timer = new PeriodicTimer(interval);
        var previousCpu = _process.TotalProcessorTime;
        var previousAt = Stopwatch.GetTimestamp();
        while (await timer.WaitForNextTickAsync(token))
        {
            try
            {
                _process.Refresh();
                if (_process.HasExited) break;
                var now = Stopwatch.GetTimestamp();
                var totalCpu = _process.TotalProcessorTime;
                var wall = Stopwatch.GetElapsedTime(previousAt, now).TotalSeconds;
                var cpuSeconds = (totalCpu - previousCpu).TotalSeconds;
                _cpu.Add(wall <= 0 ? 0 : Math.Max(0,
                    cpuSeconds / wall / Environment.ProcessorCount * 100));
                _working.Add(_process.WorkingSet64);
                _private.Add(_process.PrivateMemorySize64);
                if (_process.Responding) _responsive++;
                else _unresponsive++;
                previousCpu = totalCpu;
                previousAt = now;
                afterTick?.Invoke();
            }
            catch (InvalidOperationException)
            {
                if (!_exited.Task.IsCompleted) throw;
                break;
            }
        }
    }

    internal ProcessMetrics Snapshot(DateTimeOffset completedAtUtc)
    {
        DateTimeOffset? exitedAt = _exited.Task.IsCompletedSuccessfully
            ? _exited.Task.Result
            : null;
        return new(Identity, true, exitedAt is null, exitedAt,
            _cpu.Count, Average(_cpu), Maximum(_cpu),
            Average(_working), Maximum(_working), Average(_private), Maximum(_private),
            _responsive, _unresponsive);
    }

    private void OnExited(object? sender, EventArgs args) =>
        _exited.TrySetResult(DateTimeOffset.UtcNow);

    private static double Average(List<double> values) =>
        values.Count == 0 ? 0 : values.Average();

    private static double Maximum(List<double> values) =>
        values.Count == 0 ? 0 : values.Max();

    private static long Average(List<long> values) =>
        values.Count == 0 ? 0 : checked((long)values.Average());

    private static long Maximum(List<long> values) =>
        values.Count == 0 ? 0 : values.Max();

    public void Dispose()
    {
        _process.Exited -= OnExited;
        _process.Dispose();
    }
}
