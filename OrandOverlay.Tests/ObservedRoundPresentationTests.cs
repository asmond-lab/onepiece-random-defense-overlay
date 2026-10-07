using Xunit;

namespace OrandOverlay.Tests;

public sealed class ObservedRoundPresentationTests
{
    private static readonly Lazy<DataCatalog> Bundle = new(() => { var c = new DataCatalog(); c.Load(mapVersion: "2.320"); return c; });
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
    private static DiagnosticInventoryObservation Make(int? round) => DiagnosticInventoryObservation.Create(Bundle.Value,
        Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, new string('A', 64), 1, 0,
        Now.AddSeconds(-0.2), Now, TimeSpan.FromSeconds(0.2),
        [new InventoryEntry { UnitId = "rawcode:I10h", Count = 1 }], [], round);

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(65)]
    public void RoundReferenceDoesNotChangeInventoryOrGameplayAuthority(int round)
    {
        var value = Make(round);
        Assert.Equal(round, value.ObservedRound);
        Assert.Equal(1, value.Counts["rawcode:I10h"]);
        Assert.Equal($"{round}라운드", MainWindow.DiagnosticRoundDisplay(value));
        Assert.Equal(round, DiagnosticReferenceStats.From(value, true).ObservedRound);
        Assert.False(value.GameplayReady); Assert.False(value.CanProvideCoachCurrent);
        Assert.False(value.CanProveLocalOwnership); Assert.False(value.CanProveAlive);
        Assert.False(value.CanProveCompleteness); Assert.False(value.CanProveRuntimeMapCurrentness);
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(66)] [InlineData(int.MaxValue)]
    public void InvalidRoundIsUnknownWithoutDiscardingExistingUnits(int round)
    {
        var value = Make(round);
        Assert.Null(value.ObservedRound);
        Assert.Equal(DiagnosticInventoryAvailability.Ready, value.Availability);
        Assert.Equal(1, value.Entries.Single().Count);
        Assert.Equal("라운드 확인 중", MainWindow.DiagnosticRoundDisplay(value));
    }

    [Fact]
    public void MissingOrExpiredObservationsDoNotKeepLastRound()
    {
        Assert.Null(DiagnosticReferenceStats.From(Make(2), false).ObservedRound);
        Assert.Null(DiagnosticReferenceStats.From(null, true).ObservedRound);
        Assert.Equal("라운드 확인 중", MainWindow.DiagnosticRoundDisplay(null));
        var value = Make(1);
        Assert.True(DiagnosticInventoryConsumerPolicy.CanPresent(value, Now, "2.320", value.DatasetFingerprint,
            value.ContextId, value.SourceRevision, 0));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(value, value.StartedAt.AddSeconds(3), "2.320", value.DatasetFingerprint,
            value.ContextId, value.SourceRevision, 0));
        var unavailable = DiagnosticInventoryObservation.Unavailable("2.320", value.DatasetFingerprint,
            value.ExecutableVersion, value.ExecutableFingerprint, 2, Now, Now, TimeSpan.Zero, "read failure");
        Assert.Null(unavailable.ObservedRound);
    }

    [Fact]
    public void ExactReferenceLaneDoesNotActivateProductionProfile()
    {
        var profile = new MemoryProfile { Layout = Warcraft300Diagnostic.LayoutName,
            FileVersion = Warcraft300Diagnostic.Version, Sha256 = Warcraft300Diagnostic.Hash,
            Enabled = false, Verified = false };
        Assert.True(MapDatasetRuntimePolicy.AllowsReferenceObservation("2.320", DiagnosticInventoryObservation.PinnedDatasetFingerprint,
            profile, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash));
        Assert.False(profile.Enabled); Assert.False(profile.Verified);
        Assert.False(MemoryProfileValidator.CanActivate(profile, out _));
        Assert.False(MapDatasetRuntimePolicy.AllowsLegacyRuntime("2.320"));
        Assert.False(MapDatasetRuntimePolicy.AllowsReferenceObservation("2.314", DiagnosticInventoryObservation.PinnedDatasetFingerprint,
            profile, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash));
        Assert.False(MapDatasetRuntimePolicy.AllowsReferenceObservation("2.320", new string('0', 64),
            profile, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash));
        Assert.False(MapDatasetRuntimePolicy.AllowsReferenceObservation("2.320", DiagnosticInventoryObservation.PinnedDatasetFingerprint,
            profile, "3.0.0.24269", Warcraft300Diagnostic.Hash));
        Assert.False(MapDatasetRuntimePolicy.AllowsReferenceObservation("2.320", DiagnosticInventoryObservation.PinnedDatasetFingerprint,
            profile, Warcraft300Diagnostic.Version, new string('0', 64)));
    }
}
