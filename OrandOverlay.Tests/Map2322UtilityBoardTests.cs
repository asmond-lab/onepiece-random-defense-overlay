using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322UtilityBoardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);
    private static string Root
    {
        get
        {
            var supplied = Environment.GetEnvironmentVariable("UTILITY2322_SOURCE_ROOT");
            if (supplied is not null) return supplied;
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, Map2322UtilityBoard.SourcePath))) return dir.FullName;
            throw new InvalidOperationException("2.322 source checkout required.");
        }
    }
    private static byte[] Data() => File.ReadAllBytes(Path.Combine(Root, "Data", Map2322UtilityBoard.BundledFileName));
    private static Map2322UtilityBoard Board() => Map2322UtilityBoard.Load(Data());
    private static Map2322OwnUnit Unit(Map2322UtilityBoard b, string identity, params (string Id, int Level)[] abilities)
    {
        var levels = b.AbilityIds.ToImmutableDictionary(id => id, _ => (int?)0, StringComparer.Ordinal);
        foreach (var (id, level) in abilities) levels = levels.SetItem(id, level);
        return new(identity, "h05A", true, levels);
    }
    private static Map2322BoardSnapshot Snapshot(Map2322OwnUnit[] units, params Map2322SharedOwner[] owners) => new(
        Map2322SourceContract.MapVersion, Map2322SourceContract.ArchiveSha256, Map2322SourceContract.JassSha256, 0,
        Now, "verified coherent fixture", true, units.ToImmutableArray(), true,
        (owners.Length == 0 ? Enumerable.Range(0, 4).Select(i => new Map2322SharedOwner(i, true, false, false)) : owners).ToImmutableArray());
    private static Map2322BoardResult Calc(Map2322UtilityBoard b, Map2322BoardSnapshot s) => b.Calculate(s, Now, Budget);

    [Fact]
    public void CompleteRegistryPinnedAndLoopsAreIndependentlyCounted()
    {
        var b = Board();
        Assert.Equal(246, b.Rows.Length);
        Assert.Equal(198, b.AbilityIds.Length);
        Assert.Equal(new[] { 91, 112, 36, 7 }, Enumerable.Range(1, 4).Select(c => b.Rows.Count(r => r.Category == c)));
        Assert.Equal(Map2322UtilityBoard.DatasetSha256, Convert.ToHexString(SHA256.HashData(Data())).ToLowerInvariant());
        var source = File.ReadAllBytes(Path.Combine(Root, Map2322UtilityBoard.SourcePath));
        Assert.Equal(Map2322SourceContract.JassSha256, Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant());
        var lines = Encoding.UTF8.GetString(source).Split('\n');
        var direct = lines.Skip(84225).Take(84476 - 84225).Count(s => Regex.IsMatch(s, @"^call AmC\(\$"));
        Assert.Equal(224, direct);
        Assert.Equal(11, b.Rows.Count(r => r.Ability == "A0QJ" && r.StartLine == 84236));
        Assert.Equal(11, b.Rows.Count(r => r.Ability == "A0S1" && r.StartLine == 84327));
        Assert.Equal(direct + 22, b.Rows.Length);
        foreach (var row in b.Rows)
        {
            if (row.StartLine is 84236 or 84327)
            {
                Assert.Equal(row.Ability == "A0QJ" ? -5 * (row.Level - 1) : 5 * (row.Level - 1), row.Value);
                Assert.Equal(row.Ability, row.Family);
                continue;
            }
            var call = Regex.Match(lines[row.StartLine - 1], @"call AmC\(\$([0-9a-fA-F]{8}),(0|\$[0-9a-fA-F]{8}),(\d+),(\d+),(-?[\d.]+|U1C)\)");
            Assert.True(call.Success);
            string Raw(string hex) => Encoding.ASCII.GetString(Convert.FromHexString(hex));
            var ability = Raw(call.Groups[1].Value);
            Assert.Equal(ability, row.Ability);
            Assert.Equal(call.Groups[2].Value == "0" ? ability : Raw(call.Groups[2].Value[1..]), row.Family);
            Assert.Equal(int.Parse(call.Groups[3].Value), row.Category);
            Assert.Equal(int.Parse(call.Groups[4].Value), row.Level);
            Assert.Equal(call.Groups[5].Value == "U1C" ? -30 : double.Parse(call.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture), row.Value);
        }
        Assert.Equal(-50, b.Rows.Single(r => r.Ability == "A0WN" && r.Level == 4).Value);
        Assert.Equal(-5, b.Rows.Single(r => r.Ability == "A0BS").Value);
    }

    [Fact]
    public void FourSlotsSharedOnceAndNoDuplicatedTeammates()
    {
        var b = Board();
        var own = Unit(b, "mine", ("A0BS", 1));
        var s = Snapshot([own], new(0, true, false, false), new(1, true, true, true), new(2, true, true, true), new(3, true, false, false));
        var r = Calc(b, s);
        Assert.Equal(Map2322BoardStatus.Calculated, r.Status);
        Assert.Equal(5, r.OwnTotals!.ArmorReduction);
        Assert.Equal(13, r.DisplayTotals!.ArmorReduction);
        Assert.Equal(5, r.DisplayTotals.AuraSlowPercentagePoints);
        Assert.Equal(new[] { 1, 2 }, r.BrookOwnerSlots);
        Assert.Equal(new[] { 1, 2 }, r.UsoppOwnerSlots);
        Assert.DoesNotContain(r.SelectedOwnRows, x => x.Ability is "A0OS" or "A0Q7");
        Assert.Equal(r.DisplayTotals, Calc(b, s with { PlayerId = 3 }).DisplayTotals);
        Assert.Equal(5, Calc(b, s with { Owners = s.Owners.SetItem(1, new(1, false, false, false)) }).DisplayTotals!.AuraSlowPercentagePoints);
        var lost = s with { Owners = s.Owners.SetItem(1, new(1, false, false, false)).SetItem(2, new(2, false, false, false)) };
        Assert.Equal(r.OwnTotals, Calc(b, lost).DisplayTotals);
        Assert.Equal(Map2322BoardStatus.Invalid, Calc(b, s with { Owners = s.Owners.SetItem(1, new(1, false, true, false)) }).Status);
    }

    [Fact]
    public void A0KYRequiresObservedQualifiedOwnAbilityNotJustGrantToFs()
    {
        var b = Board();
        Assert.DoesNotContain(Calc(b, Snapshot([Unit(b, "own")])).SelectedOwnRows, r => r.Ability == "A0KY");
        var observed = Calc(b, Snapshot([Unit(b, "fs0", ("A0KY", 1))]));
        Assert.Equal(7, observed.OwnTotals!.AuraSlowPercentagePoints);
        Assert.Contains(observed.SelectedOwnRows, r => r.Ability == "A0KY" && r.Family == "B03M");
        // Only A05U has a nonzero Pf type filter; zero-Pf A0KY still needs the ability.
        Assert.DoesNotContain(Calc(b, Snapshot([Unit(b, "wrong-type", ("A05U", 1))])).SelectedOwnRows, r => r.Ability == "A05U");
        var typed = Unit(b, "right-type", ("A05U", 1)) with { TypeId = "h0AH" };
        Assert.Contains(Calc(b, Snapshot([typed])).SelectedOwnRows, r => r.Ability == "A05U");
    }

    [Fact]
    public void EnhancedBuggySpellIsBoardRowWithoutSummonOrMeasuredAura()
    {
        var b = Board();
        Assert.Equal(0, Calc(b, Snapshot([Unit(b, "buggy")])).OwnTotals!.ArmorReduction);
        var r = Calc(b, Snapshot([Unit(b, "buggy", ("A0BS", 1))]));
        Assert.Equal(5, r.OwnTotals!.ArmorReduction);
        Assert.Single(r.SelectedOwnRows, x => x.Ability == "A0BS");
    }

    [Fact]
    public void UnknownIncompleteStaleAndSourceMismatchFailClosed()
    {
        var b = Board(); var u = Unit(b, "mine"); var s = Snapshot([u]);
        var cases = new (Map2322BoardSnapshot? Capture, Map2322BoardStatus Status)[]
        {
            (null, Map2322BoardStatus.Unknown),
            (s with { CompleteQualifiedOwnRoster = null }, Map2322BoardStatus.Incomplete),
            (s with { CompleteSharedOwners = null }, Map2322BoardStatus.Incomplete),
            (s with { Owners = s.Owners.RemoveAt(3) }, Map2322BoardStatus.Incomplete),
            (s with { Owners = s.Owners.SetItem(2, new(2, true, null, false)) }, Map2322BoardStatus.Unknown),
            (s with { OwnUnits = [u with { AbilityLevels = u.AbilityLevels!.Remove("A0KY") }] }, Map2322BoardStatus.Incomplete),
            (s with { OwnUnits = [u with { AbilityLevels = u.AbilityLevels!.SetItem("A0KY", null) }] }, Map2322BoardStatus.Unknown),
            (s with { CapturedAt = Now - Budget - TimeSpan.FromTicks(1) }, Map2322BoardStatus.Stale),
            (s with { MapSha256 = "wrong" }, Map2322BoardStatus.SourceMismatch),
            (s with { JassSha256 = "wrong" }, Map2322BoardStatus.SourceMismatch),
            (s with { RuntimeSource = null }, Map2322BoardStatus.Unknown),
            (s with { OwnUnits = [u, u] }, Map2322BoardStatus.Invalid)
        };
        foreach (var (capture, status) in cases)
        {
            var r = Calc(b, capture!);
            Assert.Equal(status, r.Status);
            Assert.Null(r.DisplayTotals);
            Assert.Empty(r.BrookOwnerSlots);
        }
    }

    [Fact]
    public void RegistryTamperIncludingPlausibleSingleRowAndMissingGeneratedRowFails()
    {
        var original = Data();
        var node = JsonNode.Parse(original)!;
        node["rows"]![0]!["value"] = -1;
        Assert.Throws<InvalidDataException>(() => Map2322UtilityBoard.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
        node = JsonNode.Parse(original)!;
        node["rows"]!.AsArray().RemoveAt(10);
        Assert.Throws<InvalidDataException>(() => Map2322UtilityBoard.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
        node = JsonNode.Parse(original)!;
        node["jassSha256"] = "wrong";
        Assert.Throws<InvalidDataException>(() => Map2322UtilityBoard.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
}
