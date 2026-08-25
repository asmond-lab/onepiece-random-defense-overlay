using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class MapCombineCommandAuthorityTests
{
    [Fact]
    public void EveryMapCombineCommandMatchesEffectiveCatalog()
    {
        var catalog = LoadCatalog();
        var authority = LoadAuthority();

        Assert.Equal(81, authority.Count);
        foreach (var (rawcode, expected) in authority)
        {
            var actual = catalog.Unit("rawcode:" + rawcode).CombineCommands;
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void KoalaUsesRevolutionaryArmyInstructorCommand()
    {
        var catalog = LoadCatalog();

        Assert.Equal(["혁명군대리사범", "koala"],
            catalog.Unit("rawcode:V30h").CombineCommands);
    }

    [Fact]
    public void CommandBearingCatalogUnitsAreMapBackedOrVirtualAliases()
    {
        var catalog = LoadCatalog();
        var authority = LoadAuthority();
        var allowedVirtualAliases = new HashSet<string>(StringComparer.Ordinal)
        {
            "KB0H_"
        };

        var unsupported = catalog.AllUnits
            .Where(unit => unit.CombineCommands.Count > 0)
            .SelectMany(unit => unit.Rawcodes.DefaultIfEmpty(unit.Id))
            .Where(rawcode => !authority.ContainsKey(rawcode) &&
                              !allowedVirtualAliases.Contains(rawcode))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(rawcode => rawcode, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unsupported);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>
        LoadAuthority()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data",
            "map-combine-commands-2314.txt");
        return File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && line[0] != '#')
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0],
                parts => (IReadOnlyList<string>)parts[1].Split('|').ToList(),
                StringComparer.Ordinal);
    }

    private static DataCatalog LoadCatalog()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        return catalog;
    }
}
