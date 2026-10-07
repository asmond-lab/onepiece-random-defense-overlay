using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideRuntimeReaderTests
{
    [Fact]
    public void SharedMissionFlagIsReadWithoutInventingCompletion()
    {
        var memory = new Memory();
        memory.Boolean("cu", true);
        var reader = new DestructionKingReader();
        Assert.True(reader.Read(memory.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0x100100, default));
        memory.Boolean("cu", false);
        Assert.False(reader.Read(memory.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0x100100, default));
        Assert.Null(reader.Read(memory.Read, "wrong", RouteQuestCatalog.MapScriptSha256, 0x100100, default));
    }

    [Fact]
    public void LoadedClearCountUsesVerifiedLocalSlotAndDoesNotImplyLogin()
    {
        var memory = new Memory();
        memory.Array("WE", [0, 40]);
        var reader = new LoadedClearCountReader();
        Assert.Equal(40, reader.Read(memory.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            1, 0x100100, default));
        Assert.Equal(0, reader.Read(memory.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            0, 0x100100, default));
        Assert.Null(reader.Read(memory.Read, "wrong", RouteQuestCatalog.MapScriptSha256,
            0, 0x100100, default));
        reader.Reset();
        Assert.Null(reader.Read(memory.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            3, 0x100100, default));
    }

    [Fact]
    public void ReadsLocalTiersWithoutPretendingTheyAreExactCharges()
    {
        var memory = new Memory();
        memory.Array("ag", [1, 3]); memory.Array("Bg", [2, 1]); memory.Array("Rg", [3, 2]);
        var result = memory.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 1, true);
        Assert.True(result.IsCurrent, result.Detail);
        Assert.Equal(3, result.ArmorTier);
        Assert.Equal(1, result.SpeedTier);
        Assert.Equal(2, result.AttackTier);
        Assert.Equal(40, result.BulletArmorReduction);
        Assert.Equal(20, result.BulletSlow);
    }

    [Theory]
    [InlineData(1, 5, 0)]
    [InlineData(2, 20, 20)]
    [InlineData(3, 40, 20)]
    public void UpgradeContributionsUseAuthoritativeTierEffects(int tier, int armor, int slow)
    {
        var memory = new Memory();
        memory.Array("ag", [tier]); memory.Array("Bg", [1]); memory.Array("Rg", [1]);
        var result = memory.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, true);
        Assert.Equal(armor, result.BulletArmorReduction);
        Assert.Equal(slow, result.BulletSlow);
    }

    [Fact]
    public void MissingOwnershipVersionMapOrOwnerNeverProducesUpgradeFacts()
    {
        var memory = new Memory();
        foreach (var (version, hash, owner, owns) in new (string, string, byte?, bool)[]
        {
            ("wrong", RouteQuestCatalog.MapScriptSha256, 0, true),
            ("2.0.4.23745", "wrong", 0, true),
            ("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, null, true),
            ("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, false)
        })
        {
            var result = memory.Reader().Read(version, hash, owner, owns);
            Assert.False(result.IsCurrent);
            Assert.Null(result.ArmorTier);
            Assert.Null(result.BulletSlow);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void InvalidTierFailsClosed(int tier)
    {
        var memory = new Memory();
        memory.Array("ag", [tier]); memory.Array("Bg", [1]); memory.Array("Rg", [1]);
        Assert.False(memory.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, true).IsCurrent);
    }

    [Fact]
    public void ResetAndMissingLocalArrayCannotReusePreviousTiers()
    {
        var memory = new Memory();
        memory.Array("ag", [3]); memory.Array("Bg", [3]); memory.Array("Rg", [3]);
        var reader = memory.Reader();
        Assert.True(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, true).IsCurrent);
        reader.Reset();
        Assert.False(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 1, true).IsCurrent);
    }

    private sealed class Memory
    {
        private const ulong Base = 0x100000;
        private readonly byte[] _bytes = new byte[0x20000];
        private readonly Dictionary<string, int> _nodes = [];
        private int _next = 0x8000;
        internal Memory()
        {
            var names = RouteQuestMemory.RequiredNames.Concat(new[] { "ag", "Bg", "Rg", "WE", "cu" }).ToArray();
            for (var i = 0; i < names.Length; i++)
            {
                var offset = 0x100 + i * 72;
                _nodes[names[i]] = offset;
                Put64(offset + 24, i == 0 ? 0 : Base + (ulong)(offset - 72 + 24));
                Put64(offset + 32, i + 1 == names.Length ? 0 : Base + (ulong)(offset + 72));
                Put64(offset + 40, Base + (ulong)(0x3000 + i * 32));
                Encoding.ASCII.GetBytes(names[i] + "\0").CopyTo(_bytes, 0x3000 + i * 32);
                var type = names[i] == "gr" ? 13 : 9;
                Put32(offset + 48, type); Put32(offset + 52, type);
            }
        }
        internal BulletGuideRuntimeReader Reader() => new(Read, () => new[] { (Base, _bytes) });
        internal void Boolean(string name, bool value)
        {
            Put32(_nodes[name] + 48, 8);
            Put32(_nodes[name] + 52, 8);
            Put32(_nodes[name] + 56, value ? 1 : 0);
        }
        internal void Array(string name, int[] values)
        {
            var header = _next; _next += 128;
            Put64(_nodes[name] + 56, Base + (ulong)header);
            Put32(header + 8, values.Length); Put32(header + 24, values.Length);
            Put64(header + 16, Base + (ulong)(header + 32));
            for (var i = 0; i < values.Length; i++) Put32(header + 32 + i * 4, values[i]);
        }
        internal byte[] Read(ulong address, int length) => address >= Base && address + (ulong)length <= Base + (ulong)_bytes.Length
            ? _bytes.AsSpan((int)(address - Base), length).ToArray() : [];
        private void Put32(int offset, int value) => BitConverter.GetBytes(value).CopyTo(_bytes, offset);
        private void Put64(int offset, ulong value) => BitConverter.GetBytes(value).CopyTo(_bytes, offset);
    }
}
