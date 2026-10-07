using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticGrowthWitnessTests
{
    private static readonly Warcraft300Diagnostic.View View = new(400, 0, 500);
    private static readonly Warcraft300HandleStamp Allocation = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17);
    private static readonly Warcraft300Diagnostic.Unit Unit = new(100, 27, 123, Allocation);

    [Fact]
    public void WitnessIsPhysicalIdentityAndIgnoresVolatileAllocationStamps()
    {
        var stamp = DiagnosticInventoryBinding.GrowthUnit("salt", View, Unit);
        Assert.Matches("^[A-F0-9]{64}$", stamp);
        Assert.Equal(stamp, DiagnosticInventoryBinding.GrowthUnit("salt", View, Unit with { }));
        var allocations = new[]
        {
            Allocation with { ModuleBase = 100 }, Allocation with { Unit = 100 }, Allocation with { Vtable = 100 },
            Allocation with { RegistryGlobal = 100 }, Allocation with { Registry = 100 }, Allocation with { RawHandle = 100 },
            Allocation with { Serial = 100 }, Allocation with { Table = 100 }, Allocation with { TableLimit = 100 },
            Allocation with { Slot = 100 }, Allocation with { Marker = 100 }, Allocation with { Record = 100 },
            Allocation with { RecordSerial = 100 }, Allocation with { TypeId = 100 }, Allocation with { BackReference = 100 },
            Allocation with { State30 = 100 }, Allocation with { State83 = 100 }
        };
        foreach (var allocation in allocations)
            Assert.Equal(stamp, DiagnosticInventoryBinding.GrowthUnit("salt", View, Unit with { Allocation = allocation }));
        foreach (var unit in new[] { Unit with { Address = 101 }, Unit with { Owner = 0 }, Unit with { Rawcode = 124 } })
            Assert.NotEqual(stamp, DiagnosticInventoryBinding.GrowthUnit("salt", View, unit));
        foreach (var view in new[] { View with { Root = 401 }, View with { Slot = 1 }, View with { Player = 501 } })
            Assert.NotEqual(stamp, DiagnosticInventoryBinding.GrowthUnit("salt", view, Unit));
        Assert.NotEqual(stamp, DiagnosticInventoryBinding.GrowthUnit("other process salt", View, Unit));
    }

    [Fact]
    public void BasicProducerIncludesOnlyNeutralRecognizedGrowthCandidatesAndCopiesProofImmutably()
    {
        var data = new DataCatalog(); data.Load(mapVersion: "2.321");
        static uint Raw(string code) { Assert.True(RawcodeCodec.TryParse(code, out var raw)); return raw; }
        var units = new[]
        {
            Unit with { Rawcode = Raw("I10h") },
            Unit with { Address = 101, Owner = 0, Rawcode = Raw("100h") },
            Unit with { Address = 102, Owner = 5, Rawcode = Raw("B00h") },
            Unit with { Address = 103, Owner = 27, Rawcode = Raw("100h") },
            Unit with { Address = 104, Owner = 27, Rawcode = Raw("zzzz") }
        };
        var snapshot = new Warcraft300Diagnostic.Inventory(View, 5, 1, 4, new() { [Raw("100h")] = 1 })
            { Units = units, EntryAddresses = units.Select(unit => unit.Address).ToArray() };
        var time = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var service = new WarcraftMemoryRecognitionService(data, Path.GetTempPath());
        var sample = service.CreateBasicInventorySample(snapshot, new(1, 2, 3, 4, 5, 6, 7, 8, 9),
            new MemoryProfile { MinimumCatalogMatchRatio = .6, RequireNonEmptyInventory = true },
            new RecognitionDiagnostics { Source = DiagnosticBasicInventoryObservation.SourceName,
                ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash },
            "session", 1, time, time.AddMilliseconds(10), TimeSpan.FromMilliseconds(10));
        Assert.Equal(DiagnosticInventoryAvailability.Ready, sample.Observation.Availability);
        var witness = Assert.Single(sample.Observation.GrowthUnitFingerprints);
        Assert.False(sample.Observation.GrowthAttributionAvailable);
        Assert.False(sample.Observation.GameplayReady);
        Assert.Single(sample.Observation.Counts);
        units[0] = units[0] with { Owner = 1 };
        Assert.Equal(witness, Assert.Single(sample.Observation.GrowthUnitFingerprints));
    }

    [Fact]
    public void MalformedOrUnboundedProofsCannotMintReadyObservations()
    {
        var data = new DataCatalog(); data.Load(mapVersion: "2.321");
        var time = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var context = new string('A', 64);
        DiagnosticBasicInventoryObservation Basic(IEnumerable<string> proof) => DiagnosticBasicInventoryObservation.Create(data,
            Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, context, 1, 0, time, time,
            TimeSpan.Zero, [new() { UnitId = "rawcode:100h", Count = 1 }], context, context, proof);
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, Basic(["not-a-witness"]).Availability);
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable,
            Basic(Enumerable.Repeat(context, DiagnosticInventoryObservation.MaximumEntries + 1)).Availability);
        var mutable = new[] { context };
        var basic = Basic(mutable);
        mutable[0] = new('B', 64);
        Assert.Equal(context, Assert.Single(basic.GrowthUnitFingerprints));
        DiagnosticInventoryObservation Full(string proof, string[] reservations) => DiagnosticInventoryObservation.Create(data,
            Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, context, 1, 0, time, time,
            TimeSpan.Zero, [new() { UnitId = "rawcode:I10h", Count = 1 }, new() { UnitId = "rawcode:B00h", Count = 1 }],
            reservations, 2, context, context, proof);
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, Full("bad", ["rawcode:I10h"]).Availability);
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, Full(context, []).Availability);
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, Full(context, ["rawcode:I10h", "rawcode:B00h"]).Availability);
        Assert.Equal(DiagnosticInventoryAvailability.Ready, Full(context, ["rawcode:I10h"]).Availability);
    }
}
