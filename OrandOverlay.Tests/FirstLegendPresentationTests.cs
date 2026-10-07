using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class FirstLegendPresentationTests
{
    [Fact]
    public Task MainAndUnitOverlayStartFirstLegendOnStoryForSealedDiagnosticReference() => Sta(() =>
    {
        var fixture = new Fixture(startAtLegend: true);
        var main = fixture.CreateView(isMain: true, 1080, 720);
        var overlay = fixture.CreateView(isMain: false, 540, 480);

        Assert.True(fixture.Model.IsDiagnosticReference);
        Assert.Equal("스토리", SelectedCanonicalCategory(main));
        Assert.Equal(fixture.Model.Snapshot.Groups.Single(group => group.Name == "스토리").Candidates
            .Take(10).Select(candidate => candidate.Unit.Id), CandidateIds(main));
        AssertLegendStoryOnly(CategoryExpanders(overlay));
    });

    [Fact]
    public Task RareManualCategoryAndFoldCannotBypassStoryWhenBothViewsEnterLegend() => Sta(() =>
    {
        var fixture = new Fixture(startAtLegend: false);
        var main = fixture.CreateView(isMain: true, 1080, 720);
        var overlay = fixture.CreateView(isMain: false, 540, 480);
        Assert.Equal(NormalCandidateStage.Rare, fixture.Model.Stage);

        SelectCategory(main, "전체 카테고리");
        var rareFold = CategoryExpanders(overlay).Values.First();
        rareFold.IsExpanded = false;
        Assert.NotEmpty(fixture.Model.CollapsedCategories);

        fixture.EnterLegend();
        overlay.Render(); // UNIT may render before MAIN in production.
        main.Render();

        Assert.Equal("스토리", SelectedCanonicalCategory(main));
        AssertLegendStoryOnly(CategoryExpanders(overlay));
        Assert.DoesNotContain(fixture.Model.CollapsedCategories,
            category => !fixture.Model.Snapshot.Groups.Any(group => group.Name == category));
    });

    [Fact]
    public Task LegendExplicitCategoryAndFoldSurviveSameChangedAndStaleDiagnosticHands() => Sta(() =>
    {
        var fixture = new Fixture(startAtLegend: true);
        var main = fixture.CreateView(isMain: true, 1080, 720);
        var overlay = fixture.CreateView(isMain: false, 540, 480);
        SelectCategory(main, "공중이동");
        SetOnlyExpanded(overlay, "공중이동");

        fixture.RefreshSameHand();
        main.Render(); overlay.Render();
        AssertLegendChoice(main, overlay);

        fixture.ChangeHand();
        main.Render(); overlay.Render();
        AssertLegendChoice(main, overlay);

        fixture.Model.InvalidateReference();
        main.Render(); overlay.Render();
        Assert.False(fixture.Model.Snapshot.IsCurrent);
        AssertLegendChoice(main, overlay);
    });

    [Fact]
    public Task LateSiblingInitializationDoesNotEraseSameStageExpandAllChoice() => Sta(() =>
    {
        var fixture = new Fixture(startAtLegend: true);
        var firstOverlay = fixture.CreateView(isMain: false, 540, 480);
        firstOverlay.Render();
        var settled = CategoryExpanders(firstOverlay);
        firstOverlay.Render();
        var repeated = CategoryExpanders(firstOverlay);
        Assert.Equal(settled.Keys, repeated.Keys);
        foreach (var category in settled.Keys) Assert.Same(settled[category], repeated[category]);

        Click(firstOverlay, "모두 펼치기");
        Assert.All(CategoryExpanders(firstOverlay).Values, expander => Assert.True(expander.IsExpanded));

        var lateOverlay = fixture.CreateView(isMain: false, 540, 480);

        Assert.All(CategoryExpanders(lateOverlay).Values, expander => Assert.True(expander.IsExpanded));
        firstOverlay.Render();
        Assert.All(CategoryExpanders(firstOverlay).Values, expander => Assert.True(expander.IsExpanded));
    });

    [Fact]
    public Task UtilityDiagnosticReferenceKeepsPreviousExpandAllDefault() => Sta(() =>
    {
        var fixture = new Fixture(startAtLegend: false);
        fixture.EnterUtility();
        var overlay = fixture.CreateView(isMain: false, 540, 480);
        var folds = CategoryExpanders(overlay);

        Assert.True(fixture.Model.IsDiagnosticReference);
        Assert.Equal(NormalCandidateStage.Utility, fixture.Model.Stage);
        Assert.True(folds.Count > 1, "Bundled diagnostic Utility fixture must expose multiple categories.");
        Assert.All(folds.Values, expander => Assert.True(expander.IsExpanded));
    });

    [Fact]
    public Task ClosestCategoryKeepsCanonicalIdentityAndCandidateProjection() => Sta(() =>
    {
        var fixture = new Fixture(startAtLegend: true);
        var main = fixture.CreateView(isMain: true, 920, 620);
        SelectCategory(main, "가까운 조합");

        var picker = Find<ComboBox>(main).Single(control =>
            AutomationProperties.GetAutomationId(control) == "normal-category-picker");
        Assert.Equal("가까운 조합", SelectedCanonicalCategory(main));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(picker)));
        Assert.Equal(fixture.Model.Snapshot.Groups.Single(group => group.Name == "가까운 조합").Candidates
            .Take(10).Select(candidate => candidate.Unit.Id), CandidateIds(main));
    });

    private static void AssertLegendChoice(NormalCandidateView main, NormalCandidateView overlay)
    {
        Assert.Equal("공중이동", SelectedCanonicalCategory(main));
        var folds = CategoryExpanders(overlay);
        Assert.False(folds["스토리"].IsExpanded);
        Assert.True(folds["공중이동"].IsExpanded);
        Assert.False(folds["가까운 조합"].IsExpanded);
    }

    private static void AssertLegendStoryOnly(IReadOnlyDictionary<string, Expander> folds)
    {
        Assert.Equal(new[] { "스토리", "공중이동", "가까운 조합" }, folds.Keys);
        Assert.True(folds["스토리"].IsExpanded);
        Assert.False(folds["공중이동"].IsExpanded);
        Assert.False(folds["가까운 조합"].IsExpanded);
    }

    private sealed class Fixture
    {
        private readonly DataCatalog _catalog = new();
        private readonly UnitDefinition _firstRare;
        private readonly UnitDefinition _secondRare;
        private readonly UnitDefinition _upper;
        private long _revision;
        internal NormalCandidateBrowser Model { get; }

        internal Fixture(bool startAtLegend)
        {
            _catalog.Load(loadCarryPolicy: false, mapVersion: "2.321");
            Model = NormalCandidateBrowser.Create(_catalog);
            Model.ReferencePresentationIsValid = () => true;
            var rares = _catalog.AllUnits.Where(unit => NormalCandidateBrowser.Tier(unit) is "희귀함" or "희귀")
                .OrderBy(unit => unit.Id, StringComparer.Ordinal).Take(2).ToArray();
            Assert.Equal(2, rares.Length);
            (_firstRare, _secondRare) = (rares[0], rares[1]);
            _upper = NormalGuideProfile.LoadBundled().Select(guide => _catalog.Unit(guide.UnitId))
                .First(unit => NormalCandidateBrowser.IsUpper(unit));
            Observe(startAtLegend ? [_firstRare.Id] : []);
            Assert.True(Model.IsDiagnosticReference);
            Assert.Equal(startAtLegend ? NormalCandidateStage.Legend : NormalCandidateStage.Rare, Model.Stage);
            if (startAtLegend)
                Assert.Equal(new[] { "스토리", "공중이동", "가까운 조합" }, Model.Snapshot.Groups.Select(group => group.Name));
        }

        internal NormalCandidateView CreateView(bool isMain, double width, double height)
        {
            var view = new NormalCandidateView { IsMainWorkspace = isMain, Width = width, Height = height };
            view.SetCraftPlanner(new NormalCraftPlanner(_catalog));
            view.SetModel(Model);
            Layout(view, width, height);
            return view;
        }

        internal void EnterLegend() => Observe([_firstRare.Id]);
        internal void EnterUtility() => Observe([_upper.Id]);
        internal void RefreshSameHand() => Observe([_firstRare.Id]);
        internal void ChangeHand() => Observe([_firstRare.Id, _secondRare.Id]);

        private void Observe(IEnumerable<string> ids)
        {
            var now = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero).AddSeconds(++_revision);
            var observation = DiagnosticInventoryObservation.Create(_catalog, Warcraft300Diagnostic.Version,
                Warcraft300Diagnostic.Hash, new string('A', 64), _revision, 0,
                now.AddMilliseconds(-10), now, TimeSpan.FromMilliseconds(10),
                ids.Select(id => new InventoryEntry { UnitId = id, Count = 1 }), [],
                worldStampFingerprint: new string('C', 64), bindingContextId: new string('B', 64));
            Model.UpdateReference(observation, 1);
        }
    }

    private static void SelectCategory(NormalCandidateView view, string canonicalName)
    {
        var picker = Find<ComboBox>(view).Single(control =>
            AutomationProperties.GetAutomationId(control) == "normal-category-picker");
        picker.SelectedItem = picker.Items.Cast<object>().Single(item => item.ToString()!.StartsWith(canonicalName, StringComparison.Ordinal));
        Layout(view, view.Width, view.Height);
    }

    private static string SelectedCanonicalCategory(NormalCandidateView view)
    {
        var picker = Find<ComboBox>(view).Single(control =>
            AutomationProperties.GetAutomationId(control) == "normal-category-picker");
        var display = Assert.IsType<string>(picker.SelectedItem);
        return display.Split('·', 2)[0].Trim();
    }

    private static void SetOnlyExpanded(NormalCandidateView view, string category)
    {
        foreach (var pair in CategoryExpanders(view)) pair.Value.IsExpanded = pair.Key == category;
    }

    private static void Click(NormalCandidateView view, string label)
    {
        Find<Button>(view).Single(button => Equals(button.Content, label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(view, view.Width, view.Height);
    }

    private static Dictionary<string, Expander> CategoryExpanders(NormalCandidateView view) =>
        Find<Expander>(view).Where(expander => AutomationProperties.GetAutomationId(expander)
                .StartsWith("overlay-category-", StringComparison.Ordinal))
            .ToDictionary(expander => AutomationProperties.GetAutomationId(expander)["overlay-category-".Length..], StringComparer.Ordinal);

    private static string[] CandidateIds(NormalCandidateView view) => Find<Button>(view)
        .Where(button => AutomationProperties.GetAutomationId(button)
            .StartsWith("normal-candidate-", StringComparison.Ordinal))
        .Select(button => AutomationProperties.GetAutomationId(button)["normal-candidate-".Length..]).ToArray();

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static async Task Sta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception error) { done.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
