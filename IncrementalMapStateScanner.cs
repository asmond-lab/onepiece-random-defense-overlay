namespace OrandOverlay;

/// <summary>
/// Warcraft private 메모리를 한 번에 전부 읽지 않고 고정 예산만큼씩 순환한다.
/// 관측값은 한 세션 안에서 단조 증가하며 Reset에서 다음 판 상태로 비운다.
/// </summary>
internal sealed class IncrementalMapStateScanner
{
    private const int ChunkBytes = 4 * 1024 * 1024;
    private const int OverlapBytes = 0x2000;
    private const int MaximumHotWindows = 8;
    private readonly byte[] buffer = GC.AllocateUninitializedArray<byte>(ChunkBytes);
    private readonly int byteBudget;
    private List<MemoryRegion> regions = [];
    private readonly List<HotWindow> hotWindows = [];
    private readonly Dictionary<ulong, MapStateSample> hotSamples = [];
    private int regionIndex;
    private ulong regionOffset;
    private int hotWindowIndex;
    private int stepsSinceHotScan;
    private MapStateSample cycle = new(0, 0, "unknown");

    public IncrementalMapStateScanner(int byteBudget)
    {
        if (byteBudget < 0x1000)
            throw new ArgumentOutOfRangeException(nameof(byteBudget));
        this.byteBudget = byteBudget;
    }

    public int LastBytesRead { get; private set; }
    public int HotWindowCount => hotWindows.Count;
    public MapStateSample Current { get; private set; } = new(0, 0, "unknown");

    public MapStateSample ScanStep(ReadOnlyProcessMemory memory, CancellationToken token,
        int? overrideBudget = null, int hotRescanEverySteps = 4)
    {
        var budget = overrideBudget ?? byteBudget;
        if (budget < 0x1000)
            throw new ArgumentOutOfRangeException(nameof(overrideBudget));
        if (hotRescanEverySteps < 1)
            throw new ArgumentOutOfRangeException(nameof(hotRescanEverySteps));
        LastBytesRead = 0;
        if (regions.Count == 0) RefreshRegions(memory);

        stepsSinceHotScan++;
        if (hotWindows.Count > 0 && stepsSinceHotScan >= hotRescanEverySteps)
        {
            ScanHotWindow(memory, token, budget);
            stepsSinceHotScan = 0;
        }

        while (LastBytesRead < budget && regions.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var region = regions[regionIndex];
            var remainingRegion = region.Size - regionOffset;
            var remainingBudget = budget - LastBytesRead;
            var length = (int)Math.Min(
                Math.Min((ulong)buffer.Length, remainingRegion),
                (ulong)remainingBudget);
            var address = region.BaseAddress + regionOffset;
            var read = memory.ReadInto(address, buffer, length);
            LastBytesRead += length;
            if (read > 0) Observe(address, read, MapStateReader.ScanBuffer(buffer, read));

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

    internal void Observe(ulong address, int length, MapStateSample sample)
    {
        Merge(sample);
        if (!HasSignal(sample) || length <= 0) return;
        var existing = hotWindows.FindIndex(window => window.Address == address);
        if (existing >= 0)
        {
            hotWindows[existing] = new HotWindow(address, Math.Max(hotWindows[existing].Length, length));
            hotSamples[address] = sample;
            return;
        }
        if (hotWindows.Count >= MaximumHotWindows) return;
        hotWindows.Add(new HotWindow(address, length));
        hotSamples[address] = sample;
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
        hotWindows.Clear();
        hotSamples.Clear();
        regionIndex = 0;
        regionOffset = 0;
        hotWindowIndex = 0;
        stepsSinceHotScan = 0;
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

    private void ScanHotWindow(ReadOnlyProcessMemory memory, CancellationToken token, int budget)
    {
        token.ThrowIfCancellationRequested();
        if (hotWindowIndex >= hotWindows.Count) hotWindowIndex = 0;
        var window = hotWindows[hotWindowIndex++];
        var length = Math.Min(window.Length, Math.Min(buffer.Length, budget));
        var read = memory.ReadInto(window.Address, buffer, length);
        LastBytesRead += length;
        if (read <= 0) return;
        hotSamples[window.Address] = MapStateReader.ScanBuffer(buffer, read);
        MergeHotSamples();
    }

    private void MergeHotSamples()
    {
        var hot = new MapStateSample(0, 0, "unknown");
        foreach (var sample in hotSamples.Values)
            hot = new MapStateSample(
                Math.Max(hot.MaxRound, sample.MaxRound),
                Math.Max(hot.SettlementCopies, sample.SettlementCopies),
                MapStateReader.CombineDifficulty(hot.Difficulty, sample.Difficulty));
        Current = new MapStateSample(
            Math.Max(Current.MaxRound, hot.MaxRound),
            Math.Max(Current.SettlementCopies, hot.SettlementCopies),
            MapStateReader.CombineDifficulty(Current.Difficulty, hot.Difficulty));
    }

    private static bool HasSignal(MapStateSample sample) =>
        sample.MaxRound > 0 ||
        sample.SettlementCopies > 0 ||
        sample.Difficulty is { Length: > 0 } difficulty && difficulty != "unknown";

    private readonly record struct HotWindow(ulong Address, int Length);
}
