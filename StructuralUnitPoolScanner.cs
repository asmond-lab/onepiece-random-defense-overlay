using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OrandOverlay;

/// <summary>
/// 유닛 풀 루트를 런타임 구조로 찾는 읽기 전용 스캐너.
///
/// 고정 RVA나 바이트 시그니처 대신 두 가지 사실만 신뢰한다.
///   (1) 유닛 객체는 MSVC RTTI 클래스명(기본 .?AVCUnit@@)을 가진 vftable을 헤드에 둔다.
///   (2) 풀 루트는 countOffset에 개수, entriesPointerOffset에 객체 포인터 배열 주소를 둔다.
///
/// 힙(전용 커밋 영역)만 한 번 병렬로 훑어 유닛 객체와 구조체 후보를 함께 모은 뒤,
/// 살아남은 후보의 배열만 읽어 판정한다(후보마다 메모리를 훑으면 스캔이 수십 초로 늘어난다).
/// 전역 풀만 여러 소유자의 유닛을 함께 담으므로 그것으로 다른 목록과 구분한다.
/// 구분되지 않으면 fail-closed.
/// </summary>
internal static class StructuralUnitPoolScanner
{
    // Dedicated diagnostic sweep: only actual CWorldFrameWar3 primary heads qualify.
    // No guessed heap addresses, shifted count/array roots or root-zero fallback.
    internal static ulong ResolveDiagnostic(ReadOnlyProcessMemory memory, ProcessModule module,
        MemoryProfile profile, CancellationToken token)
    {
        var b = (ulong)module.BaseAddress.ToInt64();
        _ = Warcraft300Diagnostic.ReadView(memory.ReadAvailable, b);
        var candidates = new HashSet<ulong>();
        var watch = Stopwatch.StartNew();
        long scanned = 0;
        // The observed 3.0 process has more than 4 GiB of readable private allocations.
        // Diagnostic-only ceiling: 8 GiB / 30 seconds, with exact reads rather than silent skipped tails.
        var buffer = new byte[1024 * 1024];
        foreach (var region in memory.ReadablePrivateRegions())
        {
            for (ulong consumed = 0; consumed < region.Size;)
            {
                token.ThrowIfCancellationRequested();
                var length = (int)Math.Min((ulong)buffer.Length, region.Size - consumed);
                if (scanned + length > 8L * 1024 * 1024 * 1024 || watch.Elapsed > TimeSpan.FromSeconds(30))
                    throw new InvalidDataException("Diagnostic typed-root sweep budget exceeded; incomplete search rejected");
                var chunkBase = checked(region.BaseAddress + consumed);
                scanned += length;
                if (memory.ReadInto(chunkBase, buffer, length) != length)
                    throw new InvalidDataException("Incomplete diagnostic typed-root read rejected");
                for (var offset = 0; offset + 8 <= length; offset += 8)
                    if (BitConverter.ToUInt64(buffer, offset) == AddressMath.Add(b, Warcraft300Diagnostic.FrameVtable))
                    {
                        candidates.Add(AddressMath.Add(chunkBase, offset));
                        if (candidates.Count > 256) throw new InvalidDataException("Too many diagnostic frame heads");
                    }
                consumed += (ulong)length;
            }
        }
        var valid = new List<ulong>();
        foreach (var candidate in candidates)
        {
            try
            {
                _ = Warcraft300Diagnostic.ReadInventory(memory.ReadAvailable, b, candidate, profile, token);
                valid.Add(candidate);
            }
            catch (Exception e) when (e is InvalidDataException or Win32Exception or OverflowException) { }
        }
        LastScanMilliseconds = (int)watch.ElapsedMilliseconds;
        if (valid.Count != 1) throw new InvalidDataException($"Diagnostic typed-root selection requires one frame; found {valid.Count}");
        return valid[0];
    }

    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(5);
    private static readonly object Gate = new();
    private static DateTime _lastFailureUtc = DateTime.MinValue;
    private static string _lastFailure = "";
    private static bool _lastFailureWasNotReady;

    /// <summary>마지막 구조 스캔에 걸린 시간. 진단 문구에 노출한다.</summary>
    public static int LastScanMilliseconds { get; private set; }

    public static ulong Resolve(ReadOnlyProcessMemory memory, ProcessModule module, MemoryProfile profile,
        CancellationToken token)
    {
        // 전체 메모리 스캔은 비싸다. 직전 실패 직후에는 같은 사유로 즉시 되돌려 매 틱 재스캔을 막는다.
        lock (Gate)
            if (DateTime.UtcNow - _lastFailureUtc < FailureCooldown)
                throw _lastFailureWasNotReady
                    ? new PoolNotReadyException(_lastFailure)
                    : new InvalidOperationException(_lastFailure);
        try
        {
            var watch = Stopwatch.StartNew();
            var root = ResolveCore(memory, module, profile, token);
            LastScanMilliseconds = (int)watch.ElapsedMilliseconds;
            return root;
        }
        catch (InvalidOperationException exception)
        {
            lock (Gate)
            {
                _lastFailureUtc = DateTime.UtcNow;
                _lastFailure = exception.Message;
                _lastFailureWasNotReady = exception is PoolNotReadyException;
            }
            throw;
        }
    }

    private static ulong ResolveCore(ReadOnlyProcessMemory memory, ProcessModule module, MemoryProfile profile,
        CancellationToken token)
    {
        var moduleBase = (ulong)module.BaseAddress.ToInt64();
        var moduleSize = module.ModuleMemorySize;
        if (moduleSize <= 0 || moduleSize > 512 * 1024 * 1024)
            throw new InvalidDataException($"비정상 모듈 크기: {moduleSize}");

        // The process-identity-aware locator cache avoids rescanning on ordinary ticks.
        // Do not retain vftables across processes that reuse the same module base.
        var unitVftables = GetUnitVftables(memory, moduleBase, moduleSize, profile.UnitClassName, token);

        var (units, structs) = Sweep(memory, profile, unitVftables, token);
        if (units.Count < profile.MinimumUnitObjects)
            throw new PoolNotReadyException("대전 준비 중입니다(유닛이 아직 생성되지 않았습니다).");

        return SelectPoolRoot(memory, units, structs, profile, token);
    }

    /// <summary>프로필에 앵커가 있으면 로컬 플레이어 슬롯을 실측한다. 실패하면 null.</summary>
    public static byte? TryReadLocalPlayerSlot(ReadOnlyProcessMemory memory, ulong moduleBase, MemoryProfile profile)
    {
        if (!profile.HasLocalPlayerAnchor) return null;
        try
        {
            var mask = Convert.ToUInt64(profile.LocalPlayerRootXorHex, 16);
            var rootBefore = memory.ReadUInt64(AddressMath.Add(moduleBase, profile.LocalPlayerRootOffsetA)) ^ mask ^
                             memory.ReadUInt64(AddressMath.Add(moduleBase, profile.LocalPlayerRootOffsetB));
            if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(rootBefore)) return null;
            var slotBefore = BitConverter.ToUInt16(
                memory.Read(AddressMath.Add(rootBefore, profile.LocalPlayerIdOffset), 2));
            var rootAfter = memory.ReadUInt64(AddressMath.Add(moduleBase, profile.LocalPlayerRootOffsetA)) ^ mask ^
                            memory.ReadUInt64(AddressMath.Add(moduleBase, profile.LocalPlayerRootOffsetB));
            if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(rootAfter)) return null;
            var slotAfter = BitConverter.ToUInt16(
                memory.Read(AddressMath.Add(rootAfter, profile.LocalPlayerIdOffset), 2));
            return LocalPlayerSlotResolver.Resolve(rootBefore, slotBefore, rootAfter, slotAfter);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// 힙을 한 번만 훑어 유닛 객체(주소→소유자)와 풀 구조체 후보를 함께 모은다.
    /// 영역별로 병렬 처리하되 버퍼 안 비교만 하므로 후보당 추가 읽기가 없다.
    /// </summary>
    private static (Dictionary<ulong, byte> Units, List<PoolStruct> Structs) Sweep(ReadOnlyProcessMemory memory,
        MemoryProfile profile, HashSet<ulong> unitVftables, CancellationToken token)
    {
        var units = new Dictionary<ulong, byte>();
        var structs = new List<PoolStruct>();
        var gate = new object();

        Parallel.ForEach(memory.ReadableRegions(),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount), CancellationToken = token },
            () => (Units: new Dictionary<ulong, byte>(), Structs: new List<PoolStruct>()),
            (region, _, local) =>
            {
                foreach (var (chunkBase, buffer) in memory.ReadChunks([region]))
                    ScanBuffer(chunkBase, buffer, profile, unitVftables, local.Units, local.Structs);
                return local;
            },
            local =>
            {
                lock (gate)
                {
                    foreach (var pair in local.Units) units[pair.Key] = pair.Value;
                    structs.AddRange(local.Structs);
                }
            });

        return (units, structs);
    }

    /// <summary>
    /// 버퍼 한 덩어리에서 유닛 객체와 풀 구조체 후보를 찾는다.
    /// 오프셋이 모두 8의 배수라 qword 배열로 보고 훑는다 — 이 루프가 스캔 시간의 대부분이라
    /// 바이트 단위 변환이나 해시 조회를 넣지 않는다.
    /// </summary>
    private static void ScanBuffer(ulong chunkBase, byte[] buffer, MemoryProfile profile,
        HashSet<ulong> unitVftables, Dictionary<ulong, byte> units, List<PoolStruct> structs)
    {
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(
            buffer.AsSpan(0, buffer.Length & ~7));
        var singleVftable = unitVftables.Count == 1 ? unitVftables.First() : 0;
        var countWord = profile.CountOffset / 8;
        var entriesWord = profile.EntriesPointerOffset / 8;
        var alignedOffsets = profile.CountOffset % 8 == 0 && profile.EntriesPointerOffset % 8 == 0;
        var unitWordLimit = (buffer.Length - profile.OwnerOffset - 1) / 8;

        for (var index = 0; index < words.Length; index++)
        {
            var value = words[index];
            if (value != 0 && index <= unitWordLimit &&
                (singleVftable != 0 ? value == singleVftable : unitVftables.Contains(value)))
                units[chunkBase + (ulong)index * 8] = buffer[index * 8 + profile.OwnerOffset];

            if (!alignedOffsets || index + entriesWord >= words.Length) continue;
            var count = (int)(uint)words[index + countWord];
            if (count < Math.Max(1, profile.MinimumUnitObjects) ||
                count > profile.MaximumUnits) continue;
            var entries = words[index + entriesWord];
            if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(entries) || (entries & 7) != 0) continue;
            structs.Add(new PoolStruct(chunkBase + (ulong)index * 8, count, entries));
        }
    }

    private static HashSet<ulong> GetUnitVftables(ReadOnlyProcessMemory memory, ulong moduleBase, int moduleSize,
        string className, CancellationToken token)
    {
        var image = ReadImage(moduleBase, moduleSize,
            memory.ReadableModuleRegions(moduleBase, moduleSize, token), memory.ReadInto, token);
        if (image.Length < 0x1000) throw new InvalidDataException("모듈 이미지를 읽지 못했습니다.");
        var vftables = FindClassVftables(image, moduleBase, className, token).ToHashSet();
        if (vftables.Count == 0)
            throw new InvalidOperationException($"{className} vftable을 찾지 못했습니다.");
        return vftables;
    }

    /// <summary>
    /// 모아 둔 구조체 후보 중에서 실제 전역 풀을 고른다.
    /// 전역 풀은 살아 있는 유닛을 중복 없이 한 번씩 담으므로, 배열을 한 번 읽어
    /// 서로 다른 유닛 수와 소유자 종류로 판정한다. 구분되지 않으면 fail-closed.
    /// </summary>
    private static ulong SelectPoolRoot(ReadOnlyProcessMemory memory, Dictionary<ulong, byte> units,
        List<PoolStruct> structs, MemoryProfile profile, CancellationToken token)
    {
        // 전역 풀은 살아 있는 유닛을 넉넉히 담지만 "과반"을 요구하면 안 된다 — 메모리에는
        // 풀 밖의 CUnit(죽은 유닛·다른 구조체 소속)도 함께 잡히기 때문이다. 실제로 판이
        // 길어져 유닛이 565개로 늘었을 때 과반 조건이 인식을 통째로 막았다.
        // 순위 매기기(소유자 종류 → 유닛 수)가 진짜 풀을 골라 주므로 하한은 낮게 둔다.
        // 한 판의 실제 전역 풀 슬롯은 48까지 내려간다. 전체 힙에서 잡힌 죽은 CUnit
        // 수(실측 705)로 하한을 64까지 끌어올리면 정상 풀 44개가 영구 탈락한다.
        // 실행 파일별 검증 프로필이 정한 최소 CUnit 수만 사용한다.
        var minimumDistinctUnits = PoolDistinctUnitMinimum(units.Count, profile);
        var candidates = new List<(ulong Address, int Hits, int Owners, int Count, ulong Entries)>();
        var distinctUnits = new HashSet<ulong>();
        var owners = new HashSet<byte>();

        foreach (var candidate in structs.Where(x => x.Count >= minimumDistinctUnits)
                     .DistinctBy(x => (x.Entries, x.Count)))
        {
            token.ThrowIfCancellationRequested();
            var bytes = memory.ReadAvailable(candidate.Entries, candidate.Count * profile.EntryStride);
            if (bytes.Length < candidate.Count * profile.EntryStride) continue;

            distinctUnits.Clear();
            owners.Clear();
            var sound = true;
            for (var index = 0; index < candidate.Count && sound; index++)
            {
                var value = BitConverter.ToUInt64(bytes, index * profile.EntryStride + profile.EntryPointerOffset);
                if (value == 0) continue;
                if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(value)) { sound = false; break; }
                if (!units.TryGetValue(value, out var owner) || !distinctUnits.Add(value)) continue;
                owners.Add(owner);
            }
            // 배열에 깨진 포인터가 섞여 있으면 풀이 아니다(진짜 풀은 끝까지 성한 포인터만 담는다).
            if (!sound || distinctUnits.Count < minimumDistinctUnits) continue;
            candidates.Add((candidate.Address, distinctUnits.Count, owners.Count, candidate.Count, candidate.Entries));
        }

        if (candidates.Count == 0)
            throw new PoolNotReadyException(
                $"대전 준비 중입니다(유닛 풀이 아직 만들어지지 않았습니다). " +
                $"CUnit {units.Count} · 구조체 {structs.Count} · 판별 하한 {minimumDistinctUnits} · " +
                $"최대 슬롯 {(structs.Count == 0 ? 0 : structs.Max(item => item.Count))}");

        // 8바이트씩 훑다 보면 진짜 구조체 주변의 어긋난 위치도 그럴듯한 (개수, 배열) 쌍을 만든다.
        // 같은 유닛 집합을 담는 후보 중에서는 슬롯이 가장 적은 것이 실제 풀이다.
        var ranked = candidates
            .GroupBy(x => x.Entries)
            .Select(group => group.OrderByDescending(x => x.Hits).ThenBy(x => x.Count).First())
            .OrderByDescending(x => x.Owners).ThenByDescending(x => x.Hits).ThenBy(x => x.Count)
            .ToList();

        if (ranked.Count > 1 && ranked[1].Owners == ranked[0].Owners && ranked[1].Hits == ranked[0].Hits)
        {
            var top = string.Join(" / ", ranked.Take(3).Select(x => $"소유자{x.Owners}·유닛{x.Hits}·슬롯{x.Count}"));
            throw new InvalidOperationException($"구분되지 않는 유닛 배열이 {ranked.Count}개여서 프로필을 차단했습니다 (상위: {top}).");
        }
        return ranked[0].Address;
    }

    internal static int PoolDistinctUnitMinimum(int observedCUnits, MemoryProfile profile) =>
        Math.Max(1, profile.MinimumUnitObjects);

    private readonly record struct PoolStruct(ulong Address, int Count, ulong Entries);

    /// <summary>
    /// 모듈 이미지를 조각내어 한 버퍼로 읽는다(단일 읽기 상한보다 이미지가 클 수 있다).
    /// 못 읽은 페이지는 0으로 남고 RVA 대응은 그대로 유지된다.
    /// </summary>
    internal static byte[] ReadImage(ulong moduleBase, int moduleSize, IEnumerable<MemoryRegion> regions,
        Func<ulong, byte[], int, int> readInto, CancellationToken token = default)
    {
        ReadOnlyProcessMemory.ValidateModuleBounds(moduleBase, moduleSize);
        token.ThrowIfCancellationRequested();
        const int pageBytes = 0x1000;
        const int chunkBytes = 64 * 1024;
        var image = new byte[moduleSize];
        var buffer = new byte[Math.Min(chunkBytes, moduleSize)];
        var end = moduleBase + (ulong)moduleSize;
        var previousEnd = moduleBase;
        var read = 0;
        var attempts = 0;
        var budget = 3 * ((moduleSize + pageBytes - 1) / pageBytes) + 1024;
        foreach (var region in regions)
        {
            token.ThrowIfCancellationRequested();
            // Reject overlaps/out-of-bounds input rather than reading unrelated memory.
            if (region.Size == 0 || region.BaseAddress < previousEnd || region.BaseAddress >= end ||
                region.Size > end - region.BaseAddress)
                throw new InvalidDataException("Invalid readable module range.");
            var regionEnd = region.BaseAddress + region.Size;
            previousEnd = regionEnd;
            var address = region.BaseAddress;
            var pageRead = false;
            while (address < regionEnd)
            {
                token.ThrowIfCancellationRequested();
                if (++attempts > budget)
                    throw new InvalidDataException("Module image read budget exceeded.");
                var maximum = pageRead ? pageBytes - (int)(address % pageBytes) : chunkBytes;
                var length = (int)Math.Min((ulong)maximum, regionEnd - address);
                var actual = readInto(address, buffer, length);
                if (actual < 0 || actual > length)
                    throw new InvalidDataException("Invalid module image read count.");
                if (actual > 0)
                {
                    Buffer.BlockCopy(buffer, 0, image, (int)(address - moduleBase), actual);
                    read += actual;
                    address += (ulong)actual;
                    pageRead = actual < length;
                }
                else if (!pageRead)
                    pageRead = true; // Retry a failed large read page-by-page.
                else
                {
                    address += (ulong)length; // Only this failed page remains zero.
                    pageRead = false;
                }
            }
        }
        return read >= pageBytes ? image : [];
    }

    /// <summary>모듈 이미지에서 MSVC RTTI 클래스명에 해당하는 vftable 주소를 모두 찾는다.</summary>
    internal static List<ulong> FindClassVftables(byte[] image, ulong moduleBase, string className,
        CancellationToken token = default)
    {
        var name = Encoding.ASCII.GetBytes(className + "\0");
        var typeDescriptorRvas = new List<uint>();
        for (var index = 0; index + name.Length <= image.Length; index++)
        {
            if ((index & 0xFFFF) == 0) token.ThrowIfCancellationRequested();
            if (image[index] != name[0]) continue;
            if (!image.AsSpan(index, name.Length).SequenceEqual(name)) continue;
            if (index >= 0x10) typeDescriptorRvas.Add((uint)(index - 0x10)); // 이름은 타입 디스크립터 +0x10
        }

        // RTTICompleteObjectLocator: signature=1, +0x0C=타입 RVA, +0x14=자기 RVA
        var locators = new HashSet<ulong>();
        foreach (var typeRva in typeDescriptorRvas)
            for (var index = 0; index + 0x18 <= image.Length; index += 4)
            {
                if ((index & 0xFFFF) == 0) token.ThrowIfCancellationRequested();
                if (BitConverter.ToUInt32(image, index) == 1 &&
                    BitConverter.ToUInt32(image, index + 0x0C) == typeRva &&
                    BitConverter.ToUInt32(image, index + 0x14) == (uint)index)
                    locators.Add(moduleBase + (ulong)index);
            }

        // vftable 바로 앞(-8)에 COL 주소가 놓인다.
        var vftables = new List<ulong>();
        if (locators.Count == 0) return vftables;
        for (var index = 0; index + 8 <= image.Length; index += 8)
        {
            if ((index & 0xFFFF) == 0) token.ThrowIfCancellationRequested();
            if (locators.Contains(BitConverter.ToUInt64(image, index)))
                vftables.Add(moduleBase + (ulong)index + 8);
        }
        return vftables;
    }

}

/// <summary>
/// 대전 준비/로딩 중이라 유닛이나 풀이 아직 없는 상태.
/// 읽기 오류가 아니라 대기 상태로 표시해야 한다.
/// </summary>
internal sealed class PoolNotReadyException(string message) : InvalidOperationException(message);
