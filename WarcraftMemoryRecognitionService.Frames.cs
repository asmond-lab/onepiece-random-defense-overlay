using System.ComponentModel;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OrandOverlay;

public sealed partial class WarcraftMemoryRecognitionService
{
    private long _diagnosticBasicRevision;
    private readonly SemaphoreSlim _nativeReadAdmission = new(1, 1);

    // All entry points share admission, including cancellation while waiting to enter.
    internal T RunNativeRead<T>(Func<T> read, CancellationToken token)
    {
        _nativeReadAdmission.Wait(token);
        var thread = Thread.CurrentThread;
        var previousPriority = thread.Priority;
        try
        {
            token.ThrowIfCancellationRequested();
            thread.Priority = ThreadPriority.BelowNormal;
            return read();
        }
        finally
        {
            thread.Priority = previousPriority;
            _nativeReadAdmission.Release();
        }
    }

    IAsyncEnumerable<DiagnosticRecognitionFrame> IModernInventoryFrameSource.RecognizeFramesAsync(
        AppSettings settings, CancellationToken token) =>
        DiagnosticRecognitionStream.Run<DiagnosticRecognitionFrame>((emit, tryEmit, workerToken) =>
        {
            var result = RunNativeRead(() => Recognize(workerToken,
                PlayModes.Current(settings) == PlayMode.Guide && settings.GuideNumber == 1, tryEmit), workerToken);
            emit(DiagnosticRecognitionFrame.ForCompleted(result));
        }, token);

    Task<DiagnosticBasicInventoryRead> IModernBasicInventorySource.RecognizeBasicAsync(
        AppSettings settings, CancellationToken token) => Task.Run(() => RunNativeRead(() =>
        {
            DiagnosticBasicInventorySample? sample = null;
            var failure = RecognizeCore(token, false, basicOnly: frame =>
            {
                sample = new(frame.Basic ?? throw new InvalidOperationException("Basic read produced no sample."), frame.Diagnostics);
            });
            token.ThrowIfCancellationRequested();
            return sample is not null ? DiagnosticBasicInventoryRead.ForSample(sample) :
                DiagnosticBasicInventoryRead.ForFailure(failure ??
                    throw new InvalidOperationException("Basic read produced neither a sample nor a failure."));
        }, token), token);

    private string DiagnosticSessionKey(RecognitionDiagnostics basis, long processStarted,
        MemoryProfile profile, long profileGeneration) =>
        $"{basis.ProcessId}:{processStarted}:{profile.ProfileId}:{profile.ProfileRevision}:{profileGeneration}:" +
        $"{_catalog.SelectedDatasetFingerprint}:{(_selectedMapVersion == Map2323SourceContract.MapVersion ? Map2323SourceContract.JassSha256 : _selectedMapVersion == Map2322SourceContract.MapVersion ? Map2322SourceContract.JassSha256 : Map2320GrowthSource.DataSha256)}";

    private DiagnosticRecognitionFrame ReadBasicInventory(ReadOnlyProcessMemory memory, ProcessModule module,
        MemoryProfile profile, RecognitionDiagnostics basis, string sessionKey, CancellationToken token)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var startedTick = Stopwatch.GetTimestamp();
        var revision = checked(++_diagnosticBasicRevision);
        DiagnosticBasicInventoryObservation Unavailable(string reason) =>
            DiagnosticBasicInventoryObservation.Unavailable(_selectedMapVersion,
                _catalog.SelectedDatasetFingerprint, basis.ProcessVersion ?? "", basis.ExecutableSha256 ?? "",
                revision, startedAt, DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(startedTick), reason);
        RecognitionDiagnostics Diagnostics(string detail) => new()
        {
            Source = DiagnosticBasicInventoryObservation.SourceName, ProcessId = basis.ProcessId,
            ProcessVersion = basis.ProcessVersion ?? "", ExecutableSha256 = basis.ExecutableSha256 ?? "",
            ProfileId = profile.ProfileId, ProfileRevision = profile.ProfileRevision,
            ProfileSource = basis.ProfileSource, Detail = detail
        };
        try
        {
            token.ThrowIfCancellationRequested();
            if (_diagnosticGrowthSource?.MapVersion != _selectedMapVersion)
                _diagnosticGrowthSource = Map2320GrowthSource.LoadBundled(_selectedMapVersion);
            var moduleBase = (ulong)module.BaseAddress.ToInt64();
            var locator = Warcraft300WorldLocator.Read(memory.ReadAvailable, moduleBase, token);
            var snapshot = Warcraft300Diagnostic.ReadInventory(memory.ReadAvailable, moduleBase, locator.World, profile, token);
            if (locator != Warcraft300WorldLocator.Read(memory.ReadAvailable, moduleBase, token))
                throw new InvalidDataException("Basic diagnostic world context changed");
            token.ThrowIfCancellationRequested();
            var sample = CreateBasicInventorySample(snapshot, locator, profile, basis, sessionKey,
                revision, startedAt, DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(startedTick));
            return DiagnosticRecognitionFrame.ForBasic(sample.Observation, sample.Diagnostics);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or Win32Exception or
            OverflowException or InvalidOperationException or UnauthorizedAccessException)
        {
            return DiagnosticRecognitionFrame.ForBasic(Unavailable("Basic diagnostic native validation/read failed"),
                Diagnostics(Regex.Replace(error.Message, @"0x[0-9a-fA-F]+", "[address]")));
        }
    }

    // The caller has already validated this inventory. A completion reuses that same snapshot,
    // never its growth-enriched projection or another native read.
    internal DiagnosticBasicInventorySample CreateBasicInventorySample(Warcraft300Diagnostic.Inventory snapshot,
        Warcraft300WorldLocator.Context locator, MemoryProfile profile, RecognitionDiagnostics basis,
        string sessionKey, long revision, DateTimeOffset startedAt, DateTimeOffset completedAt, TimeSpan duration)
    {
        if (_diagnosticGrowthSource?.MapVersion != _selectedMapVersion)
            _diagnosticGrowthSource = Map2320GrowthSource.LoadBundled(_selectedMapVersion);
        var mapped = _unitMap.Map(snapshot.Rawcodes);
        var quality = Map2320DiagnosticHelperPolicy.Evaluate(_selectedMapVersion, _catalog.OfflineBundle,
            _diagnosticGrowthSource, snapshot.Rawcodes, mapped, snapshot.Owned, _unitMap.IsRecognizedCard, _catalog.MapBundle);
        var binding = DiagnosticInventoryBinding.Context(_diagnosticObservationSalt, sessionKey, locator, snapshot.CurrentView);
        var worldStamp = DiagnosticInventoryBinding.World(_diagnosticObservationSalt, snapshot);
        var observation = quality.Accepts(profile.MinimumCatalogMatchRatio, profile.RequireNonEmptyInventory)
            ? DiagnosticBasicInventoryObservation.Create(_catalog, basis.ProcessVersion ?? "", basis.ExecutableSha256 ?? "",
                binding, revision, snapshot.CurrentView.Slot, startedAt, completedAt, duration, mapped.Entries, worldStamp, binding,
                snapshot.Units.Where(unit => unit.Owner == 27 && _unitMap.IsGrowthUnit(unit.Rawcode))
                    .Select(unit => DiagnosticInventoryBinding.GrowthUnit(_diagnosticObservationSalt, snapshot.CurrentView, unit)))
            : DiagnosticBasicInventoryObservation.Unavailable(_selectedMapVersion, _catalog.SelectedDatasetFingerprint,
                basis.ProcessVersion ?? "", basis.ExecutableSha256 ?? "", revision, startedAt, completedAt, duration,
                "Basic diagnostic mapping quality rejected");
        return new(observation, new RecognitionDiagnostics
        {
            ActivityRawcodes = snapshot.Rawcodes.ToImmutableDictionary(
                pair => RawcodeCodec.Format(pair.Key), pair => pair.Value, StringComparer.Ordinal),
            Source = DiagnosticBasicInventoryObservation.SourceName, ProcessId = basis.ProcessId,
            ProcessVersion = basis.ProcessVersion ?? "", ExecutableSha256 = basis.ExecutableSha256 ?? "",
            ProfileId = profile.ProfileId, ProfileRevision = profile.ProfileRevision, ProfileSource = basis.ProfileSource,
            ObservedObjects = snapshot.Owned, ForeignObjects = snapshot.Foreign,
            MappedObjects = mapped.KnownCount + mapped.CatalogNamedCount,
            UnknownObjects = mapped.UnknownCount, UnknownRawcodes = mapped.UnknownRawcodes,
            ExcludedSourceHelperObjects = quality.ExcludedSourceHelperObjects,
            EligibleObjects = quality.EligibleObjects, EligibleMappedObjects = quality.EligibleMappedObjects,
            EligibleUnknownObjects = quality.EligibleUnknownObjects,
            Detail = observation.Availability == DiagnosticInventoryAvailability.Ready
                ? "Fresh basic CURRENT-VIEW inventory; growth and round attribution unavailable; local identity and gameplay life unproven"
                : observation.Reason
        });
    }
}
