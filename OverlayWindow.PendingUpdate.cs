namespace OrandOverlay;

public partial class OverlayWindow
{
    public event Action? PendingUpdateRequested;
    internal void InitializePendingUpdateNotice() => OverlayPendingUpdate.Requested += () => PendingUpdateRequested?.Invoke();
    internal void PresentPendingUpdate(string? tag, bool ready, bool busy) => OverlayPendingUpdate.Present(tag, ready, busy);
}
