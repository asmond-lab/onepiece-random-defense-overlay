using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletOperatingBoardTests
{
    private const string BulletGoalId = "rawcode:180h";

    [Fact]
    public void ArmorContributionIsExplicitlyLegacyPlanningNotObservedTrait()
    {
        var board = BulletOperatingBoardPolicy.Evaluate(TestProfile(), KnownInput(50, 2, 2, true))!;
        var armor = board[BulletOperatingBoardFieldKind.ArmorReduction].DisplayValue;
        Assert.Contains("기존 공략의 계획 기여 +40", armor);
        Assert.Contains("실제 특성값 아님", armor);
        Assert.Equal(82, TestProfile().ExternalSlowTarget);
    }

    [Fact]
    public void NonBulletSelectedGoalRetainsGenericPlannerPresentationAndFieldOrder()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var planner = RecommendationPresentation.PlannerEvidence(10, null, false);
                var now = new StackPanel();
                var flow = new StackPanel();
                var board = new StackPanel();

                RecommendationBoard.Fill(now, flow, board, [GenericRecommendation()], [],
                    "craft:rawcode:W30h", _ => { }, plannerEvidence: planner);

                var blocks = board.Children.Cast<FrameworkElement>().ToArray();
                var plannerBlock = Assert.IsType<Border>(blocks[0]);
                Assert.Equal("WrapPanel,WrapPanel", string.Join(',',
                    blocks.Skip(1).Select(item => item.GetType().Name)));
                var ids = Descendants(plannerBlock)
                    .Select(AutomationProperties.GetAutomationId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToArray();
                Assert.Equal(planner.Fields.Skip(1).Select(field => field.AutomationId), ids);
                Assert.DoesNotContain("bullet-operating-board", ids, StringComparer.Ordinal);
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
    public void BundledProfileIsSourceBackedAndEveryCapabilityIdExistsInCatalog()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Data");
        var profile = BulletStrategyProfileLoader.LoadFromDirectory(directory);
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);

        Assert.Equal(BulletGoalId, profile.GoalUnitId);
        Assert.Equal("180h", profile.GoalRawcode);
        Assert.Equal(
            "ef69ae6ed92ab0c44085604878c7fdb3612e6c23adf6a76174946d6188596412",
            profile.ReferenceSha256);
        Assert.Equal(20, profile.BulletSlowContribution);
        Assert.Equal(40, profile.BulletArmorReductionContribution);
        Assert.All(profile.AllCapabilityUnitIds,
            id => Assert.NotEqual("이름 미등록 유닛", catalog.Unit(id).Name));
    }

    [Fact]
    public void MalformedOrIdentityDriftedProfileFailsClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "OrandOverlay-BulletProfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-unit-catalog.json"),
                Path.Combine(directory, "tmo-unit-catalog.json"));
            File.WriteAllText(Path.Combine(directory, "bullet-strategy-2314.json"),
                "{\"schemaVersion\":1,\"goalUnitId\":\"불릿\"}");

            Assert.Throws<InvalidDataException>(() =>
                BulletStrategyProfileLoader.LoadFromDirectory(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BulletSelectionUsesCanonicalSelectedOrCommittedGoalIdWithoutNameInference()
    {
        var profile = TestProfile();
        var input = BulletOperatingBoardInput.Unknown(8, BulletGoalId, null,
            "인벤토리 구성 신호가 없습니다.");

        Assert.NotNull(BulletOperatingBoardPolicy.Evaluate(profile, input));
        Assert.NotNull(BulletOperatingBoardPolicy.Evaluate(profile,
            input with { SelectedGoalId = "rawcode:other", CommittedGoalId = BulletGoalId }));
        Assert.Null(BulletOperatingBoardPolicy.Evaluate(profile,
            input with { SelectedGoalId = "불릿", CommittedGoalId = null }));
    }

    [Theory]
    [InlineData(8, 0, 0, false, BulletOperatingBoardState.EarlyFoundation)]
    [InlineData(20, 1, 0, false, BulletOperatingBoardState.AirMobility)]
    [InlineData(30, 1, 1, false, BulletOperatingBoardState.BossKill)]
    [InlineData(30, 2, 1, false, BulletOperatingBoardState.BossKill)]
    [InlineData(40, 2, 2, false, BulletOperatingBoardState.ControlArmor)]
    [InlineData(49, 2, 2, true, BulletOperatingBoardState.Ready)]
    [InlineData(50, 2, 2, true, BulletOperatingBoardState.Round50)]
    public void PurePolicyAdvancesThroughLockedBulletSequence(int round, int flying,
        int boss, bool controlsReady, BulletOperatingBoardState expected)
    {
        var input = KnownInput(round, flying, boss, controlsReady);

        var board = Assert.IsType<BulletOperatingBoard>(
            BulletOperatingBoardPolicy.Evaluate(TestProfile(), input));

        Assert.Equal(expected, board.State);
        Assert.Equal(BulletOperatingBoardFieldKind.Flying,
            board.Fields[0].Kind);
        Assert.Equal(Enum.GetValues<BulletOperatingBoardFieldKind>(),
            board.Fields.Select(field => field.Kind));
        Assert.True(board.IsRecommendationOnly);
        Assert.False(board.ClaimsRuntimeNavigationSelection);
    }

    [Fact]
    public void Story_deadline_precedes_air_and_control_after_boss_readiness()
    {
        var board = Assert.IsType<BulletOperatingBoard>(
            BulletOperatingBoardPolicy.Evaluate(
                TestProfile(),
                KnownInput(30, flying: 1, boss: 2, controlsReady: false,
                    completedStoryStage: 12)));

        Assert.Equal(BulletOperatingBoardState.StoryDeadline, board.State);
        Assert.Contains("35라운드", board.Action, StringComparison.Ordinal);
        Assert.Contains("13단계", board.Objective, StringComparison.Ordinal);
    }

    [Fact]
    public void Story_deadline_keeps_unknown_progress_explicit_after_boss_readiness()
    {
        var input = KnownInput(30, flying: 1, boss: 2, controlsReady: false) with
        {
            CompletedStoryStage = null,
            UnknownReason = "source:story-progress-missing"
        };

        var board = Assert.IsType<BulletOperatingBoard>(
            BulletOperatingBoardPolicy.Evaluate(TestProfile(), input));

        Assert.Equal(BulletOperatingBoardState.StoryDeadline, board.State);
        Assert.Contains("스토리 진행 단계 확인 불가", board.Blocker, StringComparison.Ordinal);
        Assert.DoesNotContain(input.UnknownReason, board.Blocker, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownSignalsStayExplicitAndNeverBecomeFabricatedCurrentProgress()
    {
        var board = Assert.IsType<BulletOperatingBoard>(BulletOperatingBoardPolicy.Evaluate(
            TestProfile(), BulletOperatingBoardInput.Unknown(20, BulletGoalId, null,
                "source:upgrade-missing")));

        Assert.Equal(BulletOperatingBoardState.EarlyFoundation, board.State);
        Assert.True(board[BulletOperatingBoardFieldKind.Flying].IsWarning);
        Assert.Contains("목표 스토리 12 또는 라운드 50 전 2/2",
            board[BulletOperatingBoardFieldKind.Flying].DisplayValue);
        Assert.Contains("목표 방어력 감소 → 공격 속도 → 공격력",
            board[BulletOperatingBoardFieldKind.Enhancement].DisplayValue);
        Assert.DoesNotContain("source:upgrade-missing",
            board[BulletOperatingBoardFieldKind.Enhancement].DisplayValue);
        Assert.DoesNotContain("포함되지 않습니다.",
            board[BulletOperatingBoardFieldKind.Enhancement].DisplayValue);
        Assert.DoesNotContain("source:upgrade-missing", board.Blocker, StringComparison.Ordinal);
        Assert.DoesNotContain("현재 0/2", board[BulletOperatingBoardFieldKind.Flying].DisplayValue,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BulletPrimitiveUsesStableIdsAndMatchesVisibleAccessibilityValues()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var board = Assert.IsType<BulletOperatingBoard>(
                    BulletOperatingBoardPolicy.Evaluate(TestProfile(), KnownInput(40, 2, 2, false)));
                var root = Assert.IsType<Border>(RecommendationBoard.BulletOperatingBoardBlock(board));
                Assert.Equal("bullet-operating-board", AutomationProperties.GetAutomationId(root));
                Assert.Equal("불릿 운영 안내", AutomationProperties.GetName(root));
                var rows = Descendants(root)
                    .Where(element => AutomationProperties.GetAutomationId(element)
                        .StartsWith("bullet-board-", StringComparison.Ordinal))
                    .ToArray();
                Assert.Contains(rows, row =>
                    AutomationProperties.GetAutomationId(row) == "bullet-board-navigation");
                Assert.All(rows, row =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(row)));
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetItemStatus(row)));
                });
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

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is FrameworkElement element) yield return element;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static Recommendation GenericRecommendation() => new()
    {
        Route = new RouteDefinition
        {
            Id = "craft:rawcode:W30h",
            GoalUnitId = "W30h",
            Name = "generic-goal"
        },
        RecipeProgress = new RecipeProgress
        {
            OwnedLeafCount = 0,
            RequiredLeafCount = 1,
            Leaves =
            [
                new RecipeLeafProgress
                {
                    UnitId = "300h",
                    Name = "generic-leaf",
                    RequiredCount = 1,
                    OwnedCount = 0
                }
            ]
        },
        CompositionUnits =
        [
            new CompositionUnitDetail
            {
                UnitId = "W30h",
                Name = "generic-goal",
                Tier = "히든"
            }
        ]
    };

    private static BulletStrategyProfile TestProfile() =>
        BulletStrategyProfileLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static BulletOperatingBoardInput KnownInput(int round, int flying, int boss,
        bool controlsReady, int completedStoryStage = 13) => new(
            round,
            BulletGoalId,
            null,
            round > 8,
            completedStoryStage,
            flying,
            boss,
            controlsReady ? 82 : 40,
            controlsReady ? 100 : 60,
            controlsReady ? 1.4 : 0.6,
            1.4,
            true,
            controlsReady,
            controlsReady ? "방어력 감소 강화 준비" : "강화 자원 신호 없음",
            "fixture");
}
