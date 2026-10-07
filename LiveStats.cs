using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrandOverlay;

/// <summary>Validated, cohort-bound recommendation associations, not causal effects.</summary>
public sealed class LiveStats
{
    public const string BulletProfile = "bullet-guide-1";
    public string? Profile { get; private init; }
    public int TotalRecords { get; private init; }
    public int LabeledRecords { get; private init; }
    public string? MapScriptSha256 { get; private init; }
    public string? Difficulty { get; private init; }
    private readonly Dictionary<string, LiveGoalStats> _goals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _weights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, double>> _goalWeights = new(StringComparer.Ordinal);
    private static readonly Regex UnitId = new("^[A-Za-z0-9_:-]{1,80}$", RegexOptions.CultureInvariant);

    public static bool IsCohort(string hash, string difficulty) =>
        Regex.IsMatch(hash, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant) &&
        (MatchOutcomeDetector.IsKnownDifficulty(difficulty) ||
         difficulty is "easy" or "normal" or "hard" or "nightmare" or "hell" or "god" or "god-plus");

    // Legacy files can still be inspected, but cannot be installed into a cohort-bound engine.
    public static LiveStats Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path), null, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            JsonException or FormatException or InvalidOperationException or OverflowException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning("Live statistics rejected: {0}", error.GetType().Name);
            return new LiveStats();
        }
    }

    public static bool TryParse(string json, string mapScriptSha256, string difficulty, out LiveStats stats,
        string? profile = null)
    {
        stats = new LiveStats();
        if (!IsCohort(mapScriptSha256, difficulty) || profile is not (null or BulletProfile)) return false;
        try
        {
            stats = Parse(json, mapScriptSha256, difficulty, profile);
            return true;
        }
        catch (Exception error) when (error is JsonException or FormatException or
            InvalidOperationException or OverflowException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning("Live statistics rejected: {0}", error.GetType().Name);
            return false;
        }
    }

    private static LiveStats Parse(string json, string? hash, string? difficulty, string? profile = null)
    {
        if (json.Length > 2 * 1024 * 1024) throw new JsonException("Statistics size");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Fields(root, "schemaVersion", "generatedAt", "totalRecords", "labeledRecords", "goals", "weights", "goalWeights", "difficulties");
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            !DateTimeOffset.TryParse(root.GetProperty("generatedAt").GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out _)) throw new JsonException("Statistics version/date");
        var total = Count(root.GetProperty("totalRecords"));
        var labeled = Count(root.GetProperty("labeledRecords"), total);
        var stats = new LiveStats { TotalRecords = total, LabeledRecords = labeled, MapScriptSha256 = hash, Difficulty = difficulty, Profile = profile };
        long playsSum = 0, labeledSum = 0;
        foreach (var goal in Entries(root.GetProperty("goals")))
        {
            Fields(goal.Value, "plays", "labeled", "clears", "adherenceMean", "failHeavyUnits");
            var plays = Count(goal.Value.GetProperty("plays"), total);
            var labels = Count(goal.Value.GetProperty("labeled"), plays);
            var clears = Count(goal.Value.GetProperty("clears"), labels);
            var adherence = goal.Value.GetProperty("adherenceMean");
            if (adherence.ValueKind != JsonValueKind.Null) Number(adherence, 0, 1);
            var heavy = goal.Value.GetProperty("failHeavyUnits");
            if (heavy.ValueKind != JsonValueKind.Array || heavy.GetArrayLength() > 512) throw new JsonException("Heavy units");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unit in heavy.EnumerateArray())
                if (unit.GetString() is not { } id || !UnitId.IsMatch(id) || !seen.Add(id)) throw new JsonException("Heavy unit ID");
            stats._goals.Add(goal.Name, new LiveGoalStats(plays, labels, clears));
            playsSum += plays;
            labeledSum += labels;
        }
        if (playsSum != total || labeledSum != labeled) throw new JsonException("Goal counters");
        foreach (var weight in Entries(root.GetProperty("weights")))
        {
            if (labeled < 30 || stats._goals.Values.Sum(goal => (long)goal.Clears) is var clearTotal &&
                (clearTotal == 0 || clearTotal == labeled)) throw new JsonException("Weight gate");
            stats._weights.Add(weight.Name, Number(weight.Value, -.1, .1));
        }
        foreach (var goal in Entries(root.GetProperty("goalWeights")))
        {
            if (!stats._goals.TryGetValue(goal.Name, out var counters)) throw new JsonException("Unknown goal");
            var weights = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var weight in Entries(goal.Value))
            {
                if (counters.Labeled < 30 || counters.Clears == 0 || counters.Clears == counters.Labeled)
                    throw new JsonException("Goal weight gate");
                weights.Add(weight.Name, Number(weight.Value, -.1, .1));
            }
            stats._goalWeights.Add(goal.Name, weights);
        }
        long difficultySum = 0;
        var difficulties = Entries(root.GetProperty("difficulties"), false).ToList();
        foreach (var entry in difficulties)
        {
            if (!IsCohort(new string('a', 64), entry.Name) && entry.Name != "unknown") throw new JsonException("Difficulty");
            difficultySum += Count(entry.Value, total);
        }
        if (profile == BulletProfile && (stats._goals.Keys.Any(id => id != BulletGuidePolicy.GoalId) ||
            stats._weights.Count > 0)) throw new JsonException("Bullet profile goal");
        if (difficultySum != total || difficulty is not null &&
            (difficulties.Count != 1 || difficulties[0].Name != difficulty)) throw new JsonException("Difficulty cohort");
        return stats;
    }

    private static IEnumerable<JsonProperty> Entries(JsonElement element, bool unitIds = true)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("Object required");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in element.EnumerateObject())
        {
            if (seen.Count >= 512 || unitIds && !UnitId.IsMatch(entry.Name) || !seen.Add(entry.Name)) throw new JsonException("Object key");
            yield return entry;
        }
    }

    private static void Fields(JsonElement element, params string[] fields)
    {
        var entries = Entries(element).Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        if (!entries.SetEquals(fields)) throw new JsonException("Fields");
    }

    private static int Count(JsonElement element, int maximum = int.MaxValue)
    {
        if (!element.TryGetInt32(out var count) || count < 0 || count > maximum) throw new JsonException("Counter");
        return count;
    }

    private static double Number(JsonElement element, double minimum, double maximum)
    {
        var value = element.GetDouble();
        if (!double.IsFinite(value) || value < minimum || value > maximum) throw new JsonException("Number bounds");
        return value;
    }

    public bool MatchesCohort(string? hash, string? difficulty, string? profile = null) =>
        MapScriptSha256 is not null && MapScriptSha256 == hash && Difficulty == difficulty && Profile == profile;
    public bool TryGetGoal(string goalId, out LiveGoalStats stats) => _goals.TryGetValue(goalId, out stats!);
    public double WeightFor(string unitId) => _weights.GetValueOrDefault(unitId);
    // No cross-goal fallback, including when a particular unit has insufficient evidence.
    public double WeightFor(string goalId, string unitId) =>
        _goalWeights.TryGetValue(goalId, out var weights) ? weights.GetValueOrDefault(unitId) : 0;
    public static int ApplyWeight(int score, double weight) =>
        (int)Math.Round(score * (1 + weight), MidpointRounding.AwayFromZero);
}

public sealed record LiveGoalStats(int Plays, int Labeled, int Clears)
{
    public string ClearRateText => Labeled > 0 ? $"클리어율 {(int)Math.Round(100.0 * Clears / Labeled)}%" : "";
}
