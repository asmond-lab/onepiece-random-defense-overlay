using System.Text.Json;
using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletMechanics2320Tests
{
    public static IEnumerable<object[]> FixtureCases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "bullet-formula-regression-2314-2320.json")));
        var formulas = new Dictionary<string, BulletFormula>
        {
            ["speed_base_unique_aoe"] = BulletFormula.SpeedBaseUniqueAoe,
            ["speed_legendary_aoe"] = BulletFormula.SpeedLegendaryAoe,
            ["speed_extra_single_target"] = BulletFormula.SpeedExtraTarget,
            ["attack_ordinary_target"] = BulletFormula.AttackOrdinary,
            ["attack_special_target"] = BulletFormula.AttackSpecial,
            ["strike_initial_hit"] = BulletFormula.StrikeInitial,
            ["strike_repeated_hit_each"] = BulletFormula.StrikeRepeatEach
        };
        foreach (var formula in doc.RootElement.GetProperty("formulas").EnumerateArray())
            foreach (var sample in formula.GetProperty("samples").EnumerateArray())
                yield return new object[] { formulas[formula.GetProperty("id").GetString()!],
                    sample.GetProperty("inputLevel").GetInt32(),
                    sample.GetProperty("expectedIdealDecimal").GetProperty("old").GetDouble(),
                    sample.GetProperty("expectedIdealDecimal").GetProperty("new").GetDouble() };
    }
    [Theory, MemberData(nameof(FixtureCases))]
    public void Independent35CasesMatchBothVersions(BulletFormula formula, int level, double oldValue, double newValue)
    {
        AssertClose(oldValue, BulletMechanics.Evaluate(BulletMapVersion.V2314, formula, level, level, 1000000));
        AssertClose(newValue, BulletMechanics.Evaluate(BulletMapVersion.V2320, formula, level, level, 1000000));
    }
    private static void AssertClose(double expected, double? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange(Math.Abs(actual.Value - expected), 0, Math.Max(1e-8, Math.Abs(expected) * 1e-12));
    }
    [Fact]
    public void FixtureHasExactly35CasesAndRepeatIsEachOf12()
    {
        Assert.Equal(35, FixtureCases().Count());
        Assert.Equal(12, BulletMechanics.StrikeRepeatCount);
        AssertClose(130000, BulletMechanics.Evaluate(BulletMapVersion.V2320, BulletFormula.StrikeRepeatEach, 0, 4));
        AssertClose(3750000, BulletMechanics.Evaluate(BulletMapVersion.V2320, BulletFormula.StrikeInitial, 4, 0));
        AssertClose(351000, BulletMechanics.Evaluate(BulletMapVersion.V2320, BulletFormula.AttackOrdinary, 99, null, 1000000));
        AssertClose(10800, BulletMechanics.Evaluate(BulletMapVersion.V2314, BulletFormula.AttackOrdinary, 4, null, 1000000));
    }
    [Fact]
    public void UnknownInvalidAndNonfiniteNeverProduceValues()
    {
        Assert.Null(BulletMechanics.Evaluate(BulletMapVersion.Unknown, BulletFormula.StrikeInitial, 3, 3));
        Assert.Null(BulletMechanics.Evaluate(BulletMapVersion.V2320, (BulletFormula)99, 3, 3));
        Assert.Null(BulletMechanics.Evaluate(BulletMapVersion.V2320, BulletFormula.StrikeInitial, null, 3));
        Assert.Null(BulletMechanics.Evaluate(BulletMapVersion.V2320, BulletFormula.StrikeInitial, -1, 3));
        Assert.Null(BulletMechanics.Evaluate(BulletMapVersion.V2320, BulletFormula.AttackOrdinary, 3, 3));
        foreach (var life in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
            Assert.Null(BulletMechanics.Evaluate(BulletMapVersion.V2320, BulletFormula.AttackOrdinary, 3, 3, life));
    }
}
