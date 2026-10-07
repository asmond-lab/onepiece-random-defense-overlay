namespace OrandOverlay;

// A complete bounded address walk, unlike legacy best-effort discovery. Never changes protection.
internal static class ReadOnlyPrivateRegionScan
{
    internal const ulong Minimum = 0x10000;
    internal const ulong EndExclusive = 0x00007FFFFFFF0000;

    internal static IEnumerable<MemoryRegion> Enumerate(
        Func<ulong, ReadOnlyProcessMemory.ModuleRegionInfo?> query, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var address = Minimum;
        var queries = 0;
        while (address < EndExclusive)
        {
            token.ThrowIfCancellationRequested();
            if (++queries > 1_000_000) throw new InvalidDataException("Private-region query budget exceeded.");
            if (query(address) is not { } info)
                throw new InvalidDataException("Private-region query incomplete.");
            if (info.Size == 0 || info.BaseAddress > address ||
                info.Size > ulong.MaxValue - info.BaseAddress || info.BaseAddress + info.Size <= address)
                throw new InvalidDataException("Invalid private-region bounds.");
            var next = Math.Min(EndExclusive, info.BaseAddress + info.Size);
            if (info.State == 0x1000 && info.Type == 0x20000 &&
                (info.Protect & 0x101) == 0 &&
                (info.Protect & 0xFF) is 2 or 4 or 8 or 0x20 or 0x40 or 0x80)
                yield return new MemoryRegion(address, next - address);
            address = next;
        }
        token.ThrowIfCancellationRequested();
    }
}
