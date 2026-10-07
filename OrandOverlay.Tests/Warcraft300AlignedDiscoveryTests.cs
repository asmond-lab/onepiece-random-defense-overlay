using System.Buffers.Binary;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Warcraft300AlignedDiscoveryTests
{
    private static readonly ulong[] Pointers = { 0, ulong.MaxValue, 0x000001F3BABA5000, 0x100000, 0x00007FFFABCD0088 };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void EveryShortLengthAndInjectedOffsetMatchesByteSearch(int alignment)
    {
        var random = new Random(92661 + alignment);
        var address = 0x100000UL + (ulong)alignment;
        for (var length = 0; length <= 40; length++)
        {
            foreach (var pointer in Pointers)
            {
                var bytes = new byte[length];
                AssertEquivalent(bytes, address, pointer);
                random.NextBytes(bytes);
                AssertEquivalent(bytes, address, pointer);
                for (var offset = 0; offset + 8 <= length; offset++)
                {
                    var injected = (byte[])bytes.Clone();
                    BinaryPrimitives.WriteUInt64LittleEndian(injected.AsSpan(offset), pointer);
                    AssertEquivalent(injected, address, pointer);
                }
            }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void RandomLargeBlocksFindAllAlignedAndIgnoreUnalignedHits(int alignment)
    {
        var random = new Random(53841 + alignment);
        var address = 0x200000UL + (ulong)alignment;
        for (var sample = 0; sample < 32; sample++)
        {
            var bytes = new byte[random.Next(128, 131073)];
            random.NextBytes(bytes);
            var pointer = Pointers[sample % Pointers.Length];
            for (var at = 16; at + 48 <= bytes.Length; at += 997)
            {
                var aligned = at + (int)((8 - ((address + (ulong)at) & 7)) & 7);
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(aligned), pointer);
                // This isolated hit has an absolute address misaligned by one byte.
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(aligned + 17), pointer);
            }
            AssertEquivalent(bytes, address, pointer);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void ValidPrefixIgnoresReusableBufferStaleTail(int alignment)
    {
        const ulong pointer = 0x000001F3BABA5000;
        var address = 0x300000UL + (ulong)alignment;
        var buffer = new byte[4096];
        var first = (int)((8 - (address & 7)) & 7);
        for (var at = first; at + 8 <= buffer.Length; at += 8)
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(at), pointer);
        foreach (var length in Enumerable.Range(0, 41).Concat(new[] { 127, 128, 129, 4089, 4095, 4096 }))
        {
            var actual = FindAligned(buffer.AsSpan(0, length), address, pointer);
            Assert.Equal(FindReference(buffer.AsSpan(0, length), address, pointer), actual);
            Assert.All(actual, offset => Assert.InRange(offset, 0, length - 8));
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void SevenByteOverlapRecomputesAbsoluteAlignmentAtEveryChunk(int alignment)
    {
        const int blockSize = 64 * 1024;
        const ulong pointer = 0x000001F3BABA5000;
        var address = 0x400000UL + (ulong)alignment;
        var bytes = new byte[blockSize * 3 + 31];
        new Random(36672 + alignment).NextBytes(bytes);
        // Probe each side of the actual overlapping block boundaries independently.
        foreach (var boundary in new[] { blockSize, 2 * blockSize - 7, 3 * blockSize - 14 })
        {
            for (var delta = -9; delta <= 9; delta++)
            {
                var probe = (byte[])bytes.Clone();
                var at = boundary + delta;
                BinaryPrimitives.WriteUInt64LittleEndian(probe.AsSpan(at), pointer);
                var chunks = new List<int>();
                for (var offset = 0; offset < probe.Length;)
                {
                    var n = Math.Min(blockSize, probe.Length - offset);
                    foreach (var hit in FindAligned(probe.AsSpan(offset, n), address + (ulong)offset, pointer))
                        chunks.Add(offset + hit);
                    offset += n == probe.Length - offset ? n : n - 7;
                }
                Assert.Equal(FindReference(probe, address, pointer), chunks);
            }
        }
    }

    [Fact]
    public void AdjacentAlignedZeroHitsRemainAscendingAndComplete()
    {
        foreach (var alignment in Enumerable.Range(0, 8))
        {
            var address = 0x500000UL + (ulong)alignment;
            var bytes = new byte[4096];
            AssertEquivalent(bytes, address, 0);
            var actual = FindAligned(bytes, address, 0);
            for (var index = 1; index < actual.Count; index++) Assert.Equal(8, actual[index] - actual[index - 1]);
        }
    }

    private static void AssertEquivalent(ReadOnlySpan<byte> bytes, ulong address, ulong pointer) =>
        Assert.Equal(FindReference(bytes, address, pointer), FindAligned(bytes, address, pointer));

    private static List<int> FindReference(ReadOnlySpan<byte> bytes, ulong address, ulong pointer)
    {
        var pattern = BitConverter.GetBytes(pointer);
        var hits = new List<int>();
        for (var offset = 0; offset + 8 <= bytes.Length;)
        {
            var hit = bytes[offset..].IndexOf(pattern);
            if (hit < 0) break;
            var at = offset + hit;
            offset = at + 1;
            if (((address + (ulong)at) & 7) == 0) hits.Add(at);
        }
        return hits;
    }

    private static List<int> FindAligned(ReadOnlySpan<byte> bytes, ulong address, ulong pointer)
    {
        var lanes = Warcraft300GrowthReader.AlignedDiscoveryLanes(bytes, address, out var first);
        var hits = new List<int>();
        for (var offset = 0; offset < lanes.Length;)
        {
            var hit = lanes[offset..].IndexOf(pointer);
            if (hit < 0) break;
            var lane = offset + hit;
            offset = lane + 1;
            hits.Add(first + lane * 8);
        }
        return hits;
    }
}
