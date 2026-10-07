using System.Collections.Immutable;

namespace OrandOverlay;

// Diagnostic quality accounting only. Source roles do not prove alive, local, or exhaustive inventory.
internal static class Map2320DiagnosticHelperPolicy
{
    internal const string BundleFingerprint = "0638397cc88df886197f9fafbb6d8f292227a1863fa71cf23e4dd48fb1bc76cf";
    internal const string SourceJassSha256 = "6fdfc64bf8ad9463f5b5c8a351ffa7e1875d6cf9b51210129539375fa77a6c7c";
    internal const string GrowthSourceSha256 = "08D09CA1386CFD57E5935BBFA9C2E595644B410AE61F7054089357FAA0904909";
    internal const string SourceArchiveSha256 = "68445631fdaca12a343e9465e5bc5dc9b4c8c6cc927d701f52e2cb822821e15f";
    internal const double MinimumMatchRatio = 0.6;

    // Exact memory spelling, not JASS byte order; never case-fold or classify arbitrary unmapped IDs.
    // Startup CreateUnit evidence: pinned war3map.j lines 56126-56141. See provenance document.
    internal static ImmutableArray<HelperSource> Sources { get; } =
    [
        new("260h", "h062", "도박소1"),
        new("4C0H", "H0C4", "기록지침 primary Tg"),
        new("A70h", "h07A", "미션 shop"),
        new("A80h", "h08A", "도움소 Ag"),
        new("M50H", "H05M", "hidden 기록지침 yg"),
        new("Q60h", "h06Q", "강화소1 gg"),
        new("R60h", "h06R", "강화소2"),
        new("S60h", "h06S", "연구소"),
        new("U50h", "h05U", "연구소효과 SF")
    ];
    internal sealed record HelperSource(string MemoryId, string JassId, string Role);
    private static readonly ImmutableHashSet<uint> HelperIds = Sources.Select(source =>
    {
        if (!RawcodeCodec.TryParse(source.MemoryId, out var rawcode))
            throw new InvalidDataException("Invalid compiled helper ID.");
        return rawcode;
    }).ToImmutableHashSet();

    internal sealed record Quality(int ObservedObjects, int MappedObjects, int UnknownObjects,
        int ExcludedSourceHelperObjects, int EligibleObjects, int EligibleMappedObjects,
        int EligibleUnknownObjects, bool SourceConflict)
    {
        internal bool Accepts(double minimumRatio, bool requireNonEmptyInventory) =>
            !SourceConflict && double.IsFinite(minimumRatio) && minimumRatio >= MinimumMatchRatio &&
            minimumRatio <= 1 && EligibleMappedObjects > 0 &&
            (!requireNonEmptyInventory || EligibleObjects > 0) &&
            (double)EligibleMappedObjects / Math.Max(1, EligibleObjects) >= minimumRatio;
    }

    internal static Quality Evaluate(string selectedMapVersion, Map2320DataBundle? bundle,
        Map2320GrowthSource? growthSource, IReadOnlyDictionary<uint, int> rawcodes,
        RawcodeMappingResult mapped, int attributedObjects, Func<uint, bool> isRecognizedCard,
        Map2322DataBundle? bundle2322 = null)
    {
        ArgumentNullException.ThrowIfNull(rawcodes);
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(isRecognizedCard);
        var enabled = Map2320DataBundle.IsCompatible(selectedMapVersion);
        if (enabled) ValidatePins(bundle, growthSource, selectedMapVersion); // Before any exclusion or acceptance.
        if ((selectedMapVersion is "2.322" or "2.323") &&
            (bundle2322 is null || bundle2322.MapVersion != selectedMapVersion || growthSource?.MapVersion != selectedMapVersion ||
             bundle2322.Fingerprint.Length != 64))
            throw new InvalidDataException("2.322 diagnostic helper source pins missing.");
        var observed = 0;
        foreach (var count in rawcodes.Values)
        {
            if (count <= 0) throw new InvalidDataException("Nonpositive diagnostic object count.");
            observed = checked(observed + count);
        }
        var matches = checked(mapped.KnownCount + mapped.CatalogNamedCount);
        if (matches < 0 || mapped.UnknownCount < 0 || observed != attributedObjects ||
            observed != checked(matches + mapped.UnknownCount))
            throw new InvalidDataException("Diagnostic quality accounting mismatch.");
        // Check the entire allowlist, including absent IDs: future catalog changes require new review.
        var conflict = enabled && HelperIds.Any(isRecognizedCard);
        var excluded = 0;
        if (enabled && !conflict)
            foreach (var pair in rawcodes)
                if (HelperIds.Contains(pair.Key)) excluded = checked(excluded + pair.Value);
        if (excluded > mapped.UnknownCount)
            throw new InvalidDataException("Diagnostic helper mapping conflict.");
        return new(observed, matches, mapped.UnknownCount, excluded, observed - excluded,
            matches, mapped.UnknownCount - excluded, conflict);
    }

    private static void ValidatePins(Map2320DataBundle? bundle, Map2320GrowthSource? growthSource, string selectedMapVersion)
    {
        // These types can only be constructed through their existing byte/hash-checked loaders.
        // This is offline source provenance, not verification of the running script's bytes.
        if (bundle is null || growthSource is null || growthSource.MapVersion != selectedMapVersion || bundle.Fingerprint != BundleFingerprint ||
            bundle.Source.MapVersion != "2.320" || bundle.Source.Archive.Sha256 != SourceArchiveSha256 ||
            !bundle.Source.Members.Any(member => member.Name == "war3map.j" &&
                member.LengthBytes == 3014709 && member.Sha256 == SourceJassSha256) ||
            Map2320GrowthSource.JassSha256 != SourceJassSha256 ||
            Map2320GrowthSource.DataSha256 != GrowthSourceSha256)
            throw new InvalidDataException("Diagnostic helper source pins missing or mismatched.");
    }
}
