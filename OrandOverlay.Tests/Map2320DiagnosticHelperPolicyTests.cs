using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2320DiagnosticHelperPolicyTests
{
    private static readonly Lazy<DataCatalog> Bundled = new(() =>
    {
        var catalog = new DataCatalog(); catalog.Load(mapVersion: "2.320"); return catalog;
    });
    private static readonly Lazy<Map2320GrowthSource> Growth = new(Map2320GrowthSource.LoadBundled);
    private static DataCatalog Catalog => Bundled.Value;
    private static uint Code(string id)
    {
        Assert.True(RawcodeCodec.TryParse(id, out var code)); return code;
    }
    private static Dictionary<uint, int> Counts(int cards = 7, int helperCopies = 1, int unknowns = 0)
    {
        var result = new Dictionary<uint, int>();
        if (cards > 0) result.Add(Code("I10h"), cards);
        if (helperCopies > 0)
            foreach (var source in Map2320DiagnosticHelperPolicy.Sources)
                result.Add(Code(source.MemoryId), helperCopies);
        if (unknowns > 0) result.Add(Code("zzzz"), unknowns);
        return result;
    }
    private static Map2320DiagnosticHelperPolicy.Quality Evaluate(Dictionary<uint, int> counts,
        string map = "2.320", Func<uint, bool>? recognized = null)
    {
        var mapper = new RawcodeUnitMap(Catalog);
        Assert.True(mapper.IsRecognizedCard(Code("I10h")));
        Assert.False(mapper.IsRecognizedCard(Code("zzzz")));
        return Map2320DiagnosticHelperPolicy.Evaluate(map, Catalog.OfflineBundle, Growth.Value,
            counts, mapper.Map(counts), counts.Values.Sum(), recognized ?? mapper.IsRecognizedCard);
    }

    [Theory]
    [InlineData("260h", "h062", "도박소1")]
    [InlineData("4C0H", "H0C4", "기록지침 primary Tg")]
    [InlineData("A70h", "h07A", "미션 shop")]
    [InlineData("A80h", "h08A", "도움소 Ag")]
    [InlineData("M50H", "H05M", "hidden 기록지침 yg")]
    [InlineData("Q60h", "h06Q", "강화소1 gg")]
    [InlineData("R60h", "h06R", "강화소2")]
    [InlineData("S60h", "h06S", "연구소")]
    [InlineData("U50h", "h05U", "연구소효과 SF")]
    public void EverySourceIsExactCaseSensitiveAndWeighted(string memoryId, string jassId, string role)
    {
        Assert.Equal(9, Map2320DiagnosticHelperPolicy.Sources.Length);
        var source = Assert.Single(Map2320DiagnosticHelperPolicy.Sources.Where(x => x.MemoryId == memoryId));
        Assert.Equal(jassId, source.JassId); Assert.Equal(role, source.Role);
        Assert.Equal(jassId, new string(memoryId.Reverse().ToArray()));
        var counts = Counts(helperCopies: 0); counts.Add(Code(memoryId), 3);
        var result = Evaluate(counts);
        Assert.Equal(3, result.ExcludedSourceHelperObjects);
        Assert.Equal(7, result.EligibleObjects); Assert.True(result.Accepts(0.6, true));
        var changedCase = new string(memoryId.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
        counts.Remove(Code(memoryId)); counts.Add(Code(changedCase), 3);
        Assert.Equal(0, Evaluate(counts).ExcludedSourceHelperObjects);
        counts.Remove(Code(changedCase)); counts.Add(Code(jassId), 3);
        Assert.Equal(0, Evaluate(counts).ExcludedSourceHelperObjects);
    }
    [Fact] public void PinsMatchActuallyLoadedClosedSources()
    {
        Assert.Equal(Map2320DiagnosticHelperPolicy.BundleFingerprint, Catalog.OfflineBundle!.Fingerprint);
        Assert.Equal(Map2320DiagnosticHelperPolicy.SourceJassSha256, Catalog.OfflineBundle.Source.JassSha256);
        Assert.Equal(Map2320DiagnosticHelperPolicy.SourceJassSha256, Map2320GrowthSource.JassSha256);
        Assert.Equal(Map2320DiagnosticHelperPolicy.GrowthSourceSha256, Map2320GrowthSource.DataSha256);
        Assert.NotNull(Growth.Value); Assert.Equal(7, Map2320DataBundle.ExpectedMembers.Length);
    }
    [Fact] public void RawSevenOfSixteenRemainsVisibleEligibleSevenOfSeven()
    {
        var result = Evaluate(Counts());
        Assert.Equal(16, result.ObservedObjects); Assert.Equal(7, result.MappedObjects);
        Assert.Equal(9, result.UnknownObjects); Assert.Equal(9, result.ExcludedSourceHelperObjects);
        Assert.Equal(7, result.EligibleObjects); Assert.Equal(7, result.EligibleMappedObjects);
        Assert.Equal(0, result.EligibleUnknownObjects); Assert.False(result.SourceConflict);
        Assert.True(result.Accepts(0.6, true));
        Assert.True((double)result.MappedObjects / result.ObservedObjects < 0.6);
    }
    [Fact] public void RepeatedHelpersUseObjectWeightsNotDistinctIds()
    {
        var result = Evaluate(Counts(helperCopies: 4, unknowns: 2));
        Assert.Equal(45, result.ObservedObjects); Assert.Equal(38, result.UnknownObjects);
        Assert.Equal(36, result.ExcludedSourceHelperObjects); Assert.Equal(9, result.EligibleObjects);
        Assert.Equal(2, result.EligibleUnknownObjects); Assert.True(result.Accepts(0.6, true));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void OnlyHelpersAndEmptyRosterNeverAccept(bool requireNonEmpty)
    {
        var helpers = Evaluate(Counts(cards: 0));
        Assert.Equal(9, helpers.ExcludedSourceHelperObjects); Assert.Equal(0, helpers.EligibleObjects);
        Assert.False(helpers.Accepts(0.6, requireNonEmpty));
        Assert.False(Evaluate(Counts(cards: 0, helperCopies: 0)).Accepts(0.6, requireNonEmpty));
    }
    [Fact] public void RealUnknownMajorityStillRejectedAtUnchangedPointSix()
    {
        var majority = Evaluate(Counts(cards: 7, unknowns: 8));
        Assert.Equal(15, majority.EligibleObjects); Assert.Equal(8, majority.EligibleUnknownObjects);
        Assert.False(majority.Accepts(0.6, true));
        Assert.False(Evaluate(Counts(cards: 5, unknowns: 5)).Accepts(0.6, true));
        Assert.True(Evaluate(Counts(cards: 6, unknowns: 4)).Accepts(0.6, true));
        Assert.False(Evaluate(Counts(cards: 6, unknowns: 4)).Accepts(0.61, true));
        Assert.False(majority.Accepts(0.4, true));
        Assert.False(majority.Accepts(double.NaN, true));
        Assert.False(majority.Accepts(double.PositiveInfinity, true));
        Assert.Equal(0.6, Map2320DiagnosticHelperPolicy.MinimumMatchRatio);
    }
    [Fact] public void MissingEitherLoadedPinFailsBeforeQualityAcceptance()
    {
        var counts = Counts(); var mapper = new RawcodeUnitMap(Catalog); var mapped = mapper.Map(counts);
        Assert.Throws<InvalidDataException>(() => Map2320DiagnosticHelperPolicy.Evaluate("2.320", null,
            Growth.Value, counts, mapped, 16, mapper.IsRecognizedCard));
        Assert.Throws<InvalidDataException>(() => Map2320DiagnosticHelperPolicy.Evaluate("2.320", Catalog.OfflineBundle,
            null, counts, mapped, 16, mapper.IsRecognizedCard));
        Assert.Throws<InvalidDataException>(() => Map2320GrowthSource.Load([0]));
    }
    [Theory]
    [InlineData("2.314")] [InlineData("2.320 ")] [InlineData("")]
    public void OtherMapSelectionNeverExcludes(string selected)
    {
        var result = Evaluate(Counts(), selected);
        Assert.Equal(0, result.ExcludedSourceHelperObjects); Assert.Equal(16, result.EligibleObjects);
        Assert.Equal(9, result.EligibleUnknownObjects); Assert.False(result.Accepts(0.6, true));
    }
    [Fact] public void EveryHelperCardConflictRejectsEvenWhenAbsent()
    {
        var mapper = new RawcodeUnitMap(Catalog);
        foreach (var source in Map2320DiagnosticHelperPolicy.Sources)
        foreach (var copies in new[] { 0, 1 })
        {
            var conflict = Code(source.MemoryId);
            var result = Evaluate(Counts(helperCopies: copies), recognized: code =>
                code == conflict || mapper.IsRecognizedCard(code));
            Assert.True(result.SourceConflict); Assert.Equal(0, result.ExcludedSourceHelperObjects);
            Assert.False(result.Accepts(0.6, true));
        }
    }
    [Fact] public void ActualKnownCardCatalogConflictNeverSilentlyDropsCard()
    {
        var catalog = new DataCatalog(); catalog.Load(mapVersion: "2.320");
        catalog.Data.Units.Add(new UnitDefinition { Id = "helper-conflict", Name = "conflict", Rawcodes = ["260h"] });
        var mapper = new RawcodeUnitMap(catalog); var counts = Counts(); var mapped = mapper.Map(counts);
        Assert.True(mapper.IsRecognizedCard(Code("260h")));
        var result = Map2320DiagnosticHelperPolicy.Evaluate("2.320", catalog.OfflineBundle,
            Growth.Value, counts, mapped, 16, mapper.IsRecognizedCard);
        Assert.True(result.SourceConflict); Assert.False(result.Accepts(0.6, true));
        Assert.Equal(0, result.ExcludedSourceHelperObjects);
        Assert.Contains(mapped.Entries, entry => entry.UnitId == "helper-conflict" && entry.Count == 1);
    }
    [Fact] public void MappingInputsAndGrowthSpecialEntriesAreNotMutated()
    {
        var counts = Counts(); var mapper = new RawcodeUnitMap(Catalog); var mapped = mapper.Map(counts);
        var unknown = mapped.UnknownRawcodes.ToArray(); var entries = mapped.Entries.ToArray();
        Assert.True(mapper.IsGrowthUnit(Code("I10h")));
        _ = Map2320DiagnosticHelperPolicy.Evaluate("2.320", Catalog.OfflineBundle, Growth.Value,
            counts, mapped, 16, mapper.IsRecognizedCard);
        Assert.Equal(7, counts[Code("I10h")]); Assert.Equal(10, counts.Count);
        Assert.Equal(unknown, mapped.UnknownRawcodes); Assert.Equal(entries, mapped.Entries);
        Assert.Equal(7, mapped.Entries.Sum(entry => entry.Count));
    }
    [Fact] public void InconsistentAccountingFailsClosed()
    {
        var counts = Counts(); var mapper = new RawcodeUnitMap(Catalog);
        Assert.Throws<InvalidDataException>(() => Map2320DiagnosticHelperPolicy.Evaluate("2.320", Catalog.OfflineBundle,
            Growth.Value, counts, mapper.Map(counts), 15, mapper.IsRecognizedCard));
    }
}
