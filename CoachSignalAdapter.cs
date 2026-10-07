using System.Collections.Immutable;

namespace OrandOverlay;

public static class CoachSignalAdapter
{
    public static ImmutableDictionary<string, long?> WithPlayerResources(
        ImmutableDictionary<string, long?> signals, PlayerResourceState? resources, bool current) =>
        current && resources is not null
            ? signals.SetItem("gold", resources.Gold).SetItem("lumber", resources.Lumber)
                .SetItem("trait-points", resources.TraitPoints)
            : signals;

    public static readonly ImmutableArray<string> Names =
        ["gold", "lumber", "trait-points", "lives", "line-count", "boss-hp",
         "boss-limit-seconds", "upgrade-level", "upgrade-cost", "reroll-cost"];

    public static ImmutableDictionary<string, long?> Read(
        NavigationStateSnapshot? snapshot, long generation, long revision, bool current)
    {
        var values = Names.ToImmutableDictionary(name => name, _ => (long?)null,
            StringComparer.Ordinal).ToBuilder();
        if (!current || snapshot is null || !snapshot.IsPlannerReady ||
            snapshot.State != RuntimeRecommendationSnapshotState.Current ||
            snapshot.MatchGeneration != generation || snapshot.RecognitionRevision != revision)
            return values.ToImmutable();
        foreach (var group in snapshot.Fields.GroupBy(field => field.Value.Name, StringComparer.Ordinal))
        {
            if (!values.ContainsKey(group.Key) || group.Count() != 1) continue;
            var field = group.Single();
            if (field.ConfidenceBp == 10000 && field.Value.IsKnown && field.Value.Value >= 0)
                values[group.Key] = field.Value.Value;
        }
        return values.ToImmutable();
    }
}
