using System.Numerics;

namespace OrandOverlay;

internal static class WarcraftRealMath
{
    // Build 2.0.4.23745 uses integer real arithmetic rather than IEEE round-to-nearest.
    internal static float Add(float left, float right) =>
        BitConverter.UInt32BitsToSingle(AddBits(BitConverter.SingleToUInt32Bits(left),
            BitConverter.SingleToUInt32Bits(right)));

    internal static float Subtract(float left, float right) =>
        BitConverter.UInt32BitsToSingle(AddBits(BitConverter.SingleToUInt32Bits(left),
            BitConverter.SingleToUInt32Bits(right) ^ 0x80000000U));

    private static uint AddBits(uint left, uint right)
    {
        unchecked
        {
            var exponent = left & 0x7f800000U;
            var rightExponent = right & 0x7f800000U;
            if (exponent == 0) return right;
            if (rightExponent == 0) return left;
            var leftSign = (int)left >> 31;
            var rightSign = (int)right >> 31;
            var a = ((int)((left & 0x7fffffU | 0x800000U) * 2) ^ leftSign) - leftSign;
            var b = ((int)((right & 0x7fffffU | 0x800000U) * 2) ^ rightSign) - rightSign;
            var difference = (int)(rightExponent - exponent);
            if (difference <= 0)
            {
                if (difference < -0xb7fffff) return left;
                b >>= (int)((exponent - rightExponent) >> 23);
            }
            else
            {
                if (difference > 0xb7fffff) return right;
                a >>= difference >> 23;
                exponent = rightExponent;
            }
            var sum = a + b;
            if (sum == 0) return 0;
            var magnitude = (uint)((sum ^ (sum >> 31)) - (sum >> 31));
            var shift = BitOperations.Log2(magnitude) - 23;
            magnitude = shift >= 0 ? (uint)((int)magnitude >> shift) : magnitude << -shift;
            return exponent - 0x800000U + (uint)(shift * 0x800000) |
                magnitude & 0x7fffffU | (uint)sum & 0x80000000U;
        }
    }

    internal static float Multiply(float left, float right)
    {
        unchecked
        {
            var a = BitConverter.SingleToUInt32Bits(left);
            var b = BitConverter.SingleToUInt32Bits(right);
            var exponentA = a & 0x7f800000U;
            var exponentB = b & 0x7f800000U;
            var sign = (a ^ b) & 0x80000000U;
            a &= 0x7fffffU;
            b &= 0x7fffffU;
            uint result;
            if (a != 0 && b != 0)
            {
                var product = (ulong)((b | 0xff800000U) << 8) * ((a | 0xff800000U) << 8);
                var high = (uint)(product >> 32);
                result = ((uint)(0x181 - ((int)high >> 31)) * 0x800000U + exponentA + exponentB |
                    (high >> (int)(product >> 63)) >> 7 & 0x7fffffU | sign) &
                    (uint)~((int)(exponentA + exponentB - 0x40000000U) >> 31);
            }
            else if (exponentA != 0 && exponentB != 0)
            {
                var exponent = exponentA + exponentB + 0xc0800000U;
                result = (exponent | a | b | sign) & (uint)~((int)(exponent - 0x800000U) >> 31);
            }
            else result = 0;
            return BitConverter.UInt32BitsToSingle(result);
        }
    }

    internal static float FromInt(int value)
    {
        if (value == 0) return 0;
        unchecked
        {
            var magnitude = (uint)((value ^ (value >> 31)) - (value >> 31));
            var highest = BitOperations.Log2(magnitude);
            var shift = 23 - highest;
            magnitude = shift >= 0 ? magnitude << shift : (uint)((int)magnitude >> -shift);
            return BitConverter.UInt32BitsToSingle((uint)((highest + 127) * 0x800000) |
                magnitude & 0x7fffffU | (uint)value & 0x80000000U);
        }
    }
}
