namespace OrandOverlay;

/// <summary>
/// Warcraft private 메모리를 한 번에 전부 읽지 않고 고정 예산만큼씩 순환한다.
/// 관측값은 한 세션 안에서 단조 증가하며 Reset에서 다음 판 상태로 비운다.
/// </summary>
internal sealed class IncrementalMapStateScanner
{
    private const int ChunkBytes = 4 * 1024 * 1024;
    private const int OverlapBytes = 0x2000;
    private readonly byte[] buffer = GC.AllocateUninitializedArray<byte>(ChunkBytes);
    private readonly int byteBudget;
    private List<MemoryRegion> regions = [];
    private int regionIndex;
    private ulong regionOffset;
    private MapStateSample cycle = new(0, 0, "unknown");

    public IncrementalMapStateScanner(int byteBudget)
    {
        if (byteBudget < 0x1000)
            throw new ArgumentOutOfRangeException(nameof(byteBudget));
        this.byteBudget = byteBudget;
    }

    public int LastBytesRead { get; private set; }
    public MapStateSample Current { get; private set; } = new(0, 0, "unknown");

    public MapStateSample ScanStep(ReadOnlyProcessMemory memory, CancellationToken token,
        int? overrideBudget = null)
    {
        var budget = overrideBudget ?? byteBudget;
        if (budget < 0x1000)
            throw new ArgumentOutOfRangeException(nameof(overrideBudget));
        LastBytesRead = 0;
        if (regions.Count == 0) RefreshRegions(memory);

        while (LastBytesRead < budget && regions.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var region = regions[regionIndex];
            var remainingRegion = region.Size - regionOffset;
            var remainingBudget = budget - LastBytesRead;
            var length = (int)Math.Min(
                Math.Min((ulong)buffer.Length, remainingRegion),
                (ulong)remainingBudget);
            var read = memory.ReadInto(region.BaseAddress + regionOffset, buffer, length);
            LastBytesRead += length;
            if (read > 0) Merge(MapStateReader.ScanBuffer(buffer, read));

            if ((ulong)length >= remainingRegion)
            {
                regionIndex++;
                regionOffset = 0;
                if (regionIndex >= regions.Count)
                {
                    RefreshRegions(memory);
                    cycle = new MapStateSample(0, 0, "unknown");
                    break;
                }
                continue;
            }

            regionOffset += (ulong)Math.Max(length - OverlapBytes, 0x1000);
        }
        return Current;
    }

    internal void Merge(MapStateSample sample)
    {
        cycle = new MapStateSample(
            Math.Max(cycle.MaxRound, sample.MaxRound),
            cycle.SettlementCopies + sample.SettlementCopies,
            MapStateReader.CombineDifficulty(cycle.Difficulty, sample.Difficulty));
        Current = new MapStateSample(
            Math.Max(Current.MaxRound, cycle.MaxRound),
            Math.Max(Current.SettlementCopies, cycle.SettlementCopies),
            MapStateReader.CombineDifficulty(Current.Difficulty, cycle.Difficulty));
    }

    public void Reset()
    {
        regions = [];
        regionIndex = 0;
        regionOffset = 0;
        cycle = new MapStateSample(0, 0, "unknown");
        Current = new MapStateSample(0, 0, "unknown");
        LastBytesRead = 0;
    }

    private void RefreshRegions(ReadOnlyProcessMemory memory)
    {
        regions = memory.ReadableRegions().ToList();
        regionIndex = 0;
        regionOffset = 0;
    }
}
