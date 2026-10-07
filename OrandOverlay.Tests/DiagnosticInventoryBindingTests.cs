using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticInventoryBindingTests
{
    [Fact]
    public void WorldStampMatchesExactWorldEqualityAndChangesForEveryIdentityField()
    {
        var allocation = new Warcraft300HandleStamp(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17);
        var unit = new Warcraft300Diagnostic.Unit(100, 0, 123, allocation);
        var entry = new Warcraft300Diagnostic.UnregisteredEntry(1, 200, 300, uint.MaxValue, uint.MaxValue);
        var original = new Warcraft300Diagnostic.Inventory(new(400, 0, 500), 2, 1, 0, new() { [123] = 1 })
        { EntryAddresses = new ulong[] { 100, 200 }, Units = new[] { unit }, UnregisteredEntries = new[] { entry } };
        var stamp = DiagnosticInventoryBinding.World("salt", original);
        Assert.Matches("^[A-F0-9]{64}$", stamp);
        Assert.Equal(stamp, DiagnosticInventoryBinding.World("salt", original with { }));
        var changes = new List<Warcraft300Diagnostic.Inventory>
        {
            original with { CurrentView = original.CurrentView with { Root = 401 } },
            original with { CurrentView = original.CurrentView with { Slot = 1 } },
            original with { CurrentView = original.CurrentView with { Player = 501 } },
            original with { Count = 3 }, original with { Owned = 2 }, original with { Foreign = 1 },
            original with { EntryAddresses = new ulong[] { 200, 100 } },
            original with { EntryAddresses = new ulong[] { 100, 100 } },
            original with { EntryAddresses = new ulong[] { 100 } },
            original with { Units = new[] { unit with { Address = 101 } } },
            original with { Units = new[] { unit with { Owner = 1 } } },
            original with { Units = new[] { unit with { Rawcode = 124 } } },
            original with { UnregisteredEntries = new[] { entry with { Index = 2 } } },
            original with { UnregisteredEntries = new[] { entry with { Address = 201 } } },
            original with { UnregisteredEntries = new[] { entry with { Vtable = 301 } } },
            original with { UnregisteredEntries = new[] { entry with { Handle = 1 } } },
            original with { UnregisteredEntries = new[] { entry with { Serial = 1 } } }
        };
        var allocations = new[]
        {
            allocation with { ModuleBase = 100 }, allocation with { Unit = 100 }, allocation with { Vtable = 100 },
            allocation with { RegistryGlobal = 100 }, allocation with { Registry = 100 }, allocation with { RawHandle = 100 },
            allocation with { Serial = 100 }, allocation with { Table = 100 }, allocation with { TableLimit = 100 },
            allocation with { Slot = 100 }, allocation with { Marker = 100 }, allocation with { Record = 100 },
            allocation with { RecordSerial = 100 }, allocation with { TypeId = 100 }, allocation with { BackReference = 100 },
            allocation with { State30 = 100 }, allocation with { State83 = 100 }
        };
        changes.AddRange(allocations.Select(changed => original with { Units = new[] { unit with { Allocation = changed } } }));
        foreach (var changed in changes)
        {
            Assert.False(original.SameWorldEntries(changed));
            Assert.NotEqual(stamp, DiagnosticInventoryBinding.World("salt", changed));
        }
    }

    [Fact]
    public void BindingIsSaltedAndChangesWithSessionLocatorOrCurrentView()
    {
        var locator = new Warcraft300WorldLocator.Context(1, 2, 3, 4, 5, 6, 7, 8, 9);
        var view = new Warcraft300Diagnostic.View(10, 0, 11);
        var binding = DiagnosticInventoryBinding.Context("salt", "session", locator, view);
        Assert.Matches("^[A-F0-9]{64}$", binding);
        Assert.Equal(binding, DiagnosticInventoryBinding.Context("salt", "session", locator, view));
        Assert.NotEqual(binding, DiagnosticInventoryBinding.Context("other salt", "session", locator, view));
        Assert.NotEqual(binding, DiagnosticInventoryBinding.Context("salt", "next session", locator, view));
        Assert.NotEqual(binding, DiagnosticInventoryBinding.Context("salt", "session", locator with { World = 12 }, view));
        Assert.NotEqual(binding, DiagnosticInventoryBinding.Context("salt", "session", locator, view with { Slot = 1 }));
    }
}
