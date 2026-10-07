using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2320UtilityBoardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);
    private static string Root
    {
        get
        {
            var supplied = Environment.GetEnvironmentVariable("UTILITY2320_SOURCE_ROOT");
            if (supplied is not null) return supplied;
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, Map2320UtilityBoard.AnalysisPath))) return dir.FullName;
            throw new InvalidOperationException("Source checkout required for evidence tests.");
        }
    }
    private static byte[] Data() => File.ReadAllBytes(Path.Combine(Root, "Data", Map2320UtilityBoard.BundledFileName));
    private static Map2320UtilityBoard Board() => Map2320UtilityBoard.Load(Data());
    private static Map2320QualifiedUnitState Unit(Map2320UtilityBoard board, string id, params (string Ability, int Level)[] abilities)
    {
        var levels = board.AbilityIds.ToImmutableDictionary(a => a, _ => (int?)0, StringComparer.Ordinal);
        foreach (var ability in abilities) levels = levels.SetItem(ability.Ability, ability.Level);
        return new(id, true, levels);
    }
    private static Map2320AbilitySnapshot Snapshot(params Map2320QualifiedUnitState[] units) => new(
        "2.320", Map2320UtilityBoard.MapSha256, Map2320UtilityBoard.JassSha256, 0, Now,
        "explicit test fixture, not a live capture", true, units.ToImmutableArray());
    private static Map2320UtilityResult Calculate(Map2320UtilityBoard board, params Map2320QualifiedUnitState[] units) => board.Calculate(Snapshot(units), Now, Budget);

    [Fact]
    public void Registry221AndEveryEvidenceRowAndSourcePinsMatch()
    {
        var board = Board();
        Assert.Equal(221, board.Rows.Length);
        Assert.Equal(199, board.AbilityIds.Length);
        Assert.Equal(new[] { 79, 101, 37, 4 }, Enumerable.Range(1, 4).Select(c => board.Rows.Count(r => r.Category == c)));
        Assert.Equal(Map2320UtilityBoard.DatasetSha256, Convert.ToHexString(SHA256.HashData(Data())));
        var jassBytes = File.ReadAllBytes(Path.Combine(Root, Map2320UtilityBoard.SourcePath));
        Assert.Equal(Map2320UtilityBoard.JassSha256, Convert.ToHexString(SHA256.HashData(jassBytes)));
        Assert.Contains(Map2320UtilityBoard.MapSha256, File.ReadAllText(Path.Combine(Root, "docs/analysis-2320/README.md")));
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, Map2320UtilityBoard.AnalysisPath)));
        var evidenceRows = evidence.RootElement.GetProperty("registry").EnumerateArray().ToArray();
        Assert.Equal(221, evidenceRows.Length);
        foreach (var row in board.Rows)
        {
            var e = evidenceRows[row.Index];
            Assert.Equal(e.GetProperty("ability").GetString(), row.Ability);
            Assert.Equal(e.GetProperty("category").GetInt32(), row.Category);
            Assert.Equal(e.GetProperty("level").GetInt32(), row.Level);
            Assert.Equal(e.GetProperty("family").GetString(), row.Family);
            Assert.Equal(e.GetProperty("value").GetDouble(), row.Value);
        }
        // Independent sequential JASS registry transcription, including reused local value.
        var lines = Encoding.UTF8.GetString(jassBytes).Split('\n');
        double value = 0;
        var index = 0;
        string ability = "", family = "";
        int category = 0, level = 0;
        foreach (var text in lines.Skip(5053).Take(6462 - 5053))
        {
            var line = text.Trim();
            if (Regex.IsMatch(line, @"^(local real|set) value=")) value = double.Parse(line.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);
            if (line.StartsWith("set vT[MT]=$")) ability = Encoding.ASCII.GetString(Convert.FromHexString(line.Split('$')[1]));
            if (line.StartsWith("set QT[MT]=$")) family = Encoding.ASCII.GetString(Convert.FromHexString(line.Split('$')[1]));
            if (line.StartsWith("set OT[MT]=")) category = int.Parse(line.Split('=')[1]);
            if (line.StartsWith("set rT[MT]=")) level = int.Parse(line.Split('=')[1]);
            if (line.StartsWith("set hT[MT]="))
            {
                var rhs = line.Split('=')[1];
                var actual = rhs == "value" ? value : double.Parse(rhs, System.Globalization.CultureInfo.InvariantCulture);
                var r = board.Rows[index++];
                Assert.Equal((r.Ability, r.Category, r.Level, r.Family, r.Value), (ability, category, level, family, actual));
            }
        }
        Assert.Equal(221, index);
    }

    [Fact]
    public void WeakerFamilyWinsAndCopiesDoNotMultiply()
    {
        var b = Board();
        var result = Calculate(b, Unit(b, "a", ("A15G", 1)), Unit(b, "b", ("A0GJ", 1)), Unit(b, "c", ("A0GJ", 1)));
        Assert.Equal(25, result.Totals!.ArmorReduction);
        Assert.Equal(20, result.Totals.ProcSlowPercentagePoints);
        Assert.DoesNotContain(result.SelectedRows, r => r.Ability == "A15G");
    }

    [Fact]
    public void LowerRegisteredLevelSuppressesHigherEvenInReverseUnitOrder()
    {
        var b = Board();
        var high = Unit(b, "high", ("A173", 2));
        var low = Unit(b, "low", ("A173", 1));
        Assert.Equal(60, Calculate(b, high).Totals!.AuraSlowPercentagePoints);
        Assert.Equal(45, Calculate(b, high, low).Totals!.AuraSlowPercentagePoints);
        Assert.Equal(45, Calculate(b, low, high).Totals!.AuraSlowPercentagePoints);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    public void BulletBoardOmissionIsNotObjectArmor(int level, double expected)
    {
        var b = Board();
        var r = Calculate(b, Unit(b, "bullet", ("A0WN", level), ("A0WV", 1)));
        Assert.Equal(expected, r.Totals!.ArmorReduction);
        Assert.Equal(20, r.Totals.AuraSlowPercentagePoints);
    }

    [Theory]
    [InlineData(1, 7)]
    [InlineData(2, 0)]
    public void LeoUsesBoardSevenNotObjectTenAndOmitsLevelTwo(int level, double expected)
    {
        var b = Board();
        Assert.Equal(expected, Calculate(b, Unit(b, "leo", ("A0II", level))).Totals!.AuraSlowPercentagePoints);
    }

    [Fact]
    public void AllFourCategoriesRemainIndependentIncludingSharedFamilyAndAbility()
    {
        var b = Board();
        var r = Calculate(b, Unit(b, "a", ("A132", 1), ("A089", 1), ("A088", 1)));
        Assert.Equal(new Map2320UtilityTotals(5, 5, 5, 30), r.Totals);
    }

    [Fact]
    public void EqualMagnitudeKeepsEarliestRegistryRowNotFirstUnit()
    {
        var b = Board();
        var pair = b.Rows.GroupBy(r => (r.Category, r.Family, r.Value)).First(g => g.Select(r => r.Ability).Distinct().Count() > 1).OrderBy(r => r.Index).Take(2).ToArray();
        var r = Calculate(b, Unit(b, "later", (pair[1].Ability, pair[1].Level)), Unit(b, "earlier", (pair[0].Ability, pair[0].Level)));
        Assert.Contains(pair[0], r.SelectedRows);
        Assert.DoesNotContain(pair[1], r.SelectedRows);
    }

    [Fact]
    public void UnknownIncompleteStaleAndWrongSourceNeverProduceZeroTotals()
    {
        var b = Board();
        var u = Unit(b, "unit");
        var s = Snapshot(u);
        var invalid = new (Map2320AbilitySnapshot? Snapshot, Map2320UtilityStatus Status)[]
        {
            (null, Map2320UtilityStatus.Unknown),
            (s with { CompleteQualifiedRoster = null }, Map2320UtilityStatus.Incomplete),
            (s with { Units = default }, Map2320UtilityStatus.Incomplete),
            (s with { Units = ImmutableArray.Create(u with { Qualified = null }) }, Map2320UtilityStatus.Incomplete),
            (s with { Units = ImmutableArray.Create(u with { AbilityLevels = u.AbilityLevels!.Remove("A0WN") }) }, Map2320UtilityStatus.Incomplete),
            (s with { Units = ImmutableArray.Create(u with { AbilityLevels = u.AbilityLevels!.SetItem("A0WN", null) }) }, Map2320UtilityStatus.Unknown),
            (s with { Units = ImmutableArray.Create(u with { AbilityLevels = u.AbilityLevels!.SetItem("A0WN", -1) }) }, Map2320UtilityStatus.Invalid),
            (s with { Units = ImmutableArray.Create(u, u) }, Map2320UtilityStatus.Invalid),
            (s with { CapturedAt = Now - Budget - TimeSpan.FromTicks(1) }, Map2320UtilityStatus.Stale),
            (s with { CapturedAt = Now.AddTicks(1) }, Map2320UtilityStatus.Invalid),
            (s with { CapturedAt = null }, Map2320UtilityStatus.Unknown),
            (s with { RuntimeSource = "" }, Map2320UtilityStatus.Unknown),
            (s with { MapVersion = "2.314" }, Map2320UtilityStatus.SourceMismatch),
            (s with { JassSha256 = "wrong" }, Map2320UtilityStatus.SourceMismatch),
            (s with { PlayerId = 4 }, Map2320UtilityStatus.Invalid)
        };
        foreach (var item in invalid)
        {
            var r = b.Calculate(item.Snapshot, Now, Budget);
            Assert.Equal(item.Status, r.Status);
            Assert.Null(r.Totals);
            Assert.Empty(r.SelectedRows);
            Assert.Equal(Map2320UtilityBoard.JassSha256, r.JassSha256);
        }
    }

    [Fact]
    public void ExplicitEmptyRosterIsZeroAndFreshnessBoundaryIsInclusive()
    {
        var b = Board();
        var r = b.Calculate(Snapshot() with { CapturedAt = Now - Budget }, Now, Budget);
        Assert.Equal(Map2320UtilityStatus.Calculated, r.Status);
        Assert.Equal(new Map2320UtilityTotals(0, 0, 0, 0), r.Totals);
        Assert.Equal(Now - Budget, r.CapturedAt);
        Assert.Equal(Now, r.EvaluatedAt);
        Assert.Contains("offline", r.Source);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.Calculate(Snapshot(), Now, TimeSpan.FromTicks(-1)));
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("registryCount", "220")]
    [InlineData("abilityCount", "198")]
    [InlineData("mapSha256", "\"wrong\"")]
    [InlineData("categoryCounts", "[78,102,37,4]")]
    public void InvalidMetadataRejected(string field, string value)
    {
        var node = JsonNode.Parse(Data())!;
        node[field] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => Map2320UtilityBoard.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Theory]
    [InlineData("index", "1")]
    [InlineData("ability", "\"bad\"")]
    [InlineData("category", "0")]
    [InlineData("level", "0")]
    [InlineData("family", "\"rawcode:B02M\"")]
    [InlineData("value", "1e999")]
    [InlineData("startLine", "5000")]
    public void InvalidRowsRejected(string field, string value)
    {
        var node = JsonNode.Parse(Data())!;
        node["rows"]![0]![field] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => Map2320UtilityBoard.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void DuplicateRowsPropertiesMissingRowsAndPlausibleValueTamperingRejected()
    {
        var node = JsonNode.Parse(Data())!;
        node["rows"]![1] = node["rows"]![0]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Map2320UtilityBoard.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
        node = JsonNode.Parse(Data())!;
        node["rows"]!.AsArray().RemoveAt(220);
        Assert.Throws<InvalidDataException>(() => Map2320UtilityBoard.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
        var text = Encoding.UTF8.GetString(Data());
        Assert.Throws<InvalidDataException>(() => Map2320UtilityBoard.Load(Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"))));
        Assert.Throws<InvalidDataException>(() => Map2320UtilityBoard.Load(Encoding.UTF8.GetBytes(text.Replace("\"value\":-35", "\"value\":-34"))));
    }
}
