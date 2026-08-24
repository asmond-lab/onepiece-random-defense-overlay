using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class LocalPlayerSlotResolverTests
{
    private const ulong Root = 0x0000012345678000;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void StableHumanSlot_AcceptsEveryOrdrPlayer(int slot)
    {
        Assert.Equal((byte)slot,
            LocalPlayerSlotResolver.Resolve(Root, (ushort)slot, Root, (ushort)slot));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(27)]
    public void NonHumanMapOwner_IsRejected(int slot)
    {
        Assert.Null(LocalPlayerSlotResolver.Resolve(Root, (ushort)slot, Root, (ushort)slot));
    }

    [Fact]
    public void ChangedRoot_IsRejectedAsSnapshotRace()
    {
        Assert.Null(LocalPlayerSlotResolver.Resolve(Root, 2, Root + 0x1000, 2));
    }

    [Fact]
    public void ChangedSlot_IsRejectedAsSnapshotRace()
    {
        Assert.Null(LocalPlayerSlotResolver.Resolve(Root, 0, Root, 3));
    }
}
