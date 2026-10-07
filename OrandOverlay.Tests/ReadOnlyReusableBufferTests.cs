using System.Runtime.InteropServices;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ReadOnlyReusableBufferTests
{
    [Fact]
    public void ReusedNativeBufferReadsOnlyRequestedBytesAndAccountsActualCount()
    {
        var source = Marshal.AllocHGlobal(16);
        try
        {
            var expected = Enumerable.Range(1, 16).Select(x => (byte)x).ToArray();
            Marshal.Copy(expected, 0, source, expected.Length);
            using var memory = ReadOnlyProcessMemory.Open(Environment.ProcessId);
            var buffer = new byte[32]; Array.Fill(buffer, (byte)0xEE);
            var address = (ulong)source.ToInt64();
            Assert.Equal(16, memory.ReadAvailable(address, buffer, 16));
            Assert.Equal(expected, buffer.Take(16).ToArray());
            Assert.All(buffer.Skip(16), b => Assert.Equal((byte)0xEE, b));
            Assert.Equal(4, memory.ReadAvailable(address + 8, buffer, 4));
            Assert.Equal(expected.Skip(8).Take(4).ToArray(), buffer.Take(4).ToArray());
            Assert.Equal(expected.Skip(4).ToArray(), buffer.Skip(4).Take(12).ToArray());
            Assert.Equal(20, memory.SnapshotReads().UnitTraversal.Bytes);
            Assert.Equal(2, memory.SnapshotReads().UnitTraversal.Calls);
            Assert.Equal(expected, memory.ReadAvailable(address, 16));
        }
        finally { Marshal.FreeHGlobal(source); }
    }

    [Fact]
    public void InvalidBufferSpansNeverIssueNativeReadOrOverwriteExistingBytes()
    {
        using var memory = ReadOnlyProcessMemory.Open(Environment.ProcessId);
        var buffer = Enumerable.Repeat((byte)0xAA, 8).ToArray();
        Assert.Throws<ArgumentNullException>(() => memory.ReadAvailable(0x10000, null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.ReadAvailable(0x10000, buffer, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.ReadAvailable(0x10000, buffer, 9));
        Assert.Equal(0, memory.ReadAvailable(0x10000, buffer, 0));
        Assert.Equal(0, memory.ReadAvailable(0, buffer, 8));
        Assert.All(buffer, b => Assert.Equal((byte)0xAA, b));
        Assert.Equal(0, memory.SnapshotReads().UnitTraversal.Calls);
    }
}
