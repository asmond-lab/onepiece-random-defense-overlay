using System.Collections.Immutable;
using Xunit;
using Scope = OrandOverlay.Tests.ObservedTelemetryClientTests.Scope;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryOutboxTests
{
    [Fact]
    public async Task RepeatedIdentityIsIdempotentButChangedContentCannotReplaceIt()
    {
        using var scope = new Scope();
        var outbox = scope.Outbox();
        var packet = ObservedTelemetryWireTests.Packet();
        Assert.True(await outbox.EnqueueAsync(packet));
        Assert.True(await outbox.EnqueueAsync(packet));
        var original = await File.ReadAllBytesAsync(Assert.Single(scope.PendingFiles()));
        Assert.False(await outbox.EnqueueAsync(packet with { Events = [packet.Events[0] with { State = "fresh" }] }));
        Assert.Equal(original, await File.ReadAllBytesAsync(Assert.Single(scope.PendingFiles())));
    }

    [Fact]
    public async Task ExpiredPendingPacketIsRemovedBeforeAnyHttp()
    {
        using var scope = new Scope();
        var outbox = scope.Outbox();
        Assert.True(await outbox.EnqueueAsync(ObservedTelemetryWireTests.Packet()));
        File.SetLastWriteTimeUtc(Assert.Single(scope.PendingFiles()), DateTime.UtcNow.AddDays(-31));
        Assert.Equal(0, await outbox.FlushAsync());
        Assert.Empty(scope.PendingFiles());
        Assert.Equal(0, scope.Handler.Calls);
    }

    [Fact]
    public async Task FullDiskQueueRefusesNewPacketsWithoutEvictingExistingFiles()
    {
        using var scope = new Scope();
        var folder = Path.Combine(scope.Root, "observed-v4", "pending");
        Directory.CreateDirectory(folder);
        for (var index = 0; index < ObservedTelemetryOutbox.MaxPackets; index++)
            await File.WriteAllTextAsync(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json"), "{}");
        var before = scope.PendingFiles().Order().ToArray();
        var outbox = scope.Outbox();
        Assert.False(await outbox.EnqueueAsync(ObservedTelemetryWireTests.Packet()));
        Assert.Equal(before, scope.PendingFiles().Order());
        Assert.Equal(1, outbox.CapacityRefusalCount);
    }

    [Fact]
    public async Task StorageByteCapIsEnforcedBelowThePacketCountCap()
    {
        using var scope = new Scope();
        var folder = Path.Combine(scope.Root, "observed-v4", "pending");
        Directory.CreateDirectory(folder);
        var filling = new byte[ObservedTelemetryWire.MaxBytes];
        for (var index = 0; index < 128; index++)
            await File.WriteAllBytesAsync(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json"), filling);
        var outbox = scope.Outbox();
        Assert.False(await outbox.EnqueueAsync(ObservedTelemetryWireTests.Packet()));
        Assert.Equal(128, scope.PendingFiles().Length);
        Assert.Equal(1, outbox.CapacityRefusalCount);
    }

    [Fact]
    public async Task InvalidRestartFileCannotBeUploadedAndDoesNotBlockValidPacket()
    {
        using var scope = new Scope();
        var outbox = scope.Outbox();
        Assert.True(await outbox.EnqueueAsync(ObservedTelemetryWireTests.Packet()));
        var folder = Path.GetDirectoryName(Assert.Single(scope.PendingFiles()))!;
        var invalid = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(invalid, "[]");
        Assert.Equal(1, await outbox.FlushAsync());
        Assert.Equal(invalid, Assert.Single(scope.PendingFiles()));
        Assert.Single(scope.Handler.Packets);
    }

    [Fact]
    public async Task DirectOutboxConsentGateProtectsBothStorageAndTransport()
    {
        using var scope = new Scope { Allowed = false };
        var outbox = scope.Outbox();
        Assert.False(await outbox.EnqueueAsync(ObservedTelemetryWireTests.Packet()));
        Assert.Equal(0, await outbox.FlushAsync());
        Assert.False(Directory.Exists(scope.Root));
        Assert.Equal(0, scope.Handler.Calls);
    }
}
