using System.Buffers.Binary;

namespace OrandOverlay;

public sealed record PlayerResourceState(long Gold, long Lumber, long TraitPoints);

internal static class PlayerResourceRecords
{
    internal const int Stride = 0xE0;
    internal const int SnapshotSize = Stride * 4 + 56;

    internal static PlayerResourceState? Decode(ReadOnlySpan<byte> snapshot, byte owner)
    {
        if (owner > 23 || snapshot.Length < SnapshotSize) return null;
        foreach (var kind in new[] { 3, 4, 6, 7 })
        {
            var record = snapshot[((kind - 3) * Stride)..];
            var key = (uint)(owner * 40 + kind);
            if (BinaryPrimitives.ReadUInt64LittleEndian(record[40..]) != 0x60666c675e70726f ||
                BinaryPrimitives.ReadUInt32LittleEndian(record[48..]) != key ||
                BinaryPrimitives.ReadUInt32LittleEndian(record[52..]) != key ||
                BinaryPrimitives.ReadInt64LittleEndian(record) < 0)
                return null;
        }
        return new(BinaryPrimitives.ReadInt64LittleEndian(snapshot) / 10,
            BinaryPrimitives.ReadInt64LittleEndian(snapshot[Stride..]) / 10,
            BinaryPrimitives.ReadInt64LittleEndian(snapshot[(4 * Stride)..]));
    }
}
