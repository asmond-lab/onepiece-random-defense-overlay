using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalCandidateReviewTests
{
    private static UnitDefinition U(string id, string tier, params string[] ingredients) => new()
    {
        Id = id, Name = id, Tier = tier,
        Recipe = ingredients.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count())
    };
    private static UnitDefinition[] Units() => [U("a", "흔함"), U("b", "흔함"), U("rare", "희귀함", "a", "b"),
        U("legend", "전설", "rare"), U("unknown-legend", "전설", "rare"), U("upper", "초월", "legend")];
    [Fact]
    public async Task LossDuringDelayedWorkCannotBeUndoneBySelectionFoldsOrDirection()
    {
        var browser = new NormalCandidateBrowser(Units());
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 5);
        browser.Select("rare"); Assert.Equal(0.5, browser.SelectedCandidate!.Completion);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = Task.Run(async () => { await gate.Task; return "work finished"; });
        browser.InvalidateObservation(); // Same synchronous call used before MainWindow recommendation await.
        Assert.False(delayed.IsCompleted);
        browser.Select("rare"); browser.Fold("희귀함", true); browser.SetDirection("magical"); browser.FoldAll(false);
        Assert.False(browser.Snapshot.IsCurrent); Assert.Null(browser.SelectedCandidate!.Allocation);
        Assert.All(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => Assert.Null(c.Allocation));
        gate.SetResult(true); await delayed;
        browser.Fold("희귀함", true); browser.Select("rare");
        Assert.False(browser.Snapshot.IsCurrent); Assert.Null(browser.SelectedCandidate!.Allocation);
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 5);
        Assert.True(browser.Snapshot.IsCurrent); Assert.Equal(0.5, browser.SelectedCandidate!.Completion);
    }
    [Fact]
    public void ApplicationInvalidatesBeforeAwaitAndPresentationDoesNotReuseFrames()
    {
        var source = Source("MainWindow.xaml.cs");
        var start = source.IndexOf("private async void RefreshAll(", StringComparison.Ordinal);
        var fence = source.IndexOf("InvalidateNormalCandidateObservation();", start, StringComparison.Ordinal);
        var awaitWork = source.IndexOf("await _recommendationWork.RunAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && fence > start && awaitWork > fence);
        Assert.Contains("if (!available) InvalidateNormalCandidateObservation();", source);
        Assert.Contains("if (!_coachCurrent || _automaticStale || _automaticDisconnected) InvalidateNormalCandidateObservation();", source);
        var integration = Source("NormalCandidateMain.cs");
        var eventStart = integration.IndexOf("PresentationChanged +=", StringComparison.Ordinal);
        var eventEnd = integration.IndexOf("};", eventStart, StringComparison.Ordinal);
        Assert.DoesNotContain(".Update(", integration[eventStart..eventEnd]);
        Assert.Contains("frame.RecognitionRevision == _recognitionRevision", integration);
        Assert.Contains("frame.MatchGeneration == _adaptivePlanning.MatchGeneration", integration);
        Assert.Contains("!UsesMap2320 && _coachCurrent", integration);
        Assert.Contains("view.ResumeRequested += ResumeNormalCandidates", integration);
        Assert.Contains("_normalFrame = null;", source); // Parent reset fence retained.
    }
    [Fact]
    public void MissingCraftableIntermediateIsUnknownNotAValidLeaf()
    {
        var browser = new NormalCandidateBrowser([U("a", "흔함"), U("missing-rare", "희귀함"), U("legend", "전설", "a", "missing-rare")]);
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1);
        browser.Select("legend"); Assert.Null(browser.SelectedCandidate!.Completion);
    }
    [Theory]
    [InlineData("GOLD")]
    [InlineData("rawcode:GOLD")]
    [InlineData("resource:gold")]
    public void ScalarResourceNeverInflatesCardDenominator(string resource)
    {
        var browser = new NormalCandidateBrowser([U("a", "흔함"), U("rare", "희귀함", "a", resource), U("only-resource", "희귀함", resource)]);
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1);
        browser.Select("rare");
        var allocation = browser.SelectedCandidate!.Allocation!;
        Assert.NotNull(allocation); Assert.Equal(1, allocation.Progress.RequiredLeafCount);
        Assert.Equal(1, allocation.Progress.OwnedLeafCount); Assert.Equal(1, allocation.ResourceRequirements.Gold);
        browser.Select("only-resource"); Assert.Null(browser.SelectedCandidate!.Completion);
    }
    [Fact]
    public void SelectedAllocationSurvivesStageAndDirectionFilteringAndUpdatesWithHand()
    {
        var browser = new NormalCandidateBrowser(Units(), [new("upper", "upper", "magical", [new("스턴", "0.3", "발동")]),
            new("legend", "legend", "magical", [new("스턴", "0.4", "발동")])]);
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1); browser.Select("rare");
        Assert.Equal(0.5, browser.SelectedCandidate!.Completion);
        browser.Update(new Dictionary<string, int> { ["rare"] = 1 }, true, 1);
        Assert.DoesNotContain(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => c.Unit.Id == "rare");
        Assert.Equal(1.0, browser.SelectedCandidate!.Completion);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["legend"] = 1 }, true, 1); browser.Select("legend");
        browser.SetDirection("physical");
        Assert.DoesNotContain(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => c.Unit.Id == "legend");
        Assert.Equal(1.0, browser.SelectedCandidate!.Completion);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1 }, true, 1);
        Assert.Equal(0.0, browser.SelectedCandidate!.Completion);
        browser.InvalidateObservation(); Assert.Null(browser.SelectedCandidate!.Completion);
    }
    [Fact]
    public void UnknownDirectionCandidateIsAccessibleButNeverHighlightedOrClaimedCompatible()
    {
        var browser = new NormalCandidateBrowser(Units(), [new("upper", "upper", "magical", [new("스턴", "0.3", "발동")]),
            new("unknown-legend", "unknown", "unknown", [new("스턴", "0.4", "발동")])]);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["a"] = 1 }, true, 1);
        var unknown = browser.Snapshot.Groups.Single(g => g.Name == "마딜+스턴").Candidates.Single(c => c.Unit.Id == "unknown-legend");
        Assert.True(unknown.Completion > 0); Assert.True(browser.DirectionUnverified(unknown.Unit.Id)); Assert.False(browser.CanHighlight(unknown));
        Assert.Equal("unknown", unknown.DamageType);
        Assert.Contains("group.Candidates.FirstOrDefault(CanHighlightForBrowsing)", Source("NormalCandidateView.cs"));
    }
    [Fact]
    public void PausedNormalViewsExposeResumeWithoutMakingObservationCurrent()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var model = new NormalCandidateBrowser(Units());
                model.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1, paused: true);
                Assert.False(model.Snapshot.IsCurrent); Assert.True(model.IsPaused);
                var viewType = typeof(NormalCandidateBrowser).Assembly.GetType("OrandOverlay.NormalCandidateView")!;
                var resumes = 0;
                foreach (var unused in new[] { "main", "overlay" })
                {
                    var view = Activator.CreateInstance(viewType)!;
                    viewType.GetMethod("SetModel")!.Invoke(view, [model]);
                    Assert.True((bool)viewType.GetProperty("CanResume")!.GetValue(view)!);
                    viewType.GetEvent("ResumeRequested")!.AddEventHandler(view, new Action(() => resumes++));
                    viewType.GetMethod("RequestResume")!.Invoke(view, null);
                }
                Assert.Equal(2, resumes); Assert.False(model.Snapshot.IsCurrent);
            }
            catch (Exception e) { error = e; }
        });
        thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15))); Assert.Null(error);
    }
    private static string Source(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs"))) root = root.Parent;
        Assert.NotNull(root); return File.ReadAllText(Path.Combine(root!.FullName, file));
    }
}
