using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class MapSignalRecognitionTests
{
    [Theory]
    [InlineData(0x6E303030u, 5, "n000", 1)]
    [InlineData(0x6E303041u, 5, "n00A", 1)]
    [InlineData(0x65303139u, 0, "e019", 2)]
    [InlineData(0x65303139u, 3, "e019", 0)]
    public void LiveMemoryRawcode_IsConvertedBeforeStoryAndRewardClassification(
        uint memoryRawcode, byte owner, string mapRawcode, int kind)
    {
        var normalized = MapSignalReadPolicy.ToMapRawcode(memoryRawcode);
        Assert.Equal(mapRawcode, RawcodeCodec.Format(normalized));
        Assert.Equal(kind, (int)Profile.Classify(owner, 0, normalized));
    }

    private static readonly StoryProgressionProfile Story = MapStoryProfileLoader.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly MapSignalRecognitionProfile Profile = MapSignalRecognitionProfile.FromStory(Story);

    [Theory]
    [InlineData(5, 0, "n006", 1)]
    [InlineData(5, 0, "xxxx", 0)]
    [InlineData(7, 0, "n006", 0)]
    [InlineData(3, 0, "n006", 0)]
    [InlineData(15, 0, "n006", 0)]
    [InlineData(0, 0, "e016", 2)]
    [InlineData(15, 0, "e016", 0)]
    [InlineData(3, 0, "e016", 0)]
    [InlineData(0, 0, "help", 0)]
    public void Classify_UsesExactOwnerAndRawcodeWhitelists(
        byte owner, byte localOwner, string rawcode, int expected)
    {
        Assert.True(RawcodeCodec.TryParse(rawcode, out var code));

        Assert.Equal(expected, (int)Profile.Classify(owner, localOwner, code));
    }

    [Fact]
    public void Tracker_RequiresTwoConsistentSnapshotsAndConfirmsARepeatedLowerNewMatch()
    {
        var tracker = new MapSignalSnapshotTracker(Profile);

        Assert.Equal(0, tracker.Observe(Snapshot("n006")).CompletedStoryStageOrdinal);
        var stage6 = tracker.Observe(Snapshot("n006"));
        Assert.Equal(6, stage6.ActiveObjectiveOrdinal);
        Assert.Equal(5, stage6.CompletedStoryStageOrdinal);
        Assert.True(stage6.Stage5Complete);

        Assert.Equal(5, tracker.Observe(Snapshot("n007")).CompletedStoryStageOrdinal);
        var stage7 = tracker.Observe(Snapshot("n007"));
        Assert.Equal(6, stage7.CompletedStoryStageOrdinal);
        Assert.True(stage7.Stage6Complete);

        tracker.Observe(Snapshot("n00A"));
        var marineford = tracker.Observe(Snapshot("n00A"));
        Assert.Equal(8, marineford.CompletedStoryStageOrdinal);
        Assert.True(marineford.MarinefordReady);

        Assert.Equal(8, tracker.Observe(Snapshot("n000")).CompletedStoryStageOrdinal);
        Assert.False(tracker.LastObservationConfirmedReset);
        var nextMatch = tracker.Observe(Snapshot("n000"));
        Assert.True(tracker.LastObservationConfirmedReset);
        Assert.Equal(1, nextMatch.ActiveObjectiveOrdinal);
        Assert.Equal(0, nextMatch.CompletedStoryStageOrdinal);
    }

    [Fact]
    public void Tracker_RejectsOneFrameImpostorMutationAndAmbiguousDuplicates()
    {
        var tracker = new MapSignalSnapshotTracker(Profile);

        tracker.Observe(Snapshot("n006"));
        tracker.Observe(Snapshot("xxxx"));
        Assert.Equal(0, tracker.Observe(Snapshot("n006")).CompletedStoryStageOrdinal);

        tracker.Observe(Snapshot("n006"));
        var duplicate = tracker.Observe(Snapshot("n006", "n006"));
        Assert.Equal(5, duplicate.CompletedStoryStageOrdinal);

        tracker.Reset();
        tracker.Observe(Snapshot("n006", "n007"));
        Assert.Equal(0, tracker.Observe(Snapshot("n006", "n007")).CompletedStoryStageOrdinal);
    }

    [Fact]
    public void Tracker_ConfirmedMarinefordAdvancesPastLingeringObjectiveAndResumesRecommendations()
    {
        var tracker = new MapSignalSnapshotTracker(Profile);
        tracker.Observe(Snapshot("n008"));
        tracker.Observe(Snapshot("n008"));
        Assert.Equal(7, tracker.Observe(Snapshot("n008", "n00A")).CompletedStoryStageOrdinal);
        var advanced = tracker.Observe(Snapshot(["n008", "n00A"], ["e019"]));
        Assert.Equal(8, advanced.CompletedStoryStageOrdinal);
        Assert.False(tracker.LastObservationConfirmedReset);

        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var units = catalog.AllUnits.ToDictionary(unit => unit.Id);
        StoryRewardSequenceDecision Evaluate(MapSignals signals) =>
            StoryRewardSequencePlanner.Evaluate(new StoryRewardSequenceInput
            {
                Phase = PlannerPhase.AwaitMarineford,
                Round = 22,
                ActiveStoryStage = signals.ActiveObjectiveOrdinal,
                CompletedStoryStage = signals.CompletedStoryStageOrdinal,
                RewardWisps = signals.RewardWisps,
                Inventory = [],
                Units = units,
                StoryStages = Story.Stages
            });
        Assert.Equal(StorySequenceAction.SpendRareWisps, Evaluate(advanced).Action);
        var spent = tracker.Observe(Snapshot("n008", "n00A"));
        var sequence = Evaluate(spent);
        Assert.Equal(RecommendationSequenceStage.TopAndNavigation, sequence.Stage);
        var goal = catalog.AllUnits.First(unit => TopGradePolicy.IsTopGrade(unit.Tier));
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Engine = new RecommendationEngine(catalog),
            Goal = goal,
            Inventory = [],
            InitialSurface = RecommendationSurface.StoryLegend,
            StorySequence = sequence,
            NavigationMode = "AlliedForces.DoubleBenefit",
            Gorosei = GoroseiMode.None,
            BuildVariant = BuildVariants.AutoId,
            Difficulty = "악몽"
        });
        Assert.NotEmpty(candidates.Recommendations);
    }

    [Fact]
    public void Tracker_MixedObjectivesRequireOneUnambiguousForwardCandidate()
    {
        var tracker = new MapSignalSnapshotTracker(Profile);
        tracker.Observe(Snapshot("n008"));
        tracker.Observe(Snapshot("n008"));
        tracker.Observe(Snapshot("n008", "n00A"));
        tracker.Observe(Snapshot("n008", "n00A", "n001"));
        Assert.Equal(7, tracker.Observe(Snapshot("n008", "n00A")).CompletedStoryStageOrdinal);
        Assert.Equal(8, tracker.Observe(Snapshot("n008", "n00A")).CompletedStoryStageOrdinal);
        tracker.Observe(Snapshot("n006", "n008"));
        Assert.Equal(8, tracker.Observe(Snapshot("n006", "n008")).CompletedStoryStageOrdinal);
        Assert.False(tracker.LastObservationConfirmedReset);
    }

    [Fact]
    public void Tracker_RetainsLastGoodAcrossDisappearanceTransientAndClearsOnlyOnReset()
    {
        var tracker = new MapSignalSnapshotTracker(Profile);
        tracker.Observe(Snapshot("n007", rewards: ["e016", "e016", "e017"]));
        var confirmed = tracker.Observe(Snapshot("n007", rewards: ["e016", "e016", "e017"]));

        Assert.Same(confirmed, tracker.LastGood);
        Assert.Same(confirmed, tracker.RetainOnTransient());
        Assert.Equal(2, confirmed.RewardWisps["e016"]);
        Assert.Equal(1, confirmed.RewardWisps["e017"]);
        Assert.Equal(6, tracker.Observe(Snapshot()).CompletedStoryStageOrdinal);

        tracker.Reset();
        Assert.Equal(MapSignals.Empty, tracker.LastGood);
    }

    [Fact]
    public void Tracker_DoesNotJumpFromEarlyStoryToLingeringLateObjective()
    {
        var tracker = new MapSignalSnapshotTracker(Profile);
        tracker.Observe(Snapshot("n000"));
        tracker.Observe(Snapshot("n000"));
        tracker.Observe(Snapshot("n000", "n00A"));
        Assert.Equal(1, tracker.Observe(Snapshot("n000", "n00A")).ActiveObjectiveOrdinal);
    }

    [Fact]
    public void Tracker_InitiallyMixedObjectivesRecoverOnNewSequentialObjective()
    {
        var tracker = new MapSignalSnapshotTracker(Profile);
        tracker.Observe(Snapshot("n007", "n008"));
        Assert.Null(tracker.Observe(Snapshot("n007", "n008")).ActiveObjectiveOrdinal);
        Assert.Null(tracker.Observe(Snapshot("n007", "n008", "n00A")).ActiveObjectiveOrdinal);
        var recovered = tracker.Observe(Snapshot("n007", "n008", "n00A"));
        Assert.Equal(9, recovered.ActiveObjectiveOrdinal);
        Assert.True(recovered.MarinefordReady);
        Assert.False(tracker.LastObservationConfirmedReset);
        var decision = StoryRewardSequencePlanner.Evaluate(new StoryRewardSequenceInput
        {
            Phase = PlannerPhase.AwaitMarineford,
            Round = 22,
            ActiveStoryStage = recovered.ActiveObjectiveOrdinal,
            CompletedStoryStage = recovered.CompletedStoryStageOrdinal,
            RewardWisps = recovered.RewardWisps,
            Inventory = [],
            Units = new Dictionary<string, UnitDefinition>(),
            StoryStages = Story.Stages
        });
        Assert.Equal(RecommendationSequenceStage.TopAndNavigation, decision.Stage);
    }

    [Fact]
    public void SignalObjects_AreExcludedFromInventoryAndCatalogRatioAccounting()
    {
        Assert.True(Profile.IsSignal(owner: 5, localOwner: 0, Code("n006")));
        Assert.True(Profile.IsSignal(owner: 0, localOwner: 0, Code("e016")));
        Assert.False(Profile.IsSignal(owner: 0, localOwner: 0, Code("help")));

        var catalog = new DataCatalog();
        catalog.Load();
        var unitMap = new RawcodeUnitMap(catalog);
        Assert.False(unitMap.IsRecognizedCard(Code("n006")));
        Assert.False(unitMap.IsRecognizedCard(Code("e016")));

        var mapped = unitMap.Map(new Dictionary<uint, int>
        {
            [Code("100h")] = 1,
            [Code("help")] = 1
        });
        Assert.Equal(1, mapped.KnownCount + mapped.CatalogNamedCount);
        Assert.Equal(1, mapped.UnknownCount);
        Assert.Equal(0.5, (mapped.KnownCount + mapped.CatalogNamedCount) / 2d);
    }

    [Fact]
    public void SamePassReadPlan_IsBoundedByOwnerCandidates()
    {
        byte[] owners = [0, 5, 7, 3, 15, 5];
        var reads = owners.Count(owner => MapSignalReadPolicy.ShouldReadRawcode(
            owner, localOwner: 0, neutralOwner: 15, trackedGrowth: false));

        Assert.Equal(5, reads);
        Assert.Equal(2, owners.Count(owner => owner == MapSignalRecognitionProfile.StoryObjectiveOwner));
    }

    private static MapSignalRawSnapshot Snapshot(
        params string[] objectives) => Snapshot(objectives, []);

    private static MapSignalRawSnapshot Snapshot(
        string objective, string[] rewards) => Snapshot([objective], rewards);

    private static MapSignalRawSnapshot Snapshot(
        string[] objectives, string[] rewards) => new(
            objectives.Select(Code).ToImmutableArray(),
            rewards.Select(Code).ToImmutableArray());

    private static uint Code(string value)
    {
        Assert.True(RawcodeCodec.TryParse(value, out var code));
        return code;
    }
}
