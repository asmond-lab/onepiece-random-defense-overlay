using OrandOverlay;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BoardSelectionTests
{
    [Fact]
    public void JinbeZeroWithAllLeavesPlansJinbeBeforeMagellan()
    {
        var (recommendation, plan) = MagellanWithMissingJinbePlan();

        Assert.Equal("100%", RecommendationPresentation.CompletionPercent(
            recommendation.RecipeProgress));
        Assert.Equal("rawcode:810h", plan[0].TargetUnitId);
        Assert.Equal("징베 - 특별함", plan[0].TargetName);
        Assert.DoesNotContain(plan.Take(1), step =>
            step.TargetUnitId.Equals("rawcode:Z10h", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VisibleClusterChildWinsWhenMainRecommendationSharesRouteId()
    {
        var main = Recommendation("craft:rawcode:W30h", owned: 2, required: 8);
        var child = Recommendation("craft:rawcode:W30h", owned: 8, required: 9);

        var selected = BoardSelection.Resolve([main], [child], child.Route.Id);

        Assert.Same(child, selected);
        Assert.Equal(1, Assert.Single(
            RecommendationPresentation.BoardMissingLeaves(
                selected!.RecipeProgress, preferCommons: true)).MissingCount);
    }

    [Fact]
    public void WpfBoardShowsSelectedChildPercentAndItsMissingCommonCount()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var main = Recommendation("craft:rawcode:W30h", owned: 2, required: 8);
                var child = Recommendation("craft:rawcode:W30h", owned: 8, required: 9);
                var now = new StackPanel();
                var flow = new StackPanel();
                var board = new StackPanel();

                RecommendationBoard.Fill(
                    now, flow, board, [main], [], child.Route.Id, _ => { },
                    selectedChildren: [child], clusterHeadId: main.Route.Id);

                Assert.Contains("89%", Texts(now));
                Assert.DoesNotContain("25%", Texts(now));
                Assert.Contains("부족 ×1", Texts(board));
                Assert.DoesNotContain("부족 ×6", Texts(board));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void WpfCurrentCraftUsesFirstPlannedTargetForVisibleAndAccessibleValues()
    {
        var (recommendation, plan) = MagellanWithMissingJinbePlan();

        AssertCurrentCraftProjection(recommendation, plan, "마젤란 - 희귀함");
    }

    [Fact]
    public void FirstRareCurrentCardShowsSelectedRareWhileFlowShowsImmediateAceStep()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var recommendation = Recommendation("craft:vander-decken", 14, 15,
                    "rawcode:vander-decken", "반더데켄", "희귀함");
                recommendation.RemainingCraftSteps.Add(new RecipeCraftStep
                {
                    UnitId = "rawcode:ace-uncommon",
                    Name = "에이스",
                    Tier = "안흔함",
                    RequiredCount = 1,
                    OwnedCount = 1,
                    CompletionRatio = 1
                });
                var plan = new[]
                {
                    new AutoCombineStep("rawcode:ace-uncommon", "에이스 - 안흔함",
                        "rawcode:brook", "브룩", "B00H", "Z", ["Z"])
                };
                var evidence = RecommendationPresentation.PlannerEvidence(1, null, false,
                    storySequence: new StoryRewardSequenceDecision(
                        RecommendationSequenceStage.FirstRare,
                        StorySequenceAction.FindFirstRare, "1단계", "첫 희귀함",
                        "빠른 완성", "반더데켄을 완성하세요.", "1 첫 희귀함 [현재]",
                        null, null, 0, false));
                var now = new StackPanel();
                var flow = new StackPanel();
                var board = new StackPanel();

                RecommendationBoard.Fill(now, flow, board, [recommendation], plan,
                    recommendation.Route.Id, _ => { }, plannerEvidence: evidence,
                    showRouteRootAsCurrentCraft: true);

                var elements = Descendants(now).OfType<FrameworkElement>().ToArray();
                var title = elements.Single(element =>
                    AutomationProperties.GetAutomationId(element) == "current-craft-title");
                var icon = elements.Single(element =>
                    AutomationProperties.GetAutomationId(element) == "current-craft-icon");
                var progress = elements.Single(element =>
                    AutomationProperties.GetAutomationId(element) == "current-craft-progress");
                var currentCraft = elements.Single(element =>
                    AutomationProperties.GetAutomationId(element) == "current-craft");
                Assert.Equal("반더데켄 - 희귀함", Assert.IsType<TextBlock>(title).Text);
                Assert.Equal("반더데켄 - 희귀함", AutomationProperties.GetName(icon));
                Assert.Equal("93%", Assert.IsType<TextBlock>(progress).Text);
                Assert.Equal("반더데켄 - 희귀함 · 진행률 93%",
                    AutomationProperties.GetItemStatus(currentCraft));
                Assert.Contains("에이스", JoinedText(flow), StringComparison.Ordinal);
                Assert.Contains("에이스 - 안흔함", JoinedText(now), StringComparison.Ordinal);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void RedForceRecipeAndBoardPlanBuggyMagitanBeforeMissingShanks()
    {
        var (recommendation, plan) = RedForceWithMissingShanksPlan();

        Assert.Equal("100%", RecommendationPresentation.CompletionPercent(
            recommendation.RecipeProgress));
        Assert.Contains(recommendation.RecipeTree!.Children, child =>
            child.UnitId.Equals("rawcode:530h", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("rawcode:510h", plan[0].TargetUnitId);
        Assert.Equal("버기 마기탄 - 특별함", plan[0].TargetName);
        AssertCurrentCraftProjection(recommendation, plan, "레드포스호 - 해적선");
    }

    [Fact]
    public void FirstLegendBoardHidesIngredientCardsAndDoesNotClaimHandRecognitionIsWaiting()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var main = Recommendation("craft:legend", owned: 0, required: 2,
                    goalUnitId: "legend", name: "목표 전설");
                var child = Recommendation("craft:rare", owned: 0, required: 1,
                    goalUnitId: "rare", name: "희귀 재료");
                var decision = new StoryRewardSequenceDecision(
                    RecommendationSequenceStage.FirstLegend,
                    StorySequenceAction.BuildNearestLegend,
                    "8 마린포드", "희귀위습 1", "보상 확인 완료",
                    "목표 전설을 조합하세요.", "3 첫 전설 [현재]",
                    "legend", "목표 전설", 0, false);
                var evidence = RecommendationPresentation.PlannerEvidence(
                    22, null, false, storySequence: decision);
                var now = new StackPanel();
                var flow = new StackPanel();
                var board = new StackPanel();

                RecommendationBoard.Fill(
                    now, flow, board, [main], [], main.Route.Id, _ => { },
                    selectedChildren: [child], clusterHeadId: main.Route.Id,
                    plannerEvidence: evidence);

                Assert.Contains("목표 전설", JoinedText(board), StringComparison.Ordinal);
                Assert.Contains(AutomationNames(board), name =>
                    name.Contains("목표 전설", StringComparison.Ordinal));
                Assert.DoesNotContain(AutomationNames(board), name =>
                    name.Contains("희귀 재료", StringComparison.Ordinal));

                RecommendationBoard.Fill(
                    now, flow, board, [], [], null, _ => { },
                    banner: "8 마린포드 · 희귀위습 결과를 확인하세요.",
                    plannerEvidence: evidence);

                Assert.Contains("현재 단계 진행 중", JoinedText(now), StringComparison.Ordinal);
                Assert.Contains("8 마린포드", JoinedText(now), StringComparison.Ordinal);
                Assert.DoesNotContain("패 인식 대기 중", JoinedText(now), StringComparison.Ordinal);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IReadOnlyList<string> Texts(DependencyObject root)
    {
        var result = new List<string>();
        Walk(root);
        return result;

        void Walk(DependencyObject node)
        {
            if (node is TextBlock text) result.Add(text.Text);
            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
                 index++)
                Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, index));
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
             index++)
            foreach (var child in Descendants(
                         System.Windows.Media.VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }

    private static void AssertCurrentCraftProjection(Recommendation recommendation,
        IReadOnlyList<AutoCombineStep> plan, string finalGoal)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var now = new StackPanel();
                var flow = new StackPanel();
                var board = new StackPanel();
                RecommendationBoard.Fill(now, flow, board, [recommendation], plan,
                    recommendation.Route.Id, _ => { });

                var elements = Descendants(now).OfType<FrameworkElement>().ToArray();
                var title = elements.Single(element =>
                    AutomationProperties.GetAutomationId(element) == "current-craft-title");
                var icon = elements.Single(element =>
                    AutomationProperties.GetAutomationId(element) == "current-craft-icon");
                var progress = elements.Single(element =>
                    AutomationProperties.GetAutomationId(element) == "current-craft-progress");
                var currentCraft = elements.Single(element =>
                    AutomationProperties.GetAutomationId(element) == "current-craft");
                Assert.Equal(plan[0].TargetName, Assert.IsType<TextBlock>(title).Text);
                Assert.Equal(plan[0].TargetName, AutomationProperties.GetName(icon));
                Assert.Equal("100%", Assert.IsType<TextBlock>(progress).Text);
                Assert.Equal("100%", AutomationProperties.GetItemStatus(progress));
                Assert.Equal("현재 추천", AutomationProperties.GetName(currentCraft));
                Assert.Equal($"{plan[0].TargetName} · 진행률 100%",
                    AutomationProperties.GetItemStatus(currentCraft));
                if (!plan[0].TargetUnitId.Equals(
                        recommendation.CompositionUnits[0].UnitId,
                        StringComparison.OrdinalIgnoreCase))
                    foreach (var finalAbility in RecommendationPresentation.NowAbilityLines(
                                 recommendation.CompositionUnits[0]))
                        Assert.DoesNotContain(finalAbility, Texts(now));
                Assert.Contains(AutomationNames(board), name =>
                    name.Contains(finalGoal, StringComparison.Ordinal));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string JoinedText(DependencyObject root) =>
        string.Join('\n', Texts(root)).Replace("\u2060", "", StringComparison.Ordinal);

    private static IReadOnlyList<string> AutomationNames(DependencyObject root)
    {
        var result = new List<string>();
        Walk(root);
        return result;

        void Walk(DependencyObject node)
        {
            if (node is FrameworkElement element &&
                System.Windows.Automation.AutomationProperties.GetName(element) is { Length: > 0 } name)
                result.Add(name.Replace("\u2060", "", StringComparison.Ordinal));
            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
                 index++)
                Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, index));
        }
    }

    private static Recommendation Recommendation(string routeId, long owned, long required,
        string goalUnitId = "rawcode:W30h", string name = "베르고 히든",
        string tier = "히든") =>
        new()
        {
            Route = new RouteDefinition
            {
                Id = routeId,
                GoalUnitId = goalUnitId,
                Name = name
            },
            RecipeProgress = new RecipeProgress
            {
                OwnedLeafCount = owned,
                RequiredLeafCount = required,
                Leaves =
                [
                    new RecipeLeafProgress
                    {
                        UnitId = "rawcode:H00h",
                        Name = "루피",
                        Tier = "흔함",
                        OwnedCount = owned,
                        RequiredCount = required
                    }
                ]
            },
            CompositionUnits =
            [
                new CompositionUnitDetail
                {
                    UnitId = goalUnitId,
                    Name = name,
                    Tier = tier
                }
            ]
        };

    private static (Recommendation Recommendation, IReadOnlyList<AutoCombineStep> Plan)
        MagellanWithMissingJinbePlan()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(
            AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var inventory = new[] { "K00h", "A00h", "500h", "510h", "J10h" }
            .Select(rawcode => new InventoryEntry
            {
                UnitId = "rawcode:" + rawcode,
                Count = 1
            })
            .ToList();
        var recommendation = new RecommendationEngine(catalog, combineHotkeys: hotkeys)
            .RecommendFastRares(inventory, 500)
            .Single(item => item.Route.GoalUnitId.Equals(
                "rawcode:Z10h", StringComparison.OrdinalIgnoreCase));
        var plan = new AutoCombinePlanner(catalog, hotkeys).Plan([recommendation], inventory);

        Assert.DoesNotContain(inventory, entry =>
            entry.UnitId.Equals("rawcode:810h", StringComparison.OrdinalIgnoreCase));
        return (recommendation, plan);
    }

    private static (Recommendation Recommendation, IReadOnlyList<AutoCombineStep> Plan)
        RedForceWithMissingShanksPlan()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(
            AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var inventory = new[]
        {
            new InventoryEntry { UnitId = "rawcode:500h", Count = 6 },
            new InventoryEntry { UnitId = "rawcode:U00h", Count = 1 },
            new InventoryEntry { UnitId = "rawcode:Z00h", Count = 1 },
            new InventoryEntry { UnitId = "rawcode:S10h", Count = 1 },
            new InventoryEntry { UnitId = "rawcode:I20h", Count = 1 },
            new InventoryEntry { UnitId = "rawcode:060h", Count = 1 }
        };
        var recommendation = new RecommendationEngine(catalog, combineHotkeys: hotkeys)
            .RecommendNearestCrafts("rawcode:U30h", inventory, 32)
            .Single(item => item.Route.GoalUnitId.Equals(
                "rawcode:U30h", StringComparison.OrdinalIgnoreCase));
        var plan = new AutoCombinePlanner(catalog, hotkeys).Plan([recommendation], inventory);

        Assert.DoesNotContain(inventory, entry =>
            entry.UnitId.Equals("rawcode:530h", StringComparison.OrdinalIgnoreCase));
        return (recommendation, plan);
    }
}
