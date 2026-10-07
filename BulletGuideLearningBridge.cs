using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrandOverlay;

/// <summary>Only explicitly isolated Guide 1 cohorts may influence guide choices.</summary>
public sealed class BulletGuideLearningBridge
{
    private readonly LiveStats _stats;
    private BulletGuideLearningBridge(LiveStats stats) => _stats = stats;

    public static BulletGuideLearningBridge? FromSnapshot(LiveStats stats, string hash, string difficulty) =>
        stats.MatchesCohort(hash, difficulty, LiveStats.BulletProfile) ? new(stats) : null;

    // Backward compatible wire extension: mode/guideNumber are absent on legacy
    // generic aggregates. They must be produced by whole-match cohort filtering,
    // never attached to an already mixed aggregate by the client.
    public static bool TryParse(string json, string mapScriptSha256, string difficulty,
        out BulletGuideLearningBridge? learning)
    {
        learning = null;
        if (json.Length > 2 * 1024 * 1024) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root ||
                root["mode"]?.GetValue<string>() != "Guide" ||
                root["guideNumber"]?.GetValue<int>() != 1) return false;
            root.Remove("mode");
            root.Remove("guideNumber");
            if (!LiveStats.TryParse(root.ToJsonString(), mapScriptSha256, difficulty, out var stats, LiveStats.BulletProfile)) return false;
            learning = new(stats);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning("Bullet guide statistics rejected: {0}", error.GetType().Name);
            return false;
        }
    }

    public bool MatchesCohort(string mapScriptSha256, string difficulty) =>
        _stats.MatchesCohort(mapScriptSha256, difficulty, LiveStats.BulletProfile);

    // Association is only a tie-break among safe candidates within a fixed stage.
    // The validated goal gate requires 30 labels and both clear/fail evidence.
    public double WeightFor(string unitId, string difficulty) =>
        _stats.Difficulty == difficulty ? _stats.WeightFor(BulletGuidePolicy.GoalId, unitId) : 0;
}
