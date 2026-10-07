using System.ComponentModel;

namespace OrandOverlay;

internal sealed class WarcraftStateReader(Func<ulong, int, byte[]> read, ulong moduleBase)
{
    private readonly RouteQuestMemory _memory = new(read);
    private readonly WarcraftHandleResolver _handles = new(read, moduleBase);

    internal UnitPosition? Position(ulong handle)
    {
        try
        {
            if (_handles.Entry(handle) is not { } entry) return null;
            var values = _memory.Bytes(checked(entry + 0xc8), 24);
            var x = BitConverter.ToSingle(values, 8);
            var y = BitConverter.ToSingle(values, 12);
            var velocityX = BitConverter.ToSingle(values, 16);
            var velocityY = BitConverter.ToSingle(values, 20);
            if (!float.IsFinite(x) || !float.IsFinite(y) ||
                !float.IsFinite(velocityX) || !float.IsFinite(velocityY) ||
                Elapsed(entry) is not { } elapsed) return null;
            var encodedWorld = U64(checked(moduleBase + 0x2a96c48));
            var world = encodedWorld ^ 0x2b29df2be4f74917UL;
            var origin = _memory.Bytes(checked(world + 0xe0), 8);
            var originY = BitConverter.ToSingle(origin);
            var originX = BitConverter.ToSingle(origin, 4);
            if (!float.IsFinite(originX) || !float.IsFinite(originY)) return null;
            x = WarcraftRealMath.Add(x, WarcraftRealMath.Multiply(velocityX, elapsed));
            y = WarcraftRealMath.Add(y, WarcraftRealMath.Multiply(velocityY, elapsed));
            static float Scale(float value)
            {
                var bits = BitConverter.SingleToUInt32Bits(value);
                return BitConverter.UInt32BitsToSingle(unchecked(bits +
                    ((bits & 0x7f800000U) != 0 ? 0x02800000U : 0U)));
            }
            x = WarcraftRealMath.Add(Scale(x), originX);
            y = WarcraftRealMath.Add(Scale(y), originY);
            return float.IsFinite(x) && float.IsFinite(y) &&
                values.AsSpan().SequenceEqual(_memory.Bytes(checked(entry + 0xc8), 24)) &&
                origin.AsSpan().SequenceEqual(_memory.Bytes(checked(world + 0xe0), 8)) &&
                U64(checked(moduleBase + 0x2a96c48)) == encodedWorld && _handles.Entry(handle) == entry
                ? new UnitPosition(x, y) : null;
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            return null;
        }
    }

    internal (float Current, float Maximum)? Regeneration(ulong handle)
    {
        try
        {
            if (_handles.Entry(handle) is not { } entry) return null;
            var values = _memory.Bytes(checked(entry + 0xc8), 28);
            var current = BitConverter.ToSingle(values, 8);
            var rate = BitConverter.ToSingle(values, 12);
            var minimum = BitConverter.ToSingle(values, 20);
            var maximum = BitConverter.ToSingle(values, 24);
            if (!float.IsFinite(current) || !float.IsFinite(rate) ||
                !float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum < 0 || maximum < minimum ||
                Elapsed(entry) is not { } elapsed) return null;
            current = WarcraftRealMath.Add(current, WarcraftRealMath.Multiply(rate, elapsed));
            if (!float.IsFinite(current) ||
                !values.AsSpan().SequenceEqual(_memory.Bytes(checked(entry + 0xc8), 28)) ||
                _handles.Entry(handle) != entry) return null;
            return (Math.Clamp(current, minimum, maximum), maximum);
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            return null;
        }
    }

    internal float? Elapsed(ulong entry)
    {
        try
        {
            var previous = _memory.Bytes(checked(entry + 0xc8), 8);
            var root = U64(checked(moduleBase + 0x2b80848));
            var clock = checked(root + (I32(checked(entry + 0x20)) < 0 ? 0xa0UL : 0x18UL));
            var current = _memory.Bytes(checked(clock + 0x70), 12);
            var before = BitConverter.ToSingle(previous);
            var now = BitConverter.ToSingle(current);
            var period = BitConverter.ToSingle(current, 8);
            var epsilon = BitConverter.ToSingle(_memory.Bytes(checked(moduleBase + 0x2a9704c), 4));
            if (!float.IsFinite(before) || !float.IsFinite(now) ||
                !float.IsFinite(period) || period <= 0 || !float.IsFinite(epsilon) || epsilon < 0) return null;
            var elapsed = WarcraftRealMath.Subtract(now, before);
            if (Math.Abs(elapsed) < epsilon) elapsed = 0;
            var epochs = unchecked(BitConverter.ToInt32(current, 4) - BitConverter.ToInt32(previous, 4));
            if (epochs != 0)
                elapsed = WarcraftRealMath.Add(elapsed,
                    WarcraftRealMath.Multiply(period, WarcraftRealMath.FromInt(epochs)));
            return float.IsFinite(elapsed) &&
                previous.AsSpan().SequenceEqual(_memory.Bytes(checked(entry + 0xc8), 8)) &&
                current.AsSpan().SequenceEqual(_memory.Bytes(checked(clock + 0x70), 12)) &&
                U64(checked(moduleBase + 0x2b80848)) == root ? elapsed : null;
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            return null;
        }
    }

    private ulong U64(ulong address) => BitConverter.ToUInt64(_memory.Bytes(address, 8));
    private int I32(ulong address) => BitConverter.ToInt32(_memory.Bytes(address, 4));
}
