using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>
/// Offline reproduction of NEW 2.320 HQb's four utility-board sums, not combat
/// damage, effective debuffs, proc uptime, or the currently displayed cached board.
/// No inventory, unit-name, trait, spellbook or ability-level inference is performed.
/// </summary>
public sealed class Map2320UtilityBoard
{
    public const string MapVersion = "2.320";
    public const string MapSha256 = "68445631FDACA12A343E9465E5BC5DC9B4C8C6CC927D701F52E2CB822821E15F";
    public const string JassSha256 = "6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C";
    public const string DatasetSha256 = "18664391C8389E00BE5E1F8E6D28DFDF3686CCE8DF6AD66C1B6BFF720D8A5C0B";
    public const string BundledFileName = "utility-board-2320.json";
    public const string SourcePath = "docs/analysis-2320/modern-reader/members-ko-2.320/war3map.j";
    public const string AnalysisPath = "docs/analysis-2320/utility-board-formulas.json";
    public const string SourceLabel = "NEW 2.320 UtilityObjectData__Init 5053-6463; HQb 15810-15973; offline board model only";

    private Map2320UtilityBoard(ImmutableArray<Map2320UtilityRow> rows)
    {
        Rows = rows;
        AbilityIds = rows.Select(r => r.Ability).Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal).ToImmutableArray();
    }

    public ImmutableArray<Map2320UtilityRow> Rows { get; }
    public ImmutableArray<string> AbilityIds { get; }

    // Missing/invalid data is an explicit error, never an empty or fallback profile.
    public static Map2320UtilityBoard LoadBundled() =>
        LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Data", BundledFileName));

    public static Map2320UtilityBoard LoadFromFile(string path) => Load(File.ReadAllBytes(path));

    /// <summary>Validates schema and exact bundled bytes. No unpinned override path.</summary>
    public static Map2320UtilityBoard Load(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        try
        {
            using var doc = JsonDocument.Parse(utf8);
            var root = doc.RootElement;
            Properties(root, "schemaVersion", "mapVersion", "mapSha256", "jassSha256", "registryCount", "abilityCount", "categoryCounts", "source", "analysis", "registryLines", "aggregationLines", "rows");
            Require(Int(root, "schemaVersion") == 1 && Text(root, "mapVersion") == MapVersion, "Unsupported schema/map version.");
            Require(Text(root, "mapSha256") == MapSha256 && Text(root, "jassSha256") == JassSha256, "Source pin mismatch.");
            Require(Text(root, "source") == SourcePath && Text(root, "analysis") == AnalysisPath, "Source path mismatch.");
            Require(Int(root, "registryCount") == 221 && Int(root, "abilityCount") == 199, "Declared count mismatch.");
            Require(Numbers(root, "categoryCounts").SequenceEqual(new[] { 79, 101, 37, 4 }), "Category counts mismatch.");
            Require(Numbers(root, "registryLines").SequenceEqual(new[] { 5053, 6463 }) && Numbers(root, "aggregationLines").SequenceEqual(new[] { 15810, 15973 }), "Source lines mismatch.");
            var rows = ImmutableArray.CreateBuilder<Map2320UtilityRow>();
            var seen = new HashSet<(int, string, int)>();
            int previousEnd = 5053, previousCategory = 1;
            foreach (var item in root.GetProperty("rows").EnumerateArray())
            {
                Properties(item, "index", "ability", "category", "level", "family", "value", "startLine", "endLine");
                var row = new Map2320UtilityRow(Int(item, "index"), Text(item, "ability"), Int(item, "category"), Int(item, "level"), Text(item, "family"), item.GetProperty("value").GetDouble(), Int(item, "startLine"), Int(item, "endLine"));
                Require(row.Index == rows.Count && rows.Count < 221, "Registry order/count mismatch.");
                Require(Rawcode(row.Ability) && Rawcode(row.Family), "Invalid native rawcode.");
                Require(row.Category >= previousCategory && row.Category <= 4 && row.Level >= 1 && row.Level <= 4, "Invalid category/registered level.");
                Require(double.IsFinite(row.Value), "Non-finite registry value.");
                Require(row.StartLine > previousEnd && row.EndLine >= row.StartLine && row.EndLine < 6463, "Invalid source line order.");
                Require(seen.Add((row.Category, row.Ability, row.Level)), "Duplicate category/ability/level.");
                rows.Add(row);
                previousEnd = row.EndLine;
                previousCategory = row.Category;
            }
            Require(rows.Count == 221 && rows.Select(r => r.Ability).Distinct(StringComparer.Ordinal).Count() == 199, "Registry count mismatch.");
            Require(Enumerable.Range(1, 4).Select(c => rows.Count(r => r.Category == c)).SequenceEqual(new[] { 79, 101, 37, 4 }), "Actual category counts mismatch.");
            Require(Convert.ToHexString(SHA256.HashData(utf8)) == DatasetSha256, "Dataset byte hash mismatch.");
            return new Map2320UtilityBoard(rows.ToImmutable());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        {
            throw new InvalidDataException("Invalid NEW 2.320 utility-board dataset.", ex);
        }
    }

    /// <summary>
    /// The caller must explicitly attest a COMPLETE roster of qualified owned units:
    /// owner Player(p), life > .405, and (SF[p] identity OR h08L OR giant not in sx/nx).
    /// sx: h003,h002,h001,h007,h004,h008,h005,h009,h006.
    /// nx: h00M,h00A,h00O,h00C,h00D,h00I,h00N,h00E,h00G,h00J,h00K,h00L,h00F.
    /// SF/h08L exceptions precede exclusion checks. This pure module does not verify
    /// live eligibility. Each qualified unit must explicitly supply all 199 ability
    /// levels, including 0 for observed absence. Missing/null is NEVER absence.
    /// CapturedAt describes the coherent runtime observation, not manual board refresh.
    /// Freshness is evaluated with caller-supplied now and maxAge, never a hidden clock.
    /// </summary>
    public Map2320UtilityResult Calculate(Map2320AbilitySnapshot? snapshot, DateTimeOffset now, TimeSpan maxAge)
    {
        if (maxAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        Map2320UtilityResult Reject(Map2320UtilityStatus status, string reason) => new(
            status, reason, SourceLabel, MapSha256, JassSha256, DatasetSha256,
            snapshot?.RuntimeSource, snapshot?.CapturedAt, now, maxAge, null, ImmutableArray<Map2320UtilityRow>.Empty);
        if (snapshot is null) return Reject(Map2320UtilityStatus.Unknown, "No runtime snapshot.");
        if (snapshot.MapVersion != MapVersion || snapshot.MapSha256 != MapSha256 || snapshot.JassSha256 != JassSha256)
            return Reject(Map2320UtilityStatus.SourceMismatch, "Runtime map/JASS pins do not match.");
        if (snapshot.CapturedAt is null || string.IsNullOrWhiteSpace(snapshot.RuntimeSource))
            return Reject(Map2320UtilityStatus.Unknown, "Runtime provenance/time unknown.");
        if (snapshot.CapturedAt > now) return Reject(Map2320UtilityStatus.Invalid, "Snapshot is from the future.");
        if (now - snapshot.CapturedAt.Value > maxAge) return Reject(Map2320UtilityStatus.Stale, "Snapshot exceeds caller freshness budget.");
        if (snapshot.PlayerId is < 0 or > 3) return Reject(Map2320UtilityStatus.Invalid, "Board player must be 0..3.");
        if (snapshot.CompleteQualifiedRoster != true || snapshot.Units.IsDefault)
            return Reject(Map2320UtilityStatus.Incomplete, "Complete qualified roster not attested.");
        var unitIds = new HashSet<string>(StringComparer.Ordinal);
        var present = new HashSet<(string, int)>();
        foreach (var unit in snapshot.Units)
        {
            if (unit is null || unit.Qualified != true)
                return Reject(Map2320UtilityStatus.Incomplete, "Unit eligibility not explicitly qualified.");
            if (string.IsNullOrWhiteSpace(unit.UnitIdentity) || !unitIds.Add(unit.UnitIdentity))
                return Reject(Map2320UtilityStatus.Invalid, "Missing/duplicate runtime unit identity.");
            if (unit.AbilityLevels is null || unit.AbilityLevels.Count != AbilityIds.Length || AbilityIds.Any(id => !unit.AbilityLevels.ContainsKey(id)))
                return Reject(Map2320UtilityStatus.Incomplete, "Every registered ability requires an explicit level per unit.");
            foreach (var pair in unit.AbilityLevels)
            {
                if (pair.Value is null) return Reject(Map2320UtilityStatus.Unknown, "Ability level is unknown.");
                if (pair.Value < 0) return Reject(Map2320UtilityStatus.Invalid, "Ability levels cannot be negative.");
                present.Add((pair.Key, pair.Value.Value));
            }
        }
        var minimumLevels = new Dictionary<(int, string), int>();
        foreach (var row in Rows)
        {
            if (!present.Contains((row.Ability, row.Level))) continue;
            var key = (row.Category, row.Ability);
            if (!minimumLevels.TryGetValue(key, out int level) || row.Level < level) minimumLevels[key] = row.Level;
        }
        var families = new Dictionary<(int, string), Map2320UtilityRow>();
        foreach (var row in Rows)
        {
            if (!present.Contains((row.Ability, row.Level)) || !minimumLevels.TryGetValue((row.Category, row.Ability), out int level) || row.Level != level) continue;
            var key = (row.Category, row.Family);
            // Strict less-than is deliberate: equal magnitude retains earliest index.
            if (!families.TryGetValue(key, out var old) || Math.Abs(row.Value) < Math.Abs(old.Value)) families[key] = row;
        }
        var selected = families.Values.OrderBy(r => r.Index).ToImmutableArray();
        var sums = new double[4];
        foreach (var row in selected) sums[row.Category - 1] += (row.Category is 1 or 4 ? -1 : 1) * row.Value;
        return new(Map2320UtilityStatus.Calculated, "Fresh qualified snapshot; offline potential board contributions, not actual damage or active procs.", SourceLabel,
            MapSha256, JassSha256, DatasetSha256, snapshot.RuntimeSource, snapshot.CapturedAt, now, maxAge,
            new(sums[0], sums[1], sums[2], sums[3]), selected);
    }

    private static bool Rawcode(string value) => value.Length == 4 && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z');
    private static string Text(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException("Null " + name);
    private static int Int(JsonElement item, string name) => item.GetProperty(name).GetInt32();
    private static int[] Numbers(JsonElement item, string name) => item.GetProperty(name).EnumerateArray().Select(v => v.GetInt32()).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Properties(JsonElement item, params string[] expected)
    {
        var names = item.EnumerateObject().Select(p => p.Name).ToArray();
        Require(names.Length == expected.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length && expected.All(n => names.Contains(n, StringComparer.Ordinal)), "Missing, duplicate or unknown JSON property.");
    }
}

public sealed record Map2320UtilityRow(int Index, string Ability, int Category, int Level, string Family, double Value, int StartLine, int EndLine);
public sealed record Map2320QualifiedUnitState(string UnitIdentity, bool? Qualified, ImmutableDictionary<string, int?>? AbilityLevels);
public sealed record Map2320AbilitySnapshot(string MapVersion, string MapSha256, string JassSha256, int PlayerId,
    DateTimeOffset? CapturedAt, string? RuntimeSource, bool? CompleteQualifiedRoster, ImmutableArray<Map2320QualifiedUnitState> Units);
public enum Map2320UtilityStatus { Calculated, Unknown, Incomplete, Stale, SourceMismatch, Invalid }
// Deliberately no cross-category TotalSlow or TotalArmorReduction property.
public sealed record Map2320UtilityTotals(double ArmorReduction, double AuraSlowPercentagePoints, double ProcSlowPercentagePoints, double ProcArmorReduction);
public sealed record Map2320UtilityResult(Map2320UtilityStatus Status, string StatusReason, string Source,
    string MapSha256, string JassSha256, string DatasetSha256, string? RuntimeSource, DateTimeOffset? CapturedAt,
    DateTimeOffset EvaluatedAt, TimeSpan MaximumAge, Map2320UtilityTotals? Totals, ImmutableArray<Map2320UtilityRow> SelectedRows);
