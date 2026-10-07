using Xunit;

namespace OrandOverlay.Tests;

public sealed class BDesignFidelityTests
{
    private static string Source(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(name);
    }
    [Fact]
    public void MainUsesBoundedSegmentAndIndependentObservationHeading()
    {
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("Width=\"430\" MaxWidth=\"430\"", xaml);
        Assert.Contains("x:Name=\"MainObservationStatus\"", xaml);
        Assert.Contains("Text=\"조합 계획\"", xaml);
        Assert.Contains("IsMainWorkspace=\"True\"", xaml);
        Assert.Contains("{x:Static local:RandyPickTheme.Canvas}", xaml);
        Assert.Contains("{x:Static local:RandyPickTheme.Surface}", xaml);
        Assert.Contains("Data=\"{TemplateBinding Tag}\"", xaml);
        foreach (var icon in new[] { "BPlanIcon", "BInventoryIcon", "BJournalIcon", "BProfileIcon", "BSettingsIcon" })
            Assert.Contains("<Geometry x:Key=\"" + icon + "\">", xaml);
        Assert.DoesNotContain("FontFamily=\"Segoe MDL2 Assets\"", xaml);
        Assert.Contains("main-plan-navigation", xaml);
    }
    [Fact]
    public void CandidateSuggestionSelectionAndSourceDisclosureAreSeparate()
    {
        var source = Source("NormalCandidateView.cs") + Source("NormalCandidateView.Craft.cs") + Source("NormalCandidateView.Progression.cs");
        Assert.Contains("Where(CanHighlightForBrowsing)", source);
        Assert.Contains("_banner.WithOverlayBackground(recommendation is null ? OverlayChrome.WellKey : OverlayChrome.RaisedKey)", source);
        Assert.Contains("_banner.BorderBrush = recommendation is null ? RandyPickTheme.Border : RandyPickTheme.StrongBorder", source);
        Assert.Contains("BorderBrush = chosen ? RandyPickTheme.SelectionBorder : nearby ? RandyPickTheme.StrongBorder : Line", source);
        Assert.Contains("normal-category-", source);
        Assert.Contains("전체 카테고리", source);
        Assert.Contains("능력·조합 조건과 출처", source);
        Assert.Contains("role.Condition", source);
        Assert.Contains("_craftPlanner?.Build(unit.Id, IsFresh ? _model.CurrentInventory : _model.LastKnownInventory)", source);
        Assert.Contains("foreach (var role in _model!.RolesFor(unit.Id))", source);
        Assert.Contains("ability.DisplayValue", source);
        Assert.Contains("groupContent.Children.Add(Action(\"후보 더 보기\"", source);
        Assert.Contains("IsMainWorkspace ? Accent : Gold", source);
        Assert.Contains("CollapsedCategories.Contains(category)", source);
    }
    [Fact]
    public void CoachUsesBalancedColumnsAndDoesNotPresentAbsentPlansAsZero()
    {
        var layout = Source("BeginnerCoachView.xaml.cs");
        Assert.Contains("new GridLength(split ? 1.2 : 1, GridUnitType.Star)", layout);
        Assert.Contains("split ? new GridLength(1, GridUnitType.Star)", layout);
        Assert.Contains("plan.Components.Count == 0 ? \"미확인\"", Source("CoachPlanView.Main.cs"));
        Assert.Contains("plan.Components.Count == 0 ? \"재료 계획 확인 대기\"", Source("CoachPlanView.Main.cs"));
    }
    [Fact]
    public void CoachRetainsGuideOwnershipFenceAndActualControls()
    {
        var source = Source("BeginnerCoachView.Main.cs");
        Assert.Contains("ReferenceEquals(GuideChoice.Parent, oldHeader)", source);
        Assert.Contains("RestyleMainGoalAndMaterials", source);
        Assert.Contains("ConfirmButton.Background = RandyPickTheme.Accent", source);
        Assert.Contains("ConfirmButton.Foreground = RandyPickTheme.Canvas", source);
        Assert.Contains("goal.WithOverlayBackground(OverlayChrome.RaisedKey)", source);
        Assert.Contains("plan-component:", source);
    }
}
