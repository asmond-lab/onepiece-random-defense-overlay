using System.Collections.Immutable;
using OrandOverlay;

internal static class FullActivityFixture
{
    private static readonly string Context = new('A', 64);
    private static readonly string World = new('B', 64);
    private static readonly string Binding = new('C', 64);

    internal static IEnumerable<IAsyncEnumerable<DiagnosticRecognitionFrame>> CreateScans(DataCatalog catalog)
    {
        var rules = Map2321ActivityRules.LoadBundled();
        yield return CreateScan(catalog, rules, 1,
            new() { ["810e"] = 1, ["300h"] = 1 }, new());
        yield return CreateScan(catalog, rules, 2,
            new() { ["300h"] = 2 }, new() { ["NU"] = 1 });
        yield return CreateScan(catalog, rules, 3,
            new() { ["300h"] = 1, ["900h"] = 1 }, new() { ["NU"] = 1 });
        yield return CreateScan(catalog, rules, 4,
            new() { ["K00h"] = 1 }, new() { ["NU"] = 1 });
        yield return CreateScan(catalog, rules, 5,
            new() { ["K00h"] = 1, ["300h"] = 1, ["400h"] = 1 },
            new() { ["NU"] = 1, ["Ie"] = 1, ["tU"] = 1 });
    }

    private static async IAsyncEnumerable<DiagnosticRecognitionFrame> CreateScan(
        DataCatalog catalog,
        Map2321ActivityRules rules,
        long revision,
        Dictionary<string, int> rawcodes,
        Dictionary<string, int> nonzeroCounters)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = rawcodes
            .Select(pair => (
                UnitId: catalog.AllUnits.First(unit =>
                    unit.Rawcodes.Contains(pair.Key, StringComparer.Ordinal)).Id,
                pair.Value))
            .GroupBy(pair => pair.UnitId, StringComparer.Ordinal)
            .Select(group => new InventoryEntry
            {
                UnitId = group.Key,
                Count = group.Sum(pair => pair.Value)
            })
            .ToArray();
        var basicDiagnostics = new RecognitionDiagnostics
        {
            Source = DiagnosticBasicInventoryObservation.SourceName,
            ProcessVersion = Warcraft300Diagnostic.Version,
            ExecutableSha256 = Warcraft300Diagnostic.Hash
        };
        var basic = DiagnosticBasicInventoryObservation.Create(
            catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            Context, revision, 0, now.AddMilliseconds(-10), now,
            TimeSpan.FromMilliseconds(10), entries, World, Binding);
        var full = DiagnosticInventoryObservation.Create(
            catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            Context, revision, 0, now.AddMilliseconds(-20), now,
            TimeSpan.FromMilliseconds(20), entries, [],
            observedRound: 12, worldStampFingerprint: World, bindingContextId: Binding);
        var projected = rawcodes.ToImmutableDictionary(StringComparer.Ordinal);
        var counters = rules.IntegerArrays.ToImmutableDictionary(
            name => name, name => nonzeroCounters.GetValueOrDefault(name), StringComparer.Ordinal);
        var fullDiagnostics = new RecognitionDiagnostics
        {
            Source = DiagnosticInventoryObservation.SourceName,
            ProcessVersion = Warcraft300Diagnostic.Version,
            ExecutableSha256 = Warcraft300Diagnostic.Hash,
            ActivityProjectedRawcodes = projected,
            ActivityCounters = counters,
            ActivityCounterStatus = "ready"
        };
        var result = new RecognitionResult
        {
            State = RecognitionState.Ready,
            Diagnostics = fullDiagnostics,
            DiagnosticObservation = full,
            CompletionBasicSample = new(basic, basicDiagnostics)
        };
        await Task.CompletedTask;
        yield return DiagnosticRecognitionFrame.ForCompleted(result);
    }
}
