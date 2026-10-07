using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticGrowthContinuationTests
{
    private static readonly Lazy<DataCatalog> Data = new(() =>
    {
        var data = new DataCatalog(); data.Load(mapVersion: "2.321"); return data;
    });
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
    private static readonly Warcraft300Diagnostic.View View = new(0x10000, 0, 0x20000);
    private static readonly Warcraft300WorldLocator.Context Locator = new(1, 2, 3, 4, 5, 6, 7, 0x30000, 0x40000);
    private static uint Code(string rawcode) { Assert.True(RawcodeCodec.TryParse(rawcode, out var value)); return value; }
    private static Warcraft300Diagnostic.Unit Unit(ulong address, byte owner, string rawcode) => new(address, owner, Code(rawcode),
        new(0x100000, address, 0x200000, 0x300000, 0x400000, (uint)address, 7, 0x500000, 100, 0x600000,
            0xFFFFFFFE, 0x700000, 7, Warcraft300HandleValidator.UnitTypeId, address, 0, 0));
    private static readonly Warcraft300Diagnostic.Unit Growth = Unit(0x90000, 27, "I10h");
    private static Warcraft300Diagnostic.Inventory World(params Warcraft300Diagnostic.Unit[] additional)
    {
        var units = new[] { Unit(0x80000, 0, "100h"), Growth }.Concat(additional).ToArray();
        return Snapshot(units);
    }
    private static Warcraft300Diagnostic.Inventory Snapshot(Warcraft300Diagnostic.Unit[] units) => new(View, units.Length,
        units.Count(x => x.Owner == View.Slot), units.Count(x => x.Owner != View.Slot),
        units.Where(x => x.Owner == View.Slot).GroupBy(x => x.Rawcode).ToDictionary(x => x.Key, x => x.Count()))
        { Units = units, EntryAddresses = units.Select(x => x.Address).ToArray() };
    private static RecognitionDiagnostics Diagnostics(string source) => new()
        { Source = source, ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash };
    private readonly WarcraftMemoryRecognitionService _service = new(Data.Value, Path.GetTempPath());
    private DiagnosticBasicInventorySample Basic(Warcraft300Diagnostic.Inventory snapshot, long revision, DateTimeOffset time) =>
        _service.CreateBasicInventorySample(snapshot, Locator, new MemoryProfile
            { MinimumCatalogMatchRatio = 0.6, RequireNonEmptyInventory = true },
            Diagnostics(DiagnosticBasicInventoryObservation.SourceName), "growth-replay", revision,
            time.AddMilliseconds(-10), time, TimeSpan.FromMilliseconds(10));
    private static RecognitionResult Full(DiagnosticBasicInventorySample basic, long revision, string? growth = "I10h", bool includeProof = true) => new()
    {
        State = RecognitionState.Ready, Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName), CompletionBasicSample = basic,
        DiagnosticObservation = DiagnosticInventoryObservation.Create(Data.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            new string('F', 64), revision, 0, basic.Observation.StartedAt, basic.Observation.CompletedAt, basic.Observation.ReadDuration,
            basic.Observation.CloneEntries().Concat(growth is not null ? new[] { new InventoryEntry { UnitId = "rawcode:" + growth, Count = 1 } } : [])
                .GroupBy(entry => entry.UnitId).Select(group => new InventoryEntry { UnitId = group.Key, Count = group.Sum(entry => entry.Count) }),
            growth is not null ? ["rawcode:" + growth] : [], 2, basic.Observation.WorldStampFingerprint, basic.Observation.BindingContextId,
            growth is not null && includeProof ? basic.Observation.GrowthUnitFingerprints.Single() : "")
    };
    private static void Accept(DiagnosticInventoryPresentationState state, DiagnosticBasicInventorySample sample, DateTimeOffset time) =>
        Assert.True(state.TryAcceptBasic(sample.Observation, sample.Diagnostics, time, "2.321", Data.Value.OfflineBundle!.Fingerprint, out _));
    private static void Accept(DiagnosticInventoryPresentationState state, RecognitionResult full, DateTimeOffset time) =>
        Assert.True(state.TryAcceptFull(full, time, "2.321", Data.Value.OfflineBundle!.Fingerprint, out _));
    private static IDiagnosticInventoryReference Current(DiagnosticInventoryPresentationState state, DateTimeOffset time) =>
        Assert.IsAssignableFrom<IDiagnosticInventoryReference>(state.Current(time, "2.321", Data.Value.OfflineBundle!.Fingerprint));

    [Fact]
    public void MissingProofCannotUseEqualCountsToContinueAcrossWorldChanges()
    {
        var state = new DiagnosticInventoryPresentationState();
        Accept(state, Full(Basic(World(), 1, Now), 1, includeProof: false), Now);
        var next = Basic(World(Unit(0xA0000, 12, "zzzz")), 2, Now.AddMilliseconds(100));
        Accept(state, next, next.Observation.CompletedAt);
        Assert.Same(next.Observation, Current(state, next.Observation.CompletedAt));
    }

    [Fact]
    public void ANewFullStillRequiresExactCompletionWorldEvenWithMatchingUnitProof()
    {
        var old = Full(Basic(World(), 1, Now), 1);
        var next = Basic(World(Unit(0xA0000, 12, "zzzz")), 2, Now.AddMilliseconds(100));
        var state = new DiagnosticInventoryPresentationState();
        Accept(state, next, next.Observation.CompletedAt);
        Assert.False(state.TryAcceptFull(new RecognitionResult { State = old.State, Diagnostics = old.Diagnostics,
            DiagnosticObservation = old.DiagnosticObservation }, next.Observation.CompletedAt,
            "2.321", Data.Value.OfflineBundle!.Fingerprint, out _));
        Assert.Same(next.Observation, Current(state, next.Observation.CompletedAt));
    }

    [Fact]
    public void DistinctOwnedAndDormantSameRawcodeAreCountedOnceEach()
    {
        var state = new DiagnosticInventoryPresentationState();
        Accept(state, Full(Basic(World(Unit(0xA0000, 0, "I10h")), 1, Now), 1), Now);
        var next = Basic(World(Unit(0xA0000, 0, "I10h"), Unit(0xB0000, 0, "I10h")), 2, Now.AddMilliseconds(100));
        Accept(state, next, next.Observation.CompletedAt);
        Assert.Equal(3, Current(state, next.Observation.CompletedAt).Counts["rawcode:I10h"]);
        Assert.Equal(2, next.Observation.Counts["rawcode:I10h"]);
    }

    [Fact]
    public void BasicWitnessRequiresTheSpecificPhysicalUnitNotAnotherNeutralOfSameRawcode()
    {
        var state = new DiagnosticInventoryPresentationState();
        Accept(state, Full(Basic(World(), 1, Now), 1), Now);
        var next = Basic(Snapshot([Unit(0x80000, 0, "100h"), Unit(0xA0000, 27, "I10h")]), 2, Now.AddMilliseconds(100));
        Accept(state, next, next.Observation.CompletedAt);
        Assert.Same(next.Observation, Current(state, next.Observation.CompletedAt));
        Assert.False(next.Observation.Counts.ContainsKey("rawcode:I10h"));
    }

    [Fact]
    public void BasicWitnessIgnoresVectorAliasesOrderAndUnregisteredOrForeignObjects()
    {
        var original = World();
        var initial = Basic(original, 1, Now);
        var state = new DiagnosticInventoryPresentationState();
        Accept(state, Full(initial, 1), Now);
        var changed = original with { Count = 4, EntryAddresses = [Growth.Address, 0xB0000, 0x80000, Growth.Address],
            Units = original.Units.Reverse().ToArray(),
            UnregisteredEntries = [new(1, 0xB0000, 0x200000, uint.MaxValue, uint.MaxValue)] };
        var next = Basic(changed, 2, Now.AddMilliseconds(100));
        Assert.NotEqual(initial.Observation.WorldStampFingerprint, next.Observation.WorldStampFingerprint);
        Assert.Equal(initial.Observation.GrowthUnitFingerprints.Single(), next.Observation.GrowthUnitFingerprints.Single());
        Accept(state, next, next.Observation.CompletedAt);
        Assert.Equal(1, Current(state, next.Observation.CompletedAt).Counts["rawcode:I10h"]);
    }

    [Fact]
    public void BasicAddRemoveAndCountChangesStayImmediateWhileGrowthIsStable()
    {
        var state = new DiagnosticInventoryPresentationState();
        var full = Full(Basic(World(), 1, Now), 1);
        Accept(state, full, Now);
        var added = Basic(World(Unit(0xA0000, 0, "800h"), Unit(0xB0000, 0, "100h")), 2, Now.AddMilliseconds(100));
        Accept(state, added, added.Observation.CompletedAt);
        var current = Current(state, added.Observation.CompletedAt);
        Assert.Equal(1, current.Counts["rawcode:I10h"]);
        Assert.Equal(1, current.Counts["rawcode:800h"]);
        Assert.Equal(2, current.Counts["rawcode:100h"]);
        Assert.Equal(full.DiagnosticObservation!.StartedAt, current.StartedAt);
        var removed = Basic(Snapshot([Unit(0xA0000, 0, "800h"), Growth]), 3, Now.AddMilliseconds(200));
        Accept(state, removed, removed.Observation.CompletedAt);
        current = Current(state, removed.Observation.CompletedAt);
        Assert.Equal(1, current.Counts["rawcode:I10h"]);
        Assert.Equal(1, current.Counts["rawcode:800h"]);
        Assert.False(current.Counts.ContainsKey("rawcode:100h"));
    }

    [Fact]
    public void AllocationStampChurnKeepsAttributedGrowthAcrossForeignWorldChange()
    {
        var state = new DiagnosticInventoryPresentationState();
        var initial = Full(Basic(World(), 1, Now), 1);
        Accept(state, initial, Now);
        var churned = Growth with { Allocation = Growth.Allocation with { Serial = 8, RawHandle = 9 } };
        var next = Basic(Snapshot([Unit(0x80000, 0, "100h"), churned, Unit(0xB0000, 12, "zzzz")]), 2,
            Now.AddMilliseconds(100));
        Assert.NotEqual(initial.DiagnosticObservation!.WorldStampFingerprint, next.Observation.WorldStampFingerprint);
        Assert.Equal(initial.DiagnosticObservation.GrowthUnitFingerprint, next.Observation.GrowthUnitFingerprints.Single());
        Accept(state, next, next.Observation.CompletedAt);
        var current = Current(state, next.Observation.CompletedAt);
        Assert.Equal(1, current.Counts["rawcode:I10h"]);
        Assert.Same(current, Current(state, next.Observation.CompletedAt));
        Assert.Equal(initial.DiagnosticObservation.StartedAt, current.StartedAt);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("foreign")]
    [InlineData("owned")]
    [InlineData("rawcode")]
    [InlineData("address")]
    public void GrowthIdentityChangesCannotBorrowTheOldSupplement(string change)
    {
        var state = new DiagnosticInventoryPresentationState();
        Accept(state, Full(Basic(World(), 1, Now), 1), Now);
        var changed = change switch
        {
            "foreign" => Growth with { Owner = 5 },
            "owned" => Growth with { Owner = 0 },
            "rawcode" => Growth with { Rawcode = Code("B00h") },
            "address" => Growth with { Address = Growth.Address + 1 },
            "allocation" => Growth with { Allocation = Growth.Allocation with { Serial = 8 } },
            _ => Growth
        };
        var next = Basic(Snapshot(change == "deleted" ? [Unit(0x80000, 0, "100h")] :
            [Unit(0x80000, 0, "100h"), changed]), 2, Now.AddMilliseconds(100));
        Accept(state, next, next.Observation.CompletedAt);
        var current = Current(state, next.Observation.CompletedAt);
        Assert.Same(next.Observation, current);
        Assert.Equal(change == "owned" ? 1 : 0, current.Counts.GetValueOrDefault("rawcode:I10h"));
        Assert.False(current.GrowthAttributionAvailable);
    }

    [Fact]
    public void FullRemovalAndReplacementAreImmediateEvenWhenOldPhysicalUnitStillExists()
    {
        var state = new DiagnosticInventoryPresentationState();
        Accept(state, Full(Basic(World(), 1, Now), 1), Now);
        var absence = Full(Basic(World(), 2, Now.AddMilliseconds(100)), 2, growth: null);
        Accept(state, absence, absence.DiagnosticObservation!.CompletedAt);
        Assert.False(Current(state, absence.DiagnosticObservation.CompletedAt).Counts.ContainsKey("rawcode:I10h"));
        var changed = Snapshot([Unit(0x80000, 0, "100h"), Growth with { Rawcode = Code("B00h") }]);
        var replacement = Full(Basic(changed, 3, Now.AddMilliseconds(200)), 3, "B00h");
        Accept(state, replacement, replacement.DiagnosticObservation!.CompletedAt);
        var current = Current(state, replacement.DiagnosticObservation.CompletedAt);
        Assert.False(current.Counts.ContainsKey("rawcode:I10h"));
        Assert.Equal(1, current.Counts["rawcode:B00h"]);
    }

    [Fact]
    public void TransientFullFailureKeepsAttributedGrowthWhileWitnessRemains()
    {
        var state = new DiagnosticInventoryPresentationState();
        var initial = Full(Basic(World(), 1, Now), 1);
        Accept(state, initial, Now);
        var next = Basic(World(Unit(0xA0000, 12, "zzzz")), 2, Now.AddMilliseconds(100));
        Accept(state, next, next.Observation.CompletedAt);
        Assert.Equal(1, Current(state, next.Observation.CompletedAt).Counts["rawcode:I10h"]);
        Assert.False(state.TryAcceptFull(new RecognitionResult { State = RecognitionState.TransientReadError,
            Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName),
            Status = "Handle index or table limit is outside safety bounds." }, next.Observation.CompletedAt,
            "2.321", Data.Value.OfflineBundle!.Fingerprint, out _));
        var kept = Current(state, next.Observation.CompletedAt);
        Assert.Equal(1, kept.Counts["rawcode:I10h"]);
        Assert.Same(kept, Current(state, next.Observation.CompletedAt));
        Assert.Equal(initial.DiagnosticObservation!.StartedAt, kept.StartedAt);
        var later = Basic(World(), 3, Now.AddMilliseconds(200));
        Accept(state, later, later.Observation.CompletedAt);
        Assert.Equal(1, Current(state, later.Observation.CompletedAt).Counts["rawcode:I10h"]);
    }

    [Fact]
    public void SessionBoundaryStillClearsGrowthAndBasicCannotRestoreIt()
    {
        var state = new DiagnosticInventoryPresentationState();
        Accept(state, Full(Basic(World(), 1, Now), 1), Now);
        var next = Basic(World(Unit(0xA0000, 12, "zzzz")), 2, Now.AddMilliseconds(100));
        Accept(state, next, next.Observation.CompletedAt);
        Assert.False(state.TryAcceptFull(new RecognitionResult { State = RecognitionState.Waiting,
            ConfirmsSessionBoundary = true,
            Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName) }, next.Observation.CompletedAt,
            "2.321", Data.Value.OfflineBundle!.Fingerprint, out _));
        Assert.Null(state.Current(next.Observation.CompletedAt, "2.321", Data.Value.OfflineBundle!.Fingerprint));
        var later = Basic(World(), 3, Now.AddMilliseconds(200));
        Accept(state, later, later.Observation.CompletedAt);
        Assert.Same(later.Observation, Current(state, later.Observation.CompletedAt));
    }

    [Fact]
    public void ContinuationDoesNotRenewGrowthFreshnessOrRelaxMapAndSessionFences()
    {
        var state = new DiagnosticInventoryPresentationState();
        Accept(state, Full(Basic(World(), 1, Now), 1), Now);
        var later = Basic(World(Unit(0xA0000, 12, "zzzz")), 2, Now.AddSeconds(3));
        Accept(state, later, later.Observation.CompletedAt);
        Assert.Same(later.Observation, Current(state, later.Observation.CompletedAt));
        Assert.Null(state.Current(later.Observation.CompletedAt, "2.320", Data.Value.OfflineBundle!.Fingerprint));

        state = new();
        Accept(state, Full(Basic(World(), 1, Now), 1), Now);
        var nextSessionService = new WarcraftMemoryRecognitionService(Data.Value, Path.GetTempPath());
        var nextSession = nextSessionService.CreateBasicInventorySample(World(), Locator, new MemoryProfile
            { MinimumCatalogMatchRatio = 0.6, RequireNonEmptyInventory = true },
            Diagnostics(DiagnosticBasicInventoryObservation.SourceName), "other-session", 2,
            Now, Now.AddMilliseconds(100), TimeSpan.FromMilliseconds(100));
        Accept(state, nextSession, nextSession.Observation.CompletedAt);
        Assert.Same(nextSession.Observation, Current(state, nextSession.Observation.CompletedAt));
    }

    [Fact]
    public void StableGrowthSurvivesUnrelatedForeignWorldActivity()
    {
        var state = new DiagnosticInventoryPresentationState();
        var initial = Basic(World(), 1, Now);
        Accept(state, Full(initial, 1), Now);
        Assert.Equal(1, Current(state, Now).Counts["rawcode:I10h"]);
        for (var index = 1; index <= 8; index++)
        {
            var time = Now.AddMilliseconds(index * 100);
            var sample = Basic(World(Unit((ulong)(0xA0000 + index), 12, "zzzz")), index + 1, time);
            Assert.NotEqual(initial.Observation.WorldStampFingerprint, sample.Observation.WorldStampFingerprint);
            Assert.Equal(initial.Observation.BindingContextId, sample.Observation.BindingContextId);
            Accept(state, sample, time);
            var current = Current(state, time);
            Assert.Same(current, Current(state, time)); // WPF revalidates the exact accepted object identity.
            Assert.Equal(1, current.Counts.GetValueOrDefault("rawcode:I10h"));
            Assert.Equal(1, current.Counts["rawcode:100h"]);
            Assert.False(current.GameplayReady);
            Assert.False(current.CanProveLocalOwnership);
        }
    }
}
