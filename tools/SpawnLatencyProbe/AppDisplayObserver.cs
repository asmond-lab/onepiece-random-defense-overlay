using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Automation;

internal sealed class AppDisplayObserver : IDisposable
{
    private readonly AutomationElement _status;
    private readonly AutomationPropertyChangedEventHandler _handler;
    private readonly ConcurrentQueue<object> _events = new();
    internal object[] Events => _events.ToArray();

    internal AppDisplayObserver(int processId)
    {
        using var process = Process.GetProcessById(processId);
        if (process.ProcessName != "RandyPick" || process.MainWindowHandle == IntPtr.Zero)
            throw new InvalidOperationException("Expected a running RandyPick window.");
        var root = AutomationElement.FromHandle(process.MainWindowHandle);
        _status = root.FindFirst(TreeScope.Descendants, new PropertyCondition(
            AutomationElement.AutomationIdProperty, "MainObservationStatus"))
            ?? throw new InvalidOperationException("MainObservationStatus is unavailable.");
        _handler = (_, change) => _events.Enqueue(new
        {
            kind = "name-change", utc = DateTimeOffset.UtcNow,
            tick = Stopwatch.GetTimestamp(), text = change.NewValue as string
        });
        Automation.AddAutomationPropertyChangedEventHandler(_status, TreeScope.Element,
            _handler, AutomationElement.NameProperty);
        _events.Enqueue(new
        {
            kind = "initial", utc = DateTimeOffset.UtcNow,
            tick = Stopwatch.GetTimestamp(), text = _status.Current.Name
        });
    }

    public void Dispose() =>
        Automation.RemoveAutomationPropertyChangedEventHandler(_status, _handler);
}
