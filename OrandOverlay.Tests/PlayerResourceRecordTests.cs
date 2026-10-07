using System.Buffers.Binary;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class PlayerResourceRecordTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(119)]
    public void InitialDiscoveryDoesNotStarveEitherAddressEnd(int targetIndex)
    {
        var regions = Enumerable.Range(0, 120)
            .Select(index => new MemoryRegion(0x10000UL + (ulong)index * 0x20000, 0x20000)).ToArray();
        var target = regions[targetIndex].BaseAddress;
        var snapshot = Snapshot(0, 830, 0, 0);
        var bytesRead = 0;
        byte[] Read(ulong address, int count)
        {
            bytesRead += count;
            var bytes = new byte[count];
            if (address == target) snapshot.CopyTo(bytes, 0);
            return bytes;
        }
        var result = new PlayerResourceReader().Read(Read, regions, "2.0.4.23745",
            RouteQuestCatalog.MapScriptSha256, 0, default);
        Assert.Equal(new PlayerResourceState(83, 0, 0), result);
        Assert.True(bytesRead <= PlayerResourceReader.ScanBudget);
    }

    [Fact]
    public void ChangingBalanceIsUnknownWithoutDiscardingValidRecordLocation()
    {
        var bytes = Snapshot(0, 830, 0, 0);
        var changing = false;
        var calls = 0;
        byte[] Read(ulong address, int count)
        {
            calls++;
            var copy = bytes.AsSpan((int)(address - 0x10000), count).ToArray();
            if (changing) BinaryPrimitives.WriteInt64LittleEndian(copy, 830 + calls * 10);
            return copy;
        }
        var regions = new[] { new MemoryRegion(0x10000, (ulong)bytes.Length) };
        var reader = new PlayerResourceReader();
        Assert.NotNull(reader.Read(Read, regions, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, default));
        changing = true;
        Assert.Null(reader.Read(Read, regions, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, default));
        changing = false;
        calls = 0;
        Assert.Equal(new PlayerResourceState(83, 0, 0),
            reader.Read(Read, regions, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ReaderDiscoversByStructureAndRereadsChangedBalance()
    {
        var bytes = Snapshot(0, 830, 0, 0);
        byte[] Read(ulong address, int count) => bytes.AsSpan(checked((int)(address - 0x10000)),
            count).ToArray();
        var reader = new PlayerResourceReader();
        var regions = new[] { new MemoryRegion(0x10000, (ulong)bytes.Length) };
        var first = reader.Read(Read, regions, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, default);
        Assert.Equal(new PlayerResourceState(83, 0, 0), first);
        BinaryPrimitives.WriteInt64LittleEndian(bytes, 910);
        Assert.Equal(new PlayerResourceState(91, 0, 0),
            reader.Read(Read, regions, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, default));
        Assert.Null(reader.Read(Read, regions, "wrong", RouteQuestCatalog.MapScriptSha256, 0, default));
        reader.Reset();
        Assert.Null(reader.Read(Read, regions, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 1, default));
    }

    [Fact]
    public void NativeResourcesReachCoachOnlyForCurrentRecognition()
    {
        var resources = new PlayerResourceState(83, 4, 2);
        var fields = CoachSignalAdapter.Read(null, 1, 1, true);
        Assert.Null(fields["gold"]);
        // Production resource projection is tested through its public adapter seam.
        var projected = CoachSignalAdapter.WithPlayerResources(fields, resources, true);
        Assert.Equal(83, projected["gold"]);
        Assert.Equal(4, projected["lumber"]);
        Assert.Equal(2, projected["trait-points"]);
        Assert.Null(CoachSignalAdapter.WithPlayerResources(fields, resources, false)["gold"]);
        Assert.Null(CoachSignalAdapter.WithPlayerResources(fields, null, true)["gold"]);
    }

    [Fact]
    public void LocalRecordDecodesObserved83GoldAndZeroOtherResources()
    {
        var bytes = Snapshot(0, 830, 0, 0);
        Assert.Equal(new PlayerResourceState(83, 0, 0), PlayerResourceRecords.Decode(bytes, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void PlayerKeysAndResourceScalingAreNotInterchangeable(byte owner)
    {
        var bytes = Snapshot(owner, 12450, 370, 2);
        Assert.Equal(new PlayerResourceState(1245, 37, 2), PlayerResourceRecords.Decode(bytes, owner));
        Assert.Null(PlayerResourceRecords.Decode(bytes, (byte)(owner + 1)));
    }

    [Fact]
    public void MissingOrCorruptRecordIsNotAZeroBalance()
    {
        Assert.Null(PlayerResourceRecords.Decode([], 0));
        var bytes = Snapshot(0, 830, 0, 0);
        bytes[PlayerResourceRecords.Stride + 52] ^= 1;
        Assert.Null(PlayerResourceRecords.Decode(bytes, 0));
    }

    internal static byte[] Snapshot(byte owner, long gold, long lumber, long traits)
    {
        var data = new byte[PlayerResourceRecords.SnapshotSize];
        foreach (var (kind, raw) in new[] { (3, gold), (4, lumber), (6, 0L), (7, traits) })
        {
            var record = data.AsSpan((kind - 3) * PlayerResourceRecords.Stride);
            BinaryPrimitives.WriteInt64LittleEndian(record, raw);
            BinaryPrimitives.WriteUInt64LittleEndian(record[40..], 0x60666c675e70726f);
            BinaryPrimitives.WriteUInt32LittleEndian(record[48..], (uint)(owner * 40 + kind));
            BinaryPrimitives.WriteUInt32LittleEndian(record[52..], (uint)(owner * 40 + kind));
        }
        return data;
    }
}
