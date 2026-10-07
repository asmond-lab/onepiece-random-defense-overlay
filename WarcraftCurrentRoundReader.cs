using System.Globalization;
using System.Text;

namespace OrandOverlay;

internal sealed class WarcraftCurrentRoundReader(Func<string, int?>? titleParser = null)
{
    // 2.0.4.23745: live native handle table -> CTimerDialogWar3 -> CTimerDialog
    // -> owned TimerDialogTitle CTextFrame. Never discover a round by heap text.
    private const ulong HandleRootRva = 0x2b808c0, GameUiRva = 0x2b84770;
    private const ulong DialogVtable = 0x23dbcf0, UiVtable = 0x24046a8,
        TextVtable = 0x21ea5f0, GameUiVtable = 0x23fd7d0, TimerVtable = 0x23d7aa0;
    private Candidate? _cached;
    private Snapshot? _last;
    internal bool SessionChanged { get; private set; }
    internal void Reset() { _cached = null; _last = null; SessionChanged = false; }

    internal int? Read(Func<ulong, int, byte[]> read, ulong moduleBase, int moduleSize,
        string version, string mapHash, byte? owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 ||
            owner is null or > 3 || moduleSize <= (long)GameUiRva + 8)
            return null;
        try
        {
            var memory = new RouteQuestMemory(read);
            var handles = new WarcraftHandleResolver(read, moduleBase);
            ulong U64(ulong address) => BitConverter.ToUInt64(memory.Bytes(address, 8));
            var root = U64(moduleBase + HandleRootRva);
            Snapshot? first = _cached is { } cached && cached.Root == root ? Observe(cached) : null;
            if (first is null)
            {
                _cached = null;
                var slots = U64(root + 0x18);
                var count = BitConverter.ToUInt32(memory.Bytes(root + 0x30, 4));
                if (count is 0 or > 262144) return null;
                var entries = memory.Bytes(slots, checked((int)count * 16));
                for (var i = 0; i < count; i++)
                {
                    if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                    if (BitConverter.ToUInt32(entries, i * 16) != 0xfffffffe) continue;
                    var entry = BitConverter.ToUInt64(entries, i * 16 + 8);
                    var bytes = read(entry, 0x98);
                    if (bytes.Length != 0x98 || BitConverter.ToUInt64(bytes, 0x30) != 0) continue;
                    var agent = BitConverter.ToUInt64(bytes, 0x90);
                    if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(agent)) continue;
                    var identity = read(agent, 8);
                    if (identity.Length != 8 || BitConverter.ToUInt64(identity) != moduleBase + DialogVtable) continue;
                    var handle = ((ulong)BitConverter.ToUInt32(bytes, 0x24) << 32) | (uint)i;
                    var found = Observe(new(root, handle, agent));
                    if (found is null) continue;
                    // A second current-round dialog is ambiguous, not a reason to take max/min.
                    if (first is not null) return null;
                    first = found;
                }
                if (U64(root + 0x18) != slots) return null;
            }
            if (first is null || Observe(first.Source) != first) return null;
            _cached = first.Source;
            // GP is created once per pinned map. A replacement/root change or a
            // rollback of its actual owned title is a new match, not heap history.
            if (_last is { } previous && (previous.Source != first.Source || first.Round < previous.Round))
                SessionChanged = true;
            _last = first;
            return first.Round;

            Snapshot? Observe(Candidate source)
            {
                if (U64(moduleBase + HandleRootRva) != source.Root || handles.Object(source.Handle) != source.Agent ||
                    U64(source.Agent) != moduleBase + DialogVtable || U64(source.Agent + 0x18) != source.Handle)
                    return null;
                var ui = U64(source.Agent + 0x58);
                var timerHandle = U64(source.Agent + 0x60);
                if (U64(ui) != moduleBase + UiVtable || handles.Object(timerHandle) is not { } timer ||
                    U64(timer) != moduleBase + TimerVtable || U64(timer + 0x18) != timerHandle || U64(ui + 0x2d8) != timer)
                    return null;
                var gameUi = U64(moduleBase + GameUiRva);
                if (U64(ui + 0x40) != gameUi || U64(gameUi) != moduleBase + GameUiVtable) return null;
                var frame = U64(ui + 0x298);
                if (U64(frame) != moduleBase + TextVtable || U64(frame + 0x40) != ui) return null;
                var titleAddress = U64(frame + 0x350);
                if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(titleAddress)) return null;
                var bytes = read(titleAddress, 96);
                var end = Array.IndexOf(bytes, (byte)0);
                if (end < 0) return null;
                var title = Encoding.UTF8.GetString(bytes, 0, end);
                if ((titleParser ?? ParseTitle)(title) is not { } round) return null;
                return new(source, ui, frame, gameUi, titleAddress, timerHandle, timer, title, round);
            }
        }
        catch (Exception error) when (error is InvalidDataException or
            System.ComponentModel.Win32Exception or OverflowException)
        {
            _cached = null;
            return null; // Invalid ownership/read is unknown, never a heap-round fallback.
        }
    }

    internal static int? ParseTitle(string title)
    {
        if (!title.EndsWith("|r", StringComparison.Ordinal)) return null;
        foreach (var prefix in new[] { "|cffFF0000현재 라운드|r : ", "|cffFF0000현재 라운드 : |r", "|cffFF0000보스 라운드|r : " })
            if (title.Length > prefix.Length + 2 && title.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(title.AsSpan(prefix.Length, title.Length - prefix.Length - 2),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var round) && round is >= 1 and <= 65)
                return round;
        return null;
    }

    private sealed record Candidate(ulong Root, ulong Handle, ulong Agent);
    private sealed record Snapshot(Candidate Source, ulong Ui, ulong Frame, ulong GameUi,
        ulong TitleAddress, ulong TimerHandle, ulong Timer, string Title, int Round);
}
