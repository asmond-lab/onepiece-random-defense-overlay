namespace OrandOverlay;

/// <summary>Offline source approval is independent of engine/layout and session approval.</summary>
public static class MapDatasetRuntimePolicy
{
    public static bool AllowsLegacyRuntime(string mapVersion) => mapVersion == "2.314";
    public static bool AllowsReader(string mapVersion, bool experimentalProfile, bool explicitVerification) =>
        AllowsLegacyRuntime(mapVersion) || mapVersion == "2.320" && experimentalProfile && explicitVerification;
    internal static bool AllowsObservedArchive(string version, RuntimeMapIdentityResult? observation) =>
        version switch
        {
            "2.322" => observation is { State: RuntimeMapIdentityState.Proven,
                ArchiveLengthBytes: Map2322SourceContract.ArchiveLengthBytes,
                ActualArchiveSha256: Map2322SourceContract.ArchiveSha256 },
            "2.323" => observation is { State: RuntimeMapIdentityState.Proven,
                ArchiveLengthBytes: Map2323SourceContract.ArchiveLengthBytes,
                ActualArchiveSha256: Map2323SourceContract.ArchiveSha256 },
            _ => true
        };
    // Separate bounded presentation lane. This does not enable/verify a production profile.
    internal static bool AllowsReferenceObservation(string mapVersion, string? datasetFingerprint,
        MemoryProfile profile, string executableVersion, string executableHash) =>
        (Map2320DataBundle.IsCompatible(mapVersion) && datasetFingerprint == DiagnosticInventoryObservation.PinnedDatasetFingerprint ||
         mapVersion == Map2322SourceContract.MapVersion && datasetFingerprint == DiagnosticInventoryObservation.Pinned2322DatasetFingerprint ||
         mapVersion == Map2323SourceContract.MapVersion && datasetFingerprint == DiagnosticInventoryObservation.Pinned2323DatasetFingerprint) &&
        profile.KnownExperimental && profile.FileVersion == Warcraft300Diagnostic.Version &&
        executableVersion == Warcraft300Diagnostic.Version &&
        string.Equals(profile.ExecutableSha256, Warcraft300Diagnostic.Hash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(executableHash, Warcraft300Diagnostic.Hash, StringComparison.OrdinalIgnoreCase);
    public const string OfflineOnlyMessage = "2.320 오프라인 데이터만 검증됨 · 실시간 인식·플레이 기록 보류";
}
