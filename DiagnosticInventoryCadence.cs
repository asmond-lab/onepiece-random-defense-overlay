using System.Diagnostics;

namespace OrandOverlay;

internal enum DiagnosticReadLane { Full, Basic }

// Dispatcher-owned admission, not a work queue. Full reads include a basic opportunity.
internal sealed class DiagnosticInventoryCadence
{
    internal static readonly TimeSpan BasicInterval = TimeSpan.FromMilliseconds(100);
    private readonly Func<TimeSpan> _elapsed;
    private TimeSpan _nextBasic;
    private TimeSpan _nextFull;
    private DiagnosticReadLane? _activeLane;

    internal DiagnosticInventoryCadence(Func<TimeSpan>? elapsed = null)
    {
        var started = Stopwatch.GetTimestamp();
        _elapsed = elapsed ?? (() => Stopwatch.GetElapsedTime(started));
    }

    internal TimeSpan NextDelay => TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond,
        Math.Min(_nextBasic.Ticks, _nextFull.Ticks) - _elapsed().Ticks));

    internal DiagnosticReadLane? TryBegin()
    {
        var now = _elapsed();
        if (_activeLane is not null || now < _nextBasic && now < _nextFull) return null;
        _activeLane = now >= _nextFull ? DiagnosticReadLane.Full : DiagnosticReadLane.Basic;
        _nextBasic = now + BasicInterval;
        if (_activeLane == DiagnosticReadLane.Full) _nextFull = now + MainWindow.RecognitionInterval;
        return _activeLane;
    }

    internal void Complete()
    {
        var now = _elapsed();
        // Drop missed basic opportunities and the full read's own elapsed deadlines.
        // A basic read must not postpone a due full read: retain one current full
        // opportunity, never a replay queue, so sustained slow basics cannot starve it.
        if (_nextBasic <= now) _nextBasic = now + BasicInterval;
        if (_activeLane == DiagnosticReadLane.Full && _nextFull <= now)
            _nextFull = now + MainWindow.RecognitionInterval;
        _activeLane = null;
    }

    internal void Reset()
    {
        _nextBasic = _nextFull = TimeSpan.Zero;
    }
}
