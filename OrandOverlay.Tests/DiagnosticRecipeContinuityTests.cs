using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;
using Xunit.Abstractions;

namespace OrandOverlay.Tests;

public sealed class DiagnosticRecipeContinuityTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Started = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task FreshnessExpiryKeepsRecommendationCardsAndSelectedRecipeVisibleAsLastKnown() => Sta(() =>
    {
        var fixture = new Fixture();
        var before = Capture(fixture);
        Assert.NotEmpty(before.Steps);
        Assert.NotNull(before.Recommendation);
        fixture.Now = fixture.Observation.StartedAt.Add(DiagnosticInventoryObservation.FreshnessBudget);
        fixture.Render();
        var expired = Capture(fixture);
        output.WriteLine(JsonSerializer.Serialize(new { Before = before, Expired = expired }, new JsonSerializerOptions { WriteIndented = true }));

        Assert.False(expired.Current);
        Assert.Null(fixture.Reference());
        Assert.Empty(fixture.Model.CurrentInventory);
        Assert.Equal(before.Selected, expired.Selected);
        Assert.Equal(before.Session, expired.Session);
        Assert.Contains(fixture.Model.Snapshot.Groups.SelectMany(group => group.Candidates),
            candidate => candidate.Allocation is not null && candidate.Unit.Id == before.Selected);
        Assert.Null(DiagnosticReferenceStats.From(fixture.Observation, false).ObservedCount);
        Assert.Equal(Started, fixture.Observation.StartedAt);
        Assert.True(before.Steps.SequenceEqual(expired.Steps),
            $"Unchanged hand lost its selected recipe at 3000ms: steps {before.Steps.Length}->{expired.Steps.Length}; " +
            $"selection={expired.Selected}, session={expired.Session}, current={expired.Current}.");
        Assert.Equal(before.Cards, expired.Cards);
        Assert.Equal(before.Recommendation, expired.Recommendation);
        Assert.Contains(expired.Text, text => text.Contains("마지막") || text.Contains("이전 인식"));
        Assert.Contains(expired.Text, text => text.Contains("현재") && (text.Contains("확인 중") || text.Contains("미확인")));
    });

    [Fact]
    public Task SameHandNextFreshRevisionKeepsSelectionAndRecipeWithoutRenewingOldTimestamp() => Sta(() =>
    {
        var fixture = new Fixture();
        var before = Capture(fixture);
        fixture.Now = Started.AddSeconds(4);
        fixture.Render();
        Assert.False(fixture.Model.Snapshot.IsCurrent);
        var next = fixture.CreateObservation(2, fixture.Now);
        fixture.Now = next.CompletedAt;
        Assert.True(fixture.Accept(next));
        fixture.Render();
        var recovered = Capture(fixture);
        Assert.True(recovered.Current);
        Assert.Equal(before.Session, recovered.Session);
        Assert.Equal(before.Selected, recovered.Selected);
        Assert.Equal(before.Steps, recovered.Steps);
        Assert.Equal(before.Cards, recovered.Cards);
        Assert.Equal(before.Recommendation, recovered.Recommendation);
        Assert.Equal(Started, fixture.Observation.StartedAt);
        Assert.False(fixture.Accept(fixture.Observation));
    });

    [Fact]
    public Task ConfirmedNewSessionDiscardsSelectedRecipeAndLastRecommendation() => Sta(() =>
    {
        var fixture = new Fixture();
        var previous = Capture(fixture);
        Assert.False(fixture.References.TryAcceptFull(new RecognitionResult
        {
            State = RecognitionState.Waiting, ConfirmsSessionBoundary = true
        }, fixture.Now, "2.320", fixture.Fingerprint, out _));
        fixture.Model.InvalidateReference(resetContext: true);
        fixture.View.SetModel(fixture.Model);
        var reset = Capture(fixture);
        Assert.Null(reset.Selected);
        Assert.Null(reset.Recommendation);
        Assert.Empty(reset.Steps);
        Assert.False(reset.Current);
        Assert.True(reset.Session > previous.Session);
        Assert.Empty(fixture.Model.CurrentInventory);
    });

    private sealed class Fixture
    {
        internal readonly DataCatalog Catalog = new();
        internal readonly DiagnosticInventoryPresentationState References = new();
        internal readonly NormalCandidateBrowser Model;
        internal readonly NormalCandidateView View;
        internal readonly DiagnosticInventoryObservation Observation;
        internal DateTimeOffset Now = Started.AddMilliseconds(10);
        internal string Fingerprint => Catalog.OfflineBundle!.Fingerprint;

        internal Fixture()
        {
            Catalog.Load(mapVersion: "2.320");
            Model = new NormalCandidateBrowser(Catalog.AllUnits) { ReferencePresentationIsValid = () => Reference() is not null };
            View = new NormalCandidateView { IsMainWorkspace = true, Width = 1000, Height = 700 };
            View.SetCraftPlanner(new NormalCraftPlanner(Catalog));
            Observation = CreateObservation(1, Started);
            Assert.True(Accept(Observation));
            Model.UpdateReference(Observation, 1);
            Model.SetReferenceStage(NormalCandidateStage.Rare);
            var goal = Model.Snapshot.Groups.SelectMany(group => group.Candidates).First(Model.CanHighlight);
            Model.Select(goal.Unit.Id);
            Render();
        }

        internal DiagnosticInventoryObservation CreateObservation(long revision, DateTimeOffset start) =>
            DiagnosticInventoryObservation.Create(Catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
                new string('A', 64), revision, 0, start, start.AddMilliseconds(10), TimeSpan.FromMilliseconds(10),
                [new() { UnitId = "rawcode:I10h", Count = 2 }], [], 1, new string('C', 64), new string('B', 64));
        internal bool Accept(DiagnosticInventoryObservation observation) => References.TryAcceptFull(new RecognitionResult
        {
            State = RecognitionState.Ready, DiagnosticObservation = observation,
            Diagnostics = new() { Source = DiagnosticInventoryObservation.SourceName,
                ProcessVersion = Warcraft300Diagnostic.Version, ExecutableSha256 = Warcraft300Diagnostic.Hash }
        }, Now, "2.320", Fingerprint, out _);
        internal IDiagnosticInventoryReference? Reference() => References.Current(Now, "2.320", Fingerprint);
        internal void Render()
        {
            if (Reference() is { } current) Model.UpdateReference(current, 1);
            else Model.InvalidateReference();
            View.SetModel(Model);
            View.Measure(new Size(1000, 700));
            View.Arrange(new Rect(0, 0, 1000, 700));
            View.UpdateLayout();
        }
    }

    private sealed record Presentation(bool Current, long Session, string? Selected, string? Recommendation,
        string[] Cards, string[] Steps, string[] Text);
    private static Presentation Capture(Fixture fixture)
    {
        var nodes = Descendants(fixture.View).OfType<FrameworkElement>().ToArray();
        string[] Ids(string prefix) => nodes.Select(AutomationProperties.GetAutomationId)
            .Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        return new(fixture.Model.Snapshot.IsCurrent, fixture.Model.SessionRevision, fixture.Model.SelectedUnitId,
            fixture.View.DisplayedRecommendationId, Ids("normal-candidate-"), Ids("normal-craft-step-"),
            nodes.OfType<TextBlock>().Select(text => text.Text).ToArray());
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var node in Descendants(child)) yield return node;
    }
    private static async Task Sta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); done.SetResult(); } catch (Exception error) { done.SetException(error); } })
            { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
