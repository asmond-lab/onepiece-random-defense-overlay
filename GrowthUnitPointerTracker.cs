namespace OrandOverlay;

using System.Text.Json;

/// <summary>
/// 성장형 특별함은 처음에는 로컬 소유지만 성장 위치로 이동하면 맵 소유 슬롯으로
/// 바뀐다. 같은 CUnit 포인터만 계속 내 패로 인정하고, 보지 못한 타 플레이어
/// 성장형이나 맵에 미리 배치된 특별함 원본은 절대 귀속하지 않는다.
/// </summary>
internal sealed class GrowthUnitPointerTracker
{
    private const int MissingSnapshotsBeforeRemoval = 3;
    private readonly Dictionary<ulong, TrackedGrowth> _tracked = [];
    public int Revision { get; private set; }

    public IReadOnlyDictionary<ulong, uint> Snapshot() =>
        _tracked.ToDictionary(pair => pair.Key, pair => pair.Value.Rawcode);

    public bool Matches(ulong pointer, uint rawcode) =>
        _tracked.TryGetValue(pointer, out var tracked) && tracked.Rawcode == rawcode;

    public void Commit(IReadOnlyDictionary<ulong, uint> locallyObserved,
        IReadOnlySet<ulong> seenTrackedPointers)
    {
        var changed = false;
        foreach (var pointer in _tracked.Keys.ToList())
        {
            var tracked = _tracked[pointer];
            if (seenTrackedPointers.Contains(pointer))
            {
                if (tracked.MissingSnapshots != 0)
                {
                    _tracked[pointer] = tracked with { MissingSnapshots = 0 };
                    changed = true;
                }
                continue;
            }
            var missing = tracked.MissingSnapshots + 1;
            if (missing >= MissingSnapshotsBeforeRemoval)
            {
                _tracked.Remove(pointer);
                changed = true;
            }
            else
            {
                _tracked[pointer] = tracked with { MissingSnapshots = missing };
                changed = true;
            }
        }
        foreach (var (pointer, rawcode) in locallyObserved)
            if (!_tracked.TryGetValue(pointer, out var tracked) ||
                tracked.Rawcode != rawcode || tracked.MissingSnapshots != 0)
            {
                _tracked[pointer] = new TrackedGrowth(rawcode, 0);
                changed = true;
            }
        if (changed) Revision++;
    }

    public void Restore(IReadOnlyDictionary<ulong, uint> pointers)
    {
        _tracked.Clear();
        foreach (var (pointer, rawcode) in pointers)
            _tracked[pointer] = new TrackedGrowth(rawcode, 0);
        Revision++;
    }

    public void Reset()
    {
        if (_tracked.Count == 0) return;
        _tracked.Clear();
        Revision++;
    }

    private sealed record TrackedGrowth(uint Rawcode, int MissingSnapshots);
}

internal static class GrowthUnitOwnershipPolicy
{
    /// <summary>
    /// RawcodeCounts에는 로컬 소유와 포인터로 로컬 귀속을 증명한 성장형만 들어 있다.
    /// 중립 성장형은 판 전체에서 한 기뿐이어도 다른 플레이어 것일 수 있으므로
    /// 수량 추정에 절대 합치지 않는다.
    /// </summary>
    public static IReadOnlyDictionary<uint, int> InventoryCounts(
        IReadOnlyDictionary<uint, int> attributedCounts,
        IReadOnlyDictionary<uint, int> neutralGrowthCounts)
    {
        _ = neutralGrowthCounts;
        return new Dictionary<uint, int>(attributedCounts);
    }
}

internal static class GrowthUnitPointerCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyDictionary<ulong, uint> Load(string path, long processStarted)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<ulong, uint>();
            var document = JsonSerializer.Deserialize<CacheDocument>(
                File.ReadAllText(path), JsonOptions);
            return document?.ProcessStarted == processStarted
                ? document.Pointers
                : new Dictionary<ulong, uint>();
        }
        catch
        {
            return new Dictionary<ulong, uint>();
        }
    }

    public static void Save(string path, long processStarted,
        IReadOnlyDictionary<ulong, uint> pointers)
    {
        try
        {
            if (pointers.Count == 0)
            {
                File.Delete(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            var document = new CacheDocument(processStarted,
                pointers.ToDictionary(pair => pair.Key, pair => pair.Value));
            File.WriteAllText(temp, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // 세션 캐시는 보조 상태다. 저장 실패 시 현재 프로세스의 메모리 추적은 계속한다.
        }
    }

    public static void Delete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private sealed record CacheDocument(long ProcessStarted, Dictionary<ulong, uint> Pointers);
}
