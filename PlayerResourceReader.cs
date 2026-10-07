using System.Buffers.Binary;

namespace OrandOverlay;

internal sealed class PlayerResourceReader
{
    internal const int ScanBudget = 4 * 1024 * 1024;
    private MemoryRegion[]? _regions;
    private int _regionIndex;
    private ulong _offset;
    private ulong? _address;
    private byte? _owner;

    internal void Reset()
    {
        _regions = null;
        _regionIndex = 0;
        _offset = 0;
        _address = null;
        _owner = null;
    }

    internal PlayerResourceState? Read(Func<ulong, int, byte[]> read,
        IEnumerable<MemoryRegion> regions, string version, string mapHash, byte? owner,
        CancellationToken token)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 || owner is null or > 3)
        {
            Reset();
            return null;
        }
        if (_owner != owner)
        {
            Reset();
            _owner = owner;
        }
        if (_address is { } cached) return Stable(cached);
        if (_regions is null)
        {
            var ordered = new List<MemoryRegion>();
            foreach (var group in regions.GroupBy(region => region.Size > 256 * 1024).OrderBy(group => group.Key))
            {
                var addresses = group.OrderBy(region => region.BaseAddress).ToArray();
                for (int low = 0, high = addresses.Length - 1; low <= high;)
                {
                    ordered.Add(addresses[low++]);
                    if (low <= high) ordered.Add(addresses[high--]);
                }
            }
            _regions = ordered.ToArray();
        }
        var budget = ScanBudget - 2 * PlayerResourceRecords.SnapshotSize;
        while (_regionIndex < _regions.Length && budget >= PlayerResourceRecords.SnapshotSize)
        {
            token.ThrowIfCancellationRequested();
            var region = _regions[_regionIndex];
            var length = (int)Math.Min((ulong)Math.Min(budget, 1024 * 1024), region.Size - _offset);
            if (length < PlayerResourceRecords.SnapshotSize)
            {
                _regionIndex++;
                _offset = 0;
                continue;
            }
            var start = checked(region.BaseAddress + _offset);
            var bytes = read(start, length);
            budget -= length;
            for (var index = 40; index <= bytes.Length - PlayerResourceRecords.SnapshotSize + 40; index += 4)
            {
                if (BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(index)) != 0x60666c675e70726f ||
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index + 8)) != owner.Value * 40 + 3)
                    continue;
                if (PlayerResourceRecords.Decode(bytes.AsSpan(index - 40), owner.Value) is null) continue;
                _address = checked(start + (ulong)(index - 40));
                return Stable(_address.Value);
            }
            if ((ulong)length >= region.Size - _offset)
            {
                _regionIndex++;
                _offset = 0;
            }
            else _offset += (ulong)(length - PlayerResourceRecords.SnapshotSize + 4);
        }
        if (_regionIndex >= _regions.Length)
        {
            _regions = null;
            _regionIndex = 0;
            _offset = 0;
        }
        return null;

        PlayerResourceState? Stable(ulong address)
        {
            var first = PlayerResourceRecords.Decode(read(address, PlayerResourceRecords.SnapshotSize), owner.Value);
            var second = PlayerResourceRecords.Decode(read(address, PlayerResourceRecords.SnapshotSize), owner.Value);
            if (first is not null && second is not null)
                return first == second ? second : null;
            Reset();
            return null;
        }
    }
}
