using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GoalCarryPolicyTests
{
    [Theory]
    [InlineData("솔딜 권장", GoalCarryMode.SoloPreferred)]
    [InlineData("[1상위] 클리어", GoalCarryMode.SoloPreferred)]
    [InlineData("다상위 가능", GoalCarryMode.MultiAllowed)]
    [InlineData("솔딜 또는 다상위", GoalCarryMode.Unknown)]
    [InlineData("다상위권", GoalCarryMode.Unknown)]
    [InlineData("", GoalCarryMode.Unknown)]
    public void DescriptionClassificationUsesExactAuthorityTokens(
        string description, GoalCarryMode expected) =>
        Assert.Equal(expected, GoalCarryPolicy.ClassifyDescription(description));

    [Fact]
    public void CommittedPolicyMatchesDeterministicGenerator()
    {
        var catalog = LoadCatalog();
        var generated = GoalCarryPolicy.GenerateCanonical(catalog,
            AppContext.BaseDirectory);
        var path = Path.Combine(AppContext.BaseDirectory, "Data",
            "goal-carry-policy.json");

        Assert.Equal(File.ReadAllText(path), generated);
        Assert.Equal(generated, GoalCarryPolicy.GenerateCanonical(catalog,
            AppContext.BaseDirectory));
        _ = GoalCarryPolicy.Load(path, catalog);
    }

    private static DataCatalog LoadCatalog()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        return catalog;
    }
}
