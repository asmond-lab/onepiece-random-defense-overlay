using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GameplayReceiptMatcherTests
{
    private readonly DataCatalog _catalog;

    public GameplayReceiptMatcherTests()
    {
        var units = new List<UnitDefinition>
        {
            Unit("mat-a", "일반"),
            Unit("mat-b", "일반"),
            Unit("other-mat", "일반"),
            Unit("amb-mat", "일반"),
            Unit("branch-mat", "일반"),
            Unit("common-a", "흔함"),
            Unit("common-b", "흔함"),
            Unit("uncommon-a", "안흔함"),
            Unit("special-a", "특별함"),
            Unit("rare-a", "희귀함"),
            Unit("rare-b", "희귀함"),
            Unit("craft-a", "안흔함", ("mat-a", 2), ("mat-b", 1)),
            Unit("craft-b", "특별함", ("mat-b", 3)),
            Unit("amb-a", "흔함", ("amb-mat", 1)),
            Unit("amb-b", "흔함", ("amb-mat", 1)),
            Unit("chain-output", "안흔함", ("common-a", 1))
        };
        for (var i = 0; i < 65; i++) units.Add(Unit($"budget-{i:D2}", "흔함"));
        for (var i = 0; i < 15; i++) units.Add(Unit($"branch-{i:D2}", "흔함", ("branch-mat", 1 << i)));

        var path = Path.Combine(Path.GetTempPath(), $"orand-receipt-catalog-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new GameData { SchemaVersion = 1, Units = units }));
        try
        {
            _catalog = new DataCatalog(path);
            _catalog.Load(loadCarryPolicy: false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PureCraftMatchesExistingExactWholeInventoryBehavior()
    {
        var before = Observation(Counts(("mat-a", 2), ("mat-b", 1)));
        var after = Observation(Counts(("craft-a", 1)), revision: 2);

        var result = GameplayReceiptMatcher.Match(before, after, _catalog);

        Assert.Equal(GameplayReceiptMatchOutcome.IsMatched, result.Outcome);
        Assert.Empty(result.Selections);
        var craft = Assert.Single(result.Crafts);
        Assert.Equal("craft-a", craft.UnitId);
        Assert.Equal(1, craft.Count);
        Assert.Equal(2, craft.Consumed["mat-a"]);
        Assert.Equal(1, craft.Consumed["mat-b"]);
    }

    [Fact]
    public void PureSelectionMatchesExistingExactTierAndCountBehavior()
    {
        var before = Observation(Counts(), Counts(("e018", 2)));
        var after = Observation(Counts(("common-a", 2)), Counts(("e018", 0)), 2);

        var result = GameplayReceiptMatcher.Match(before, after, _catalog);

        Assert.True(result.IsMatched);
        Assert.Empty(result.Crafts);
        var selection = Assert.Single(result.Selections);
        Assert.Equal("e018", selection.WispId);
        Assert.Equal(2, selection.Count);
        Assert.Equal(2, selection.Outputs["common-a"]);
    }

    [Fact]
    public void TwoOrMoreSelectionWispTiersProduceDistinctExactReceipts()
    {
        var before = Observation(Counts(), Counts(("e018", 2), ("e017", 1), ("e016", 1), ("e019", 2)));
        var after = Observation(
            Counts(("common-a", 1), ("common-b", 1), ("uncommon-a", 1), ("special-a", 1),
                ("rare-a", 1), ("rare-b", 1)),
            Counts(("e018", 0), ("e017", 0), ("e016", 0), ("e019", 0)),
            2);

        var result = GameplayReceiptMatcher.Match(before, after, _catalog);

        Assert.True(result.IsMatched);
        Assert.Empty(result.Crafts);
        Assert.Equal(4, result.Selections.Length);
        Assert.Equal(2, result.Selections.Single(x => x.WispId == "e018").Outputs.Values.Sum());
        Assert.Equal("uncommon-a", Assert.Single(result.Selections.Single(x => x.WispId == "e017").Outputs).Key);
        Assert.Equal("special-a", Assert.Single(result.Selections.Single(x => x.WispId == "e016").Outputs).Key);
        Assert.Equal(2, result.Selections.Single(x => x.WispId == "e019").Outputs.Values.Sum());
    }

    [Fact]
    public void SimultaneousDisjointCraftAndSelectionHasOneWholeInventoryDecomposition()
    {
        var before = Observation(Counts(("mat-a", 2), ("mat-b", 1)), Counts(("e018", 1)));
        var after = Observation(Counts(("craft-a", 1), ("common-a", 1)), Counts(("e018", 0)), 2);

        var result = GameplayReceiptMatcher.Match(before, after, _catalog);

        Assert.True(result.IsMatched);
        Assert.Equal("craft-a", Assert.Single(result.Crafts).UnitId);
        var selection = Assert.Single(result.Selections);
        Assert.Equal("common-a", Assert.Single(selection.Outputs).Key);
        Assert.Equal(1, selection.Count);
    }

    [Fact]
    public void TwoValidWholeInventoryDecompositionsAreAmbiguous()
    {
        var before = Observation(Counts(("amb-mat", 1)), Counts(("e018", 1)));
        var after = Observation(Counts(("amb-a", 1), ("amb-b", 1)), Counts(("e018", 0)), 2);

        var result = GameplayReceiptMatcher.Match(before, after, _catalog);

        Assert.True(result.IsAmbiguous);
        Assert.Empty(result.Crafts);
        Assert.Empty(result.Selections);
    }

    [Fact]
    public void UnexplainedInventoryGainOrLossDeclinesReceipt()
    {
        var baseBefore = Counts(("mat-a", 2), ("mat-b", 1));
        var unexplainedGain = GameplayReceiptMatcher.Match(
            Observation(baseBefore),
            Observation(Counts(("craft-a", 1), ("common-a", 1)), revision: 2),
            _catalog);
        var unexplainedLoss = GameplayReceiptMatcher.Match(
            Observation(baseBefore.Add("other-mat", 1)),
            Observation(Counts(("craft-a", 1)), revision: 2),
            _catalog);

        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, unexplainedGain.Outcome);
        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, unexplainedLoss.Outcome);
    }

    [Theory]
    [InlineData("e0IX")]
    [InlineData("e01A")]
    [InlineData("unsupported")]
    public void UnsupportedRandomOrTranscendenceWispChangeAlwaysDeclines(string wispId)
    {
        var result = GameplayReceiptMatcher.Match(
            Observation(Counts(), Counts((wispId, 1))),
            Observation(Counts(("common-a", 1)), Counts((wispId, 0)), 2),
            _catalog);

        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, result.Outcome);
    }

    [Fact]
    public void SupportedWispIncreaseWrongTierOrUnexplainedSecondSpendDeclines()
    {
        var increase = GameplayReceiptMatcher.Match(
            Observation(Counts(("common-a", 1)), Counts(("e018", 0))),
            Observation(Counts(("common-a", 2)), Counts(("e018", 1)), 2),
            _catalog);
        var wrongTier = GameplayReceiptMatcher.Match(
            Observation(Counts(), Counts(("e018", 1))),
            Observation(Counts(("uncommon-a", 1)), Counts(("e018", 0)), 2),
            _catalog);
        var unexplainedSecondSpend = GameplayReceiptMatcher.Match(
            Observation(Counts(), Counts(("e018", 1), ("e017", 1))),
            Observation(Counts(("common-a", 1)), Counts(("e018", 0), ("e017", 0)), 2),
            _catalog);

        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, increase.Outcome);
        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, wrongTier.Outcome);
        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, unexplainedSecondSpend.Outcome);
    }

    [Fact]
    public void NegativeAndDeceptiveCountersNeverCreateReceipts()
    {
        var negativeInventory = GameplayReceiptMatcher.Match(
            Observation(Counts(("mat-a", -1))),
            Observation(Counts(("craft-a", 1)), revision: 2),
            _catalog);
        var negativeWisp = GameplayReceiptMatcher.Match(
            Observation(Counts(), Counts(("e018", 0))),
            Observation(Counts(("common-a", 1)), Counts(("e018", -1)), 2),
            _catalog);
        var overflowShaped = GameplayReceiptMatcher.Match(
            Observation(Counts(("mat-a", int.MinValue))),
            Observation(Counts(("mat-a", int.MaxValue), ("craft-a", 1)), revision: 2),
            _catalog);

        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, negativeInventory.Outcome);
        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, negativeWisp.Outcome);
        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, overflowShaped.Outcome);
    }

    [Fact]
    public void UnknownChangedUnitCannotMatchAReceipt()
    {
        var result = GameplayReceiptMatcher.Match(
            Observation(Counts(), Counts(("e018", 1))),
            Observation(Counts(("unknown-unit", 1)), Counts(("e018", 0)), 2),
            _catalog);

        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, result.Outcome);
    }

    [Fact]
    public void DistinctGainAndUnitTotalBoundsAreExplicit()
    {
        var tooManyGains = Enumerable.Range(0, 65).Select(i => ($"budget-{i:D2}", 1)).ToArray();
        var distinctBound = GameplayReceiptMatcher.Match(
            Observation(Counts(), Counts(("e018", 65))),
            Observation(Counts(tooManyGains), Counts(("e018", 0)), 2),
            _catalog);
        var totalBound = GameplayReceiptMatcher.Match(
            Observation(Counts(), Counts(("e018", 1_000_001))),
            Observation(Counts(("common-a", 1_000_001)), Counts(("e018", 0)), 2),
            _catalog);

        Assert.True(distinctBound.IsBoundExceeded);
        Assert.True(totalBound.IsBoundExceeded);
    }

    [Fact]
    public void CombinedAssignmentSearchStopsAtBranchBudgetInsteadOfTakingFirstCandidate()
    {
        var gains = Enumerable.Range(0, 15).Select(i => ($"branch-{i:D2}", 1)).ToArray();
        var result = GameplayReceiptMatcher.Match(
            Observation(Counts(("branch-mat", 32640)), Counts(("e018", 7))),
            Observation(Counts(gains), Counts(("e018", 0)), 2),
            _catalog);

        Assert.Equal(GameplayReceiptMatchOutcome.BoundExceeded, result.Outcome);
        Assert.Empty(result.Crafts);
        Assert.Empty(result.Selections);
    }

    [Fact]
    public void SameUnitCraftAndSelectionSplitIsExactAndNeverDoubleCounted()
    {
        var result = GameplayReceiptMatcher.Match(
            Observation(Counts(("amb-mat", 1)), Counts(("e018", 1))),
            Observation(Counts(("amb-a", 2)), Counts(("e018", 0)), 2),
            _catalog);

        Assert.True(result.IsMatched);
        var craft = Assert.Single(result.Crafts);
        var selection = Assert.Single(result.Selections);
        Assert.NotEmpty(selection.Outputs);
        Assert.Equal(1, craft.Count);
        Assert.Equal(1, selection.Outputs["amb-a"]);
        Assert.Equal(2, craft.Count + selection.Outputs["amb-a"]);
    }

    [Fact]
    public void SelectedThenConsumedIntermediateIdentityIsNotGuessed()
    {
        var result = GameplayReceiptMatcher.Match(
            Observation(Counts(), Counts(("e018", 1))),
            Observation(Counts(("chain-output", 1)), Counts(("e018", 0)), 2),
            _catalog);

        Assert.Equal(GameplayReceiptMatchOutcome.NotMatched, result.Outcome);
    }

    private static UnitDefinition Unit(string id, string tier, params (string Id, int Count)[] recipe) => new()
    {
        Id = id,
        Name = id,
        Tier = tier,
        Recipe = recipe.ToDictionary(pair => pair.Id, pair => pair.Count, StringComparer.Ordinal)
    };

    private static GameplayTelemetryObservation Observation(
        ImmutableDictionary<string, int> inventory,
        ImmutableDictionary<string, int>? wisps = null,
        long revision = 1) => new()
    {
        MatchGeneration = 1,
        RecognitionRevision = revision,
        Round = 20,
        Inventory = inventory,
        RewardWisps = wisps ?? Counts()
    };

    private static ImmutableDictionary<string, int> Counts(params (string Id, int Count)[] entries) =>
        entries.ToImmutableDictionary(pair => pair.Id, pair => pair.Count, StringComparer.Ordinal);
}
