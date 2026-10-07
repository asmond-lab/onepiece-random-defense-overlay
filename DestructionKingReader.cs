using System.ComponentModel;

namespace OrandOverlay;

internal sealed class DestructionKingReader
{
    private ulong? _node;
    private ulong? _anchor;
    internal void Reset() { _node = null; _anchor = null; }
    internal bool? Read(Func<ulong, int, byte[]> read, string version, string mapHash,
        ulong? anchor, CancellationToken token)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 || anchor is null)
        {
            Reset();
            return null;
        }
        if (_anchor != anchor) Reset();
        _anchor = anchor;
        try
        {
            var memory = new RouteQuestMemory(read);
            _node ??= QuestGlobalDiscovery.FindRelated(memory, anchor.Value, ["cu"], token)["cu"];
            var first = BitConverter.ToInt32(memory.Node(_node.Value, "cu", 8), 56);
            var second = BitConverter.ToInt32(memory.Node(_node.Value, "cu", 8), 56);
            return first is 0 or 1 && first == second ? first == 1 : null;
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            Reset();
            return null;
        }
    }
}
