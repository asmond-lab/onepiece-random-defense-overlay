using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>Offline 2.322 board projection; not a live memory reader or measured enemy debuff.</summary>
public sealed class Map2322UtilityBoard
{
    public const string BundledFileName = "utility-board-2322.json";
    public const string DatasetSha256 = "b370b62b81e0a04ab6d8c4853d29ffe8478cc18c0a6047d204b9c62cf543f4dc";
    public const string SourcePath = "artifacts/ordr-2322/map-extracted/war3map.j";
    public ImmutableArray<Map2322BoardRow> Rows { get; }
    public ImmutableArray<string> AbilityIds { get; }

    private Map2322UtilityBoard(ImmutableArray<Map2322BoardRow> rows)
    {
        Rows = rows;
        AbilityIds = rows.Select(r => r.Ability).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray();
    }

    public static Map2322UtilityBoard LoadBundled() => Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", BundledFileName)));
    public static Map2322UtilityBoard Load(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        if (!Convert.ToHexString(SHA256.HashData(utf8)).Equals(DatasetSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unapproved 2.322 utility registry bytes.");
        try
        {
            using var document = JsonDocument.Parse(utf8);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("mapVersion").GetString() != Map2322SourceContract.MapVersion ||
                root.GetProperty("mapSha256").GetString() != Map2322SourceContract.ArchiveSha256 ||
                root.GetProperty("jassSha256").GetString() != Map2322SourceContract.JassSha256 ||
                root.GetProperty("source").GetString() != SourcePath || root.GetProperty("registryCount").GetInt32() != 246 ||
                root.GetProperty("abilityCount").GetInt32() != 198)
                throw new InvalidDataException("Wrong 2.322 source or registry counts.");
            var rows = root.GetProperty("rows").EnumerateArray().Select(item => new Map2322BoardRow(
                item.GetProperty("index").GetInt32(), item.GetProperty("ability").GetString()!, item.GetProperty("category").GetInt32(),
                item.GetProperty("level").GetInt32(), item.GetProperty("family").GetString()!, item.GetProperty("value").GetDouble(),
                item.GetProperty("startLine").GetInt32(), item.GetProperty("endLine").GetInt32())).ToImmutableArray();
            if (rows.Length != 246 || rows.Select(r => r.Ability).Distinct(StringComparer.Ordinal).Count() != 198 ||
                !rows.Select((r, i) => r.Index == i && r.Category is >= 1 and <= 4 && r.Level is >= 1 and <= 11 &&
                    r.Ability.Length == 4 && r.Family.Length == 4 && double.IsFinite(r.Value) && r.StartLine is >= 84226 and <= 84476).All(x => x) ||
                !Enumerable.Range(1, 4).Select(c => rows.Count(r => r.Category == c)).SequenceEqual(new[] { 91, 112, 36, 7 }))
                throw new InvalidDataException("Incomplete 2.322 utility registry.");
            return new(rows);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Invalid 2.322 utility registry.", e);
        }
    }

    /// <summary>Caller attests the full qualified own roster and all four owner-presence slots from one coherent capture.</summary>
    public Map2322BoardResult Calculate(Map2322BoardSnapshot? snapshot, DateTimeOffset now, TimeSpan maxAge)
    {
        if (maxAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        Map2322BoardResult Reject(Map2322BoardStatus status, string reason) => new(status, reason, snapshot?.RuntimeSource,
            null, null, ImmutableArray<Map2322BoardRow>.Empty, ImmutableArray<int>.Empty, ImmutableArray<int>.Empty);
        if (snapshot is null || snapshot.RuntimeSource is null || snapshot.CapturedAt is null) return Reject(Map2322BoardStatus.Unknown, "No verified capture provenance.");
        if (snapshot.MapVersion != Map2322SourceContract.MapVersion || snapshot.MapSha256 != Map2322SourceContract.ArchiveSha256 ||
            snapshot.JassSha256 != Map2322SourceContract.JassSha256) return Reject(Map2322BoardStatus.SourceMismatch, "Runtime source identity differs.");
        if (string.IsNullOrWhiteSpace(snapshot.RuntimeSource)) return Reject(Map2322BoardStatus.Unknown, "Runtime capture source is unknown.");
        if (snapshot.CapturedAt > now) return Reject(Map2322BoardStatus.Invalid, "Future capture.");
        if (now - snapshot.CapturedAt.Value > maxAge) return Reject(Map2322BoardStatus.Stale, "Capture is stale.");
        if (snapshot.PlayerId is < 0 or > 3) return Reject(Map2322BoardStatus.Invalid, "Board player outside slots 0..3.");
        if (snapshot.CompleteQualifiedOwnRoster != true || snapshot.OwnUnits.IsDefault || snapshot.CompleteSharedOwners != true ||
            snapshot.Owners.IsDefault || snapshot.Owners.Length != 4) return Reject(Map2322BoardStatus.Incomplete, "Roster or four owner slots incomplete.");
        var brook = ImmutableArray.CreateBuilder<int>();
        var usopp = ImmutableArray.CreateBuilder<int>();
        for (var i = 0; i < 4; i++)
        {
            var owner = snapshot.Owners[i];
            if (owner is null || owner.Slot != i) return Reject(Map2322BoardStatus.Invalid, "Owner slots must be distinct and ordered 0..3.");
            if (owner.Active is null || owner.BrookOwner is null || owner.UsoppOwner is null)
                return Reject(Map2322BoardStatus.Unknown, "Owner presence is not verified.");
            if (owner.Active == false && (owner.BrookOwner == true || owner.UsoppOwner == true))
                return Reject(Map2322BoardStatus.Invalid, "Departed owner still has presence flag.");
            if (owner.BrookOwner == true) brook.Add(i);
            if (owner.UsoppOwner == true) usopp.Add(i);
        }
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var eligible = new HashSet<int>();
        foreach (var unit in snapshot.OwnUnits)
        {
            if (unit is null || unit.Qualified != true) return Reject(Map2322BoardStatus.Incomplete, "Own unit qualification unknown.");
            if (string.IsNullOrWhiteSpace(unit.Identity) || !identities.Add(unit.Identity) || unit.TypeId.Length != 4)
                return Reject(Map2322BoardStatus.Invalid, "Invalid own unit identity/type.");
            if (unit.AbilityLevels is null || unit.AbilityLevels.Count != AbilityIds.Length || AbilityIds.Any(a => !unit.AbilityLevels.ContainsKey(a)))
                return Reject(Map2322BoardStatus.Incomplete, "Own ability observation incomplete.");
            foreach (var entry in unit.AbilityLevels)
            {
                if (entry.Value is null) return Reject(Map2322BoardStatus.Unknown, "Unknown ability level.");
                if (entry.Value < 0) return Reject(Map2322BoardStatus.Invalid, "Negative ability level.");
            }
            // pjass grammar.y:85-88 gives OR higher precedence than AND:
            // (Pf==0 OR typeMatches) AND abilityMatches, not unconditional Pf==0.
            foreach (var row in Rows)
                if ((row.Ability != "A05U" || unit.TypeId == "h0AH") && unit.AbilityLevels[row.Ability] == row.Level)
                    eligible.Add(row.Index);
        }
        var candidates = Rows.Where(row => eligible.Contains(row.Index)).ToArray();
        var minimum = candidates.GroupBy(r => (r.Category, r.Ability)).ToDictionary(g => g.Key, g => g.Min(r => r.Level));
        var selected = new Dictionary<(int Category, string Family), Map2322BoardRow>();
        foreach (var row in candidates)
        {
            if (row.Level != minimum[(row.Category, row.Ability)]) continue;
            var key = (row.Category, row.Family);
            if (!selected.TryGetValue(key, out var old) ||
                (row.Category == 4 && row.Family == "B00E" ? Math.Abs(row.Value) > Math.Abs(old.Value) : Math.Abs(row.Value) < Math.Abs(old.Value)))
                selected[key] = row;
        }
        var own = new double[4];
        foreach (var row in selected.Values) own[row.Category - 1] += row.Category is 1 or 4 ? -row.Value : row.Value;
        var ownTotals = new Map2322BoardTotals(own[0], own[1], own[2], own[3]);
        var display = ownTotals with { ArmorReduction = own[0] + (usopp.Count > 0 ? 8 : 0), AuraSlowPercentagePoints = own[1] + (brook.Count > 0 ? 5 : 0) };
        return new(Map2322BoardStatus.Calculated, "Offline board projection; shared presence is display-only, not an own unit or measured aura.",
            snapshot.RuntimeSource, ownTotals, display, selected.Values.OrderBy(r => r.Index).ToImmutableArray(), brook.ToImmutable(), usopp.ToImmutable());
    }
}

public sealed record Map2322BoardRow(int Index, string Ability, int Category, int Level, string Family, double Value, int StartLine, int EndLine);
public sealed record Map2322OwnUnit(string Identity, string TypeId, bool? Qualified, ImmutableDictionary<string, int?>? AbilityLevels);
public sealed record Map2322SharedOwner(int Slot, bool? Active, bool? BrookOwner, bool? UsoppOwner);
public sealed record Map2322BoardSnapshot(string MapVersion, string MapSha256, string JassSha256, int PlayerId,
    DateTimeOffset? CapturedAt, string? RuntimeSource, bool? CompleteQualifiedOwnRoster, ImmutableArray<Map2322OwnUnit> OwnUnits,
    bool? CompleteSharedOwners, ImmutableArray<Map2322SharedOwner> Owners);
public enum Map2322BoardStatus { Calculated, Unknown, Incomplete, Stale, SourceMismatch, Invalid }
public sealed record Map2322BoardTotals(double ArmorReduction, double AuraSlowPercentagePoints, double ProcSlowPercentagePoints, double ProcArmorReduction);
public sealed record Map2322BoardResult(Map2322BoardStatus Status, string Reason, string? RuntimeSource, Map2322BoardTotals? OwnTotals,
    Map2322BoardTotals? DisplayTotals, ImmutableArray<Map2322BoardRow> SelectedOwnRows, ImmutableArray<int> BrookOwnerSlots,
    ImmutableArray<int> UsoppOwnerSlots);
