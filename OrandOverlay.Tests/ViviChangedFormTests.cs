using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ViviChangedFormTests
{
    [Fact]
    public void ChangedFormMapsMemoryToRareButKeepsCatalogRecipe()
    {
        Assert.True(RawcodeCodec.TryParse("W50h", out var changed));
        var catalog = new DataCatalog();
        catalog.Load();
        var changedUnit = catalog.Unit("rawcode:W50h");

        Assert.Equal("rawcode:O10h", RawcodeCodec.DynamicUnitId(changed));
        Assert.Equal("W50h", RawcodeAliases.CanonicalForStats("W50h"));
        Assert.Equal("변화된", changedUnit.Tier);
        Assert.NotEmpty(changedUnit.Recipe);
        Assert.Equal("rawcode:W50h", changedUnit.Id);
    }

    [Fact]
    public void LilithRawcodeIsNotCollapsedIntoLegacyDockingUnit()
    {
        Assert.True(RawcodeCodec.TryParse("BA0H", out var lilith));
        var catalog = new DataCatalog();
        catalog.Load();

        Assert.Equal("rawcode:BA0H", RawcodeCodec.DynamicUnitId(lilith));
        Assert.Equal("릴리스", catalog.Unit("rawcode:BA0H").Name);
    }
}
