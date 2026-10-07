using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalReferenceProgressionTests
{
    private static readonly Lazy<DataCatalog> Data = new(() => { var c = new DataCatalog(); c.Load(mapVersion: "2.320"); return c; });
    private static UnitDefinition Rare => Data.Value.AllUnits.First(u => NormalCandidateBrowser.Tier(u) is "희귀" or "희귀함");
    private static UnitDefinition Hidden => Data.Value.AllUnits.First(u => NormalCandidateBrowser.Tier(u) == "히든");
    private static UnitDefinition[] Uppers => Data.Value.AllUnits.Where(NormalCandidateBrowser.IsUpper).Take(3).ToArray();
    private static IDiagnosticInventoryReference Observation(IEnumerable<string> ids, bool basic = false,
        char context = 'A', char binding = 'B', char world = 'C', int view = 0, long revision = 1)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = ids.Select(id => new InventoryEntry { UnitId = id, Count = 1 });
        return basic ? DiagnosticBasicInventoryObservation.Create(Data.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            new string(context, 64), revision, view, now.AddMilliseconds(-100), now, TimeSpan.FromMilliseconds(100),
            entries, new string(world, 64), new string(binding, 64))
            : DiagnosticInventoryObservation.Create(Data.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                new string(context, 64), revision, view, now.AddMilliseconds(-100), now, TimeSpan.FromMilliseconds(100),
                entries, [], worldStampFingerprint: new string(world, 64), bindingContextId: new string(binding, 64));
    }
    private static NormalCandidateBrowser Browser() => new(Data.Value.AllUnits) { ReferencePresentationIsValid = () => true };

    [Fact]
    public void BasicAndFullReferencesCatchUpWithoutGameplayAuthority()
    {
        var browser = Browser();
        var rare = Observation([Rare.Id], basic: true);
        browser.UpdateReference(rare, 1);
        Assert.Equal(NormalCandidateStage.Legend, browser.ProgressStage);
        Assert.Equal(browser.ProgressStage, browser.Stage);
        browser.UpdateReference(Observation([Hidden.Id]), 1);
        Assert.Equal(NormalCandidateStage.Upper, browser.ProgressStage);
        var upper = Observation([Uppers[0].Id]);
        browser.UpdateReference(upper, 1);
        Assert.Equal(NormalCandidateStage.Utility, browser.ProgressStage);
        Assert.Equal(Uppers[0].Id, browser.FirstUpperId);
        Assert.False(upper.GameplayReady); Assert.False(upper.CanProvideCoachCurrent);
        Assert.False(rare.GameplayReady); Assert.False(rare.CanProvideCoachCurrent);
        browser.Select(Uppers[0].Id);
        Assert.False(browser.SelectedCandidate!.Owned);
        Assert.Equal(1, browser.SelectedCandidate.ObservedCount);
        Assert.False(browser.CanHighlight(browser.SelectedCandidate));
    }
    [Fact]
    public void ConsumptionPauseExpiryAndRejectedReferencesNeverRewindProgress()
    {
        var browser = Browser();
        browser.UpdateReference(Observation([Uppers[0].Id]), 1);
        browser.UpdateReference(Observation([]), 1);
        browser.InvalidateReference(paused: true);
        Assert.Equal(NormalCandidateStage.Utility, browser.ProgressStage);
        Assert.Equal(Uppers[0].Id, browser.FirstUpperId);
        Assert.Empty(browser.CurrentInventory);
        Assert.All(browser.MovementCoverage, c => Assert.False(c.Covered));
        browser.UpdateReference(Observation([Uppers[1].Id]), 1);
        Assert.Equal(Uppers[0].Id, browser.FirstUpperId);
        browser.ReferencePresentationIsValid = () => false;
        browser.UpdateReference(Observation([Uppers[2].Id]), 1);
        Assert.False(browser.Snapshot.IsCurrent);
        Assert.Equal(Uppers[0].Id, browser.FirstUpperId);
        Assert.Equal(NormalCandidateStage.Utility, browser.ProgressStage);
    }
    [Fact]
    public void ManualBrowsingSelectionAndFoldsPersistWhileObservedProgressAdvances()
    {
        var browser = Browser();
        browser.UpdateReference(Observation([Rare.Id]), 1);
        browser.SetReferenceStage(NormalCandidateStage.Rare); browser.Select(Rare.Id); browser.Fold("희귀함", true);
        browser.UpdateReference(Observation([Uppers[0].Id]), 1);
        Assert.False(browser.FollowingProgress);
        Assert.Equal(NormalCandidateStage.Rare, browser.Stage);
        Assert.Equal(NormalCandidateStage.Utility, browser.ProgressStage);
        Assert.Equal(Rare.Id, browser.SelectedUnitId); Assert.Contains("희귀함", browser.CollapsedCategories);
        browser.InvalidateReference(); browser.SetReferenceStage(NormalCandidateStage.Legend);
        Assert.False(browser.Snapshot.IsCurrent);
        browser.ResumeProgress();
        Assert.True(browser.FollowingProgress);
        Assert.Equal(NormalCandidateStage.Utility, browser.Stage);
        Assert.Equal(Rare.Id, browser.SelectedUnitId);
    }
    [Theory]
    [InlineData('B', 'C', 0, 2)]
    [InlineData('D', 'C', 0, 1)]
    [InlineData('B', 'C', 1, 1)]
    public void NewMatchBindingOrViewResetsSession(char binding, char world, int view, long generation)
    {
        var browser = Browser();
        browser.UpdateReference(Observation([Uppers[0].Id]), 1);
        browser.SetReferenceStage(NormalCandidateStage.Upper); browser.Select(Uppers[0].Id);
        browser.UpdateReference(Observation([], binding: binding, world: world, view: view), generation);
        Assert.Equal(NormalCandidateStage.Rare, browser.ProgressStage);
        Assert.Equal(NormalCandidateStage.Rare, browser.Stage);
        Assert.True(browser.FollowingProgress); Assert.Null(browser.FirstUpperId); Assert.Null(browser.SelectedUnitId);
    }
    [Fact]
    public void IdenticalBasicFullInputsReuseSnapshotCandidatesAndEmitNoRefreshLoop()
    {
        var browser = Browser();
        browser.UpdateReference(Observation([Rare.Id], basic: true), 1);
        var snapshot = browser.Snapshot;
        var first = snapshot.Groups.SelectMany(g => g.Candidates).First();
        browser.Select(first.Unit.Id);
        Assert.Same(first, browser.SelectedCandidate);
        var events = 0; browser.PresentationChanged += () => events++;
        browser.UpdateReference(Observation([Rare.Id], context: 'D', revision: 2), 1);
        browser.UpdateReference(Observation([Rare.Id], basic: true, revision: 3), 1);
        browser.Select(first.Unit.Id); browser.SetDirection("unknown");
        Assert.Same(snapshot, browser.Snapshot); Assert.Same(first, browser.SelectedCandidate);
        Assert.Equal(0, events);
    }
    [Fact]
    public void MultipleInitialUppersRequireExplicitAnchorEvenAfterOneIsConsumed()
    {
        var browser = Browser();
        browser.UpdateReference(Observation(Uppers.Take(2).Select(u => u.Id)), 1);
        Assert.Null(browser.FirstUpperId); Assert.Equal(2, browser.FirstUpperChoices.Count);
        browser.UpdateReference(Observation([Uppers[0].Id]), 1);
        Assert.Null(browser.FirstUpperId);
        Assert.False(browser.SelectFirstUpper(Uppers[2].Id));
        Assert.True(browser.SelectFirstUpper(Uppers[1].Id));
        Assert.Equal(Uppers[1].Id, browser.FirstUpperId);
        Assert.False(browser.SelectFirstUpper(Uppers[0].Id));
        browser.UpdateReference(Observation([Uppers[2].Id]), 1);
        Assert.Equal(Uppers[1].Id, browser.FirstUpperId);
    }
    [Fact]
    public void WorldStampChangeUpdatesHandWithoutResettingProgressOrUserChoices()
    {
        var browser = Browser();
        browser.UpdateReference(Observation([Uppers[0].Id]), 1);
        browser.SetStage(NormalCandidateStage.Legend); browser.Select(Uppers[0].Id); browser.Fold("전설", true);
        var before = browser.Snapshot;
        browser.UpdateReference(Observation([Rare.Id], world: 'D', revision: 2), 1);
        Assert.NotSame(before, browser.Snapshot);
        Assert.True(browser.Snapshot.IsCurrent);
        Assert.Equal(1, browser.CurrentInventory[Rare.Id]);
        Assert.False(browser.CurrentInventory.ContainsKey(Uppers[0].Id));
        Assert.Equal(NormalCandidateStage.Utility, browser.ProgressStage);
        Assert.Equal(Uppers[0].Id, browser.FirstUpperId);
        Assert.False(browser.FollowingProgress);
        Assert.Equal(NormalCandidateStage.Legend, browser.Stage);
        Assert.Equal(Uppers[0].Id, browser.SelectedUnitId);
        Assert.Contains("전설", browser.CollapsedCategories);
    }
}
