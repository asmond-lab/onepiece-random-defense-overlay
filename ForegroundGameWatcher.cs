using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrandOverlay;

/// <summary>
/// 전면 창이 워크래프트3인지 추적한다. 시작 전에는 항상 "게임 전면"으로 보고해 테스트·캡처
/// 하네스에서 오버레이가 사라지지 않게 한다. 훅은 UI 스레드에서 한 번만 설치한다.
/// </summary>
public static class ForegroundGameWatcher
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private static WinEventDelegate? _callback;
    private static IntPtr _hook;

    public static bool IsGameForeground { get; private set; } = true;
    public static event Action? Changed;

    public static bool IsWarcraftProcess(string? processName) =>
        processName is not null &&
        (processName.Equals("Warcraft III", StringComparison.OrdinalIgnoreCase) ||
         processName.Equals("war3", StringComparison.OrdinalIgnoreCase));

    public static void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _callback = (_, _, window, _, _, _, _) => Update(window);
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        Update(GetForegroundWindow());
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _callback = null;
        Set(true);
    }

    private static void Update(IntPtr window) => Set(IsWarcraftProcess(ProcessNameOf(window)));

    private static void Set(bool gameForeground)
    {
        if (gameForeground == IsGameForeground) return;
        IsGameForeground = gameForeground;
        Changed?.Invoke();
    }

    private static string? ProcessNameOf(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var pid);
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId,
        int childId, uint threadId, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
