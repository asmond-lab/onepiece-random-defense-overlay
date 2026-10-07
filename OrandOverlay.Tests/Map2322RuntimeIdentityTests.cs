using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322RuntimeIdentityTests
{
    [Fact]
    public void DiagnosticPinsUseSelected2322FingerprintWithoutWeakeningExecutableOrAgeGates()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.322");
        var fingerprint = catalog.SelectedDatasetFingerprint;
        Assert.NotEqual(DiagnosticInventoryObservation.PinnedDatasetFingerprint, fingerprint);
        var now = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
        var context = new string('A', 64);
        var view = new string('B', 64);
        var binding = new string('C', 64);
        var basic = DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, context, 1, 0, now.AddMilliseconds(-100), now,
            TimeSpan.FromMilliseconds(100), [], view, binding);
        Assert.Equal(DiagnosticInventoryAvailability.Ready, basic.Availability);
        Assert.Equal(fingerprint, basic.DatasetFingerprint);
        Assert.Equal(Map2322SourceContract.JassSha256, basic.MapSourceFingerprint);
        var full = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, context, 2, 0, now.AddMilliseconds(-100), now,
            TimeSpan.FromMilliseconds(100), [], []);
        Assert.Equal(DiagnosticInventoryAvailability.Ready, full.Availability);
        Assert.True(DiagnosticInventoryConsumerPolicy.CanPresent(full, now, "2.322", fingerprint, context, 2, 0));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(full, now, "2.322",
            DiagnosticInventoryObservation.PinnedDatasetFingerprint, context, 2, 0));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(full, now, "2.320", fingerprint, context, 2, 0));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(full, now.AddSeconds(3), "2.322", fingerprint, context, 2, 0));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(full, now, "2.322", fingerprint, context, 3, 0));
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable,
            DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
                new string('0', 64), context, 2, 0, now.AddMilliseconds(-100), now,
                TimeSpan.FromMilliseconds(100), [], view, binding).Availability);
    }

    [Fact]
    public void ArchiveProofDoesNotApproveUnknownOrMismatched2322Bytes()
    {
        var matching = new RuntimeMapIdentityResult(RuntimeMapIdentityState.Proven, RuntimeMapIdentityFailure.None,
            "", null, "", "", Map2322SourceContract.ArchiveLengthBytes,
            Map2322SourceContract.ArchiveSha256, 0, 0, 0, 0);
        Assert.True(MapDatasetRuntimePolicy.AllowsObservedArchive("2.322", matching));
        Assert.False(MapDatasetRuntimePolicy.AllowsObservedArchive("2.322", matching with { State = RuntimeMapIdentityState.Unknown }));
        Assert.False(MapDatasetRuntimePolicy.AllowsObservedArchive("2.322", matching with { ActualArchiveSha256 = new string('0', 64) }));
        Assert.False(MapDatasetRuntimePolicy.AllowsObservedArchive("2.322", null));
        Assert.False(MapDatasetRuntimePolicy.AllowsReader("2.322", true, true));
    }
}
