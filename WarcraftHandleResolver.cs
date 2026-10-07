namespace OrandOverlay;

internal sealed class WarcraftHandleResolver(Func<ulong, int, byte[]> read, ulong moduleBase)
{
    private readonly RouteQuestMemory _memory = new(read);

    internal ulong? Entry(ulong handle)
    {
        var rawIndex = (uint)handle;
        var generation = (uint)(handle >> 32);
        if ((rawIndex & generation) == uint.MaxValue) return null;
        try
        {
            var root = U64(checked(moduleBase + 0x2B808C0));
            if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(root)) return null;
            var alternate = (rawIndex & 0x80000000) != 0;
            var index = rawIndex & 0x7fffffff;
            var count = U32(checked(root + (alternate ? 0x68UL : 0x30UL)));
            if (index >= count) return null;
            var entries = U64(checked(root + (alternate ? 0x50UL : 0x18UL)));
            var slot = checked(entries + 16UL * index);
            if (U32(slot) != 0xfffffffe) return null;
            var entry = U64(checked(slot + 8));
            return ReadOnlyProcessMemory.IsPlausibleUserAddress(entry) &&
                U32(checked(entry + 0x24)) == generation ? entry : null;
        }
        catch (Exception error) when (error is InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    internal ulong? Object(ulong handle)
    {
        if (Entry(handle) is not { } entry) return null;
        try
        {
            if (U64(checked(entry + 0x30)) != 0) return null;
            var pointer = U64(checked(entry + 0x90));
            return ReadOnlyProcessMemory.IsPlausibleUserAddress(pointer) ? pointer : null;
        }
        catch (Exception error) when (error is InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    private uint U32(ulong address) => BitConverter.ToUInt32(_memory.Bytes(address, 4));
    private ulong U64(ulong address) => BitConverter.ToUInt64(_memory.Bytes(address, 8));
}
