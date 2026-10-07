using System.ComponentModel;

namespace OrandOverlay;

internal sealed class LoadedClearCountReader
{
    private ulong? _node;
    private ulong? _anchor;

    internal void Reset() { _node = null; _anchor = null; }

    internal int? Read(Func<ulong, int, byte[]> read, string version, string mapHash,
        byte? owner, ulong? anchor, CancellationToken token)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 ||
            owner is null or > 3 || anchor is null)
        {
            Reset();
            return null;
        }
        if (_anchor != anchor) Reset();
        _anchor = anchor;
        try
        {
            var memory = new RouteQuestMemory(read);
            _node ??= QuestGlobalDiscovery.FindRelated(memory, anchor.Value, ["WE"], token)["WE"];
            var first = memory.Array(_node.Value, "WE", 9, 4);
            var second = memory.Array(_node.Value, "WE", 9, 4);
            if (first is null || second is null || first.Length <= owner.Value ||
                second.Length <= owner.Value || first[owner.Value] < 0 ||
                first[owner.Value] != second[owner.Value])
                return null;
            return second[owner.Value];
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            Reset();
            return null;
        }
    }
}
