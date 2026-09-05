using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static class Program
{
    private const string LongUnknownReason =
        "상대 상위 유닛 수와 첫 전설 이력이 아직 확정되지 않아 마지막으로 안전했던 추천을 유지합니다. " +
        "신호가 안정되면 하한·평균·상한과 회복 가능성을 다시 계산합니다.";

    [STAThread]
    private static int Main(string[] args)
    {
        var output = Argument(args, "--output") ?? throw new ArgumentException("--output is required.");
        Directory.CreateDirectory(output);
        var buildSha = Argument(args, "--build-sha") ?? GitHead();
        var sourceFingerprint = SourceFingerprint();
        var app = new App { SkipRuntimeStartup = true };
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var rows = new List<EvidenceRow>();
        var bulletRows = new List<BulletEvidenceRow>();
        var currentCraftRows = new List<CurrentCraftEvidenceRow>();
        var captureValidationRows = new List<CaptureValidationRow>();
        var cases = Cases();
        var captures = CaptureVariants()
            .Select(variant =>
                (cases.Single(item => item.State == variant.State), variant.Scale))
            .ToList();
        foreach (var (fixture, scale) in captures)
            Capture(output, buildSha, sourceFingerprint, fixture, scale, rows,
                captureValidationRows);
        var bulletCaptures = BulletCases().Select(item => (item, scale: 1.0))
            .Concat([
                (BulletCases().Single(item => item.State == BulletOperatingBoardState.ControlArmor), 1.25),
                (BulletCases().Single(item => item.State == BulletOperatingBoardState.Ready), 1.5)
            ])
            .ToList();
        foreach (var (fixture, scale) in bulletCaptures)
            CaptureBullet(output, buildSha, sourceFingerprint, fixture, scale, bulletRows);
        var currentCraftCaptures = new[] { 0.75, 1.0, 1.25, 1.5 };
        foreach (var scale in currentCraftCaptures)
        {
            CaptureCurrentCraft(output, buildSha, sourceFingerprint, scale, currentCraftRows,
                redForce: false);
            CaptureCurrentCraft(output, buildSha, sourceFingerprint, scale, currentCraftRows,
                redForce: true);
            CaptureCurrentCraft(output, buildSha, sourceFingerprint, scale, currentCraftRows,
                redForce: false, firstRare: true);
        }
        var statsAdvice = CaptureSpecialAdvice(output, buildSha, sourceFingerprint);
        var telemetrySettings = CaptureTelemetrySettings(
            output, buildSha, sourceFingerprint);
        var mainPendingRecommendation = CaptureMainPendingRecommendation(
            output, buildSha, sourceFingerprint);

        var matrixPath = Path.Combine(output, "planner-ui-evidence-matrix.json");
        var generatedAtUtc = DateTimeOffset.UtcNow;
        File.WriteAllText(matrixPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            kind = "desktop-automation-transcript",
            surface = "desktop",
            tool = "PlannerEvidenceCapture",
            actions = new[]
            {
                new
                {
                    type = "custom",
                    timestamp = generatedAtUtc,
                    selector = "automation-id=candidate-board-scroll",
                    target = "WPF recommendation fixtures"
                },
                new
                {
                    type = "custom",
                    timestamp = generatedAtUtc,
                    selector = "automation-name=식별자 없는 플레이 집계 전송",
                    target = "WPF telemetry disclosure settings"
                }
            },
            assertions = new[]
            {
                new
                {
                    timestamp = generatedAtUtc,
                    selector = "automation-id=candidate-board-scroll",
                    status = "passed"
                },
                new
                {
                    timestamp = generatedAtUtc,
                    selector = "automation-name=식별자 없는 플레이 집계 전송",
                    status = "passed"
                }
            },
            generatedAtUtc,
            buildSha,
            sourceFingerprint,
            shell = new { designWidth = 540, designHeight = 740, soleVerticalScrollOwner = "candidate-board-scroll" },
            fixtures = captures.Count,
            bulletFixtures = bulletCaptures.Count,
            currentCraftFixtures = currentCraftCaptures.Length * 3,
            statsAdvice,
            telemetrySettings,
            mainPendingRecommendation,
            pendingCaptureValidation = captureValidationRows,
            rows,
            bulletRows,
            currentCraftRows
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"WPF_PLANNER_EVIDENCE PASS captures={(captures.Count + bulletCaptures.Count + currentCraftCaptures.Length * 3) * 2} rows={rows.Count} bulletRows={bulletRows.Count} currentCraftRows={currentCraftRows.Count} matrix={matrixPath}");
        app.Shutdown();
        return 0;
    }

    private static void CaptureCurrentCraft(string output, string buildSha,
        string sourceFingerprint, double scale, List<CurrentCraftEvidenceRow> rows,
        bool redForce, bool firstRare = false)
    {
        var scaleName = (scale * 100).ToString("0", CultureInfo.InvariantCulture);
        var fixtureName = firstRare
            ? "first-rare-vander-ace-step"
            : redForce ? "red-force-missing-shanks" : "jinbe-zero";
        var topPath = Path.Combine(output, $"current-craft-{fixtureName}-{scaleName}-top.png");
        var bottomPath = Path.Combine(output, $"current-craft-{fixtureName}-{scaleName}-bottom.png");
        var (recommendation, plan) = firstRare
            ? FirstRareVanderWithAcePlan()
            : redForce ? RedForceWithMissingShanksPlan() : MagellanWithMissingJinbePlan();
        var expected = firstRare
            ? RecommendationPresentation.CraftUnitName(recommendation.CompositionUnits[0])
            : plan[0].TargetName;
        var expectedProgress = firstRare ? "93%" : "100%";
        var finalGoal = RecommendationPresentation.CraftUnitName(
            recommendation.CompositionUnits[0]);
        var window = new OverlayWindow
        {
            Topmost = false,
            ShowActivated = false,
            Opacity = 0,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop
        };
        window.SetClickThrough(true);
        window.Render(recommendation.CompositionUnits[0].Name, [recommendation], EmptyStats(),
            [], [], false, plan, redForce
                ? "인식 정상 · 샹크스 직접 보유 0 · 재귀 재료 충족"
                : firstRare
                    ? "인식 정상 · 첫 희귀함 반 더 데켄 진행 유지"
                    : "인식 정상 · 징베 직접 보유 0 · 재귀 재료 충족",
            showRouteRootAsCurrentCraft: firstRare);
        if (firstRare)
            window.RenderPlannerEvidence(1, null, storySequence: new StoryRewardSequenceDecision(
                RecommendationSequenceStage.FirstRare, StorySequenceAction.FindFirstRare,
                "1단계", "첫 희귀함", "빠른 완성", "반 더 데켄을 완성하세요.",
                "1 첫 희귀함 [현재]", null, null, 0, false));
        window.UpdateStatus("인식 정상 · 현재 추천은 첫 실행 가능 조합");
        window.Show();
        window.Dispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.Render);
        window.Left = SystemParameters.VirtualScreenLeft - 1024;
        window.Opacity = 1;
        ApplyScale(window, scale);
        var scroll = (ScrollViewer?)window.FindName("BoardScrollViewer")
                     ?? throw new InvalidOperationException("candidate board scroll owner missing");
        scroll.ScrollToHome();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.Render);
        CapturePng(window, topPath, scale);
        scroll.ScrollToEnd();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.Render);
        CapturePng(window, bottomPath, scale);
        ValidateCapture(topPath, 540 * scale, 740 * scale);
        ValidateCapture(bottomPath, 540 * scale, 740 * scale);
        ValidateFooter(window);

        var elements = Descendants(window).OfType<FrameworkElement>().ToArray();
        if (elements.Count(element => AutomationProperties.GetAutomationId(element) ==
                                      "candidate-board-scroll") != 1)
            throw new InvalidOperationException(
                "Current-craft fixture must retain one candidate-board scroller.");
        var currentCraft = elements.Single(element =>
            AutomationProperties.GetAutomationId(element) == "current-craft");
        var title = elements.OfType<TextBlock>().Single(element =>
            AutomationProperties.GetAutomationId(element) == "current-craft-title");
        var icon = elements.Single(element =>
            AutomationProperties.GetAutomationId(element) == "current-craft-icon");
        var progress = elements.OfType<TextBlock>().Single(element =>
            AutomationProperties.GetAutomationId(element) == "current-craft-progress");
        var finalGoalVisible = elements.Any(element =>
            AutomationProperties.GetName(element).Contains(
                finalGoal, StringComparison.Ordinal));
        var visibleText = Descendants(currentCraft).OfType<TextBlock>()
            .Select(text => text.Text).ToArray();
        var leakedFinalAbility = RecommendationPresentation.NowAbilityLines(
                recommendation.CompositionUnits[0])
            .Any(visibleText.Contains);
        var flowPanel = (Panel?)window.FindName("FlowPanel")
                        ?? throw new InvalidOperationException("craft flow panel missing");
        var flowText = string.Join(' ', Descendants(flowPanel).OfType<TextBlock>()
            .Select(text => text.Text));
        if (title.Text != expected || !firstRare && title.Text.Contains(finalGoal, StringComparison.Ordinal) ||
            title.TextWrapping != TextWrapping.Wrap ||
            AutomationProperties.GetName(currentCraft) != "현재 추천" ||
            AutomationProperties.GetItemStatus(currentCraft) != $"{expected} · 진행률 {expectedProgress}" ||
            AutomationProperties.GetName(icon) != expected || progress.Text != expectedProgress ||
            AutomationProperties.GetItemStatus(progress) != expectedProgress ||
            !finalGoalVisible || !firstRare && leakedFinalAbility ||
            firstRare && !flowText.Contains("에이스", StringComparison.Ordinal) ||
            title.ActualHeight <= 0 ||
            title.ActualHeight > title.MaxHeight + 0.5)
            throw new InvalidOperationException(
                "Current-craft visible/accessibility/final-goal/CJK contract mismatch.");
        rows.Add(new CurrentCraftEvidenceRow(firstRare
                ? "first-rare-vander-93-ace-step"
                : redForce
                    ? "red-force-missing-shanks-all-leaves-present"
                    : "jinbe-zero-all-leaves-present", expected,
            AutomationProperties.GetName(currentCraft),
            AutomationProperties.GetItemStatus(currentCraft), finalGoal,
            firstRare ? 93 : 100,
            96 * scale, scale, topPath, bottomPath, buildSha, sourceFingerprint,
            "PASS"));
        window.Stats.CloseForApplication();
        window.CloseForApplication();
    }

    private static (Recommendation Recommendation, IReadOnlyList<AutoCombineStep> Plan)
        FirstRareVanderWithAcePlan()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var vander = catalog.Unit("rawcode:T10h");
        var ace = catalog.Unit("rawcode:K00h");
        var recommendation = new Recommendation
        {
            Route = new RouteDefinition
            {
                Id = "craft:" + vander.Id,
                GoalUnitId = vander.Id,
                Name = vander.Name
            },
            RecipeProgress = new RecipeProgress
            {
                OwnedLeafCount = 14,
                RequiredLeafCount = 15,
                Leaves =
                [
                    new RecipeLeafProgress
                    {
                        UnitId = "rawcode:H00h",
                        Name = "몽키.D.루피",
                        Tier = "흔함",
                        OwnedCount = 14,
                        RequiredCount = 15
                    }
                ]
            },
            CompositionUnits =
            [
                new CompositionUnitDetail
                {
                    UnitId = vander.Id,
                    Name = vander.Name,
                    Tier = vander.Tier,
                    Image = vander.Image
                }
            ],
            RemainingCraftSteps =
            [
                new RecipeCraftStep
                {
                    UnitId = ace.Id,
                    Name = ace.Name,
                    Tier = ace.Tier,
                    Image = ace.Image,
                    RequiredCount = 1,
                    OwnedCount = 1,
                    CompletionRatio = 1
                }
            ]
        };
        IReadOnlyList<AutoCombineStep> plan =
        [
            new(ace.Id, RecommendationPresentation.CraftUnitName(ace.Name, ace.Tier),
                "rawcode:H00h", "몽키.D.루피", "H00h", "Z", ["Z"])
        ];
        return (recommendation, plan);
    }

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
        if (recommendation.RecipeProgress.CompletionRatio != 1 || plan.Count == 0 ||
            !plan[0].TargetUnitId.Equals("rawcode:810h", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Jinbe-zero fixture no longer resolves the intended planner state.");
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
        if (recommendation.RecipeProgress.CompletionRatio != 1 || plan.Count == 0 ||
            !plan[0].TargetUnitId.Equals("rawcode:510h", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Red Force fixture no longer resolves the intended planner state.");
        return (recommendation, plan);
    }

    private static void CaptureBullet(string output, string buildSha, string sourceFingerprint,
        BulletFixture fixture, double scale, List<BulletEvidenceRow> rows)
    {
        var scaleName = (scale * 100).ToString("0", CultureInfo.InvariantCulture);
        var stateName = fixture.State.ToString()
            .Replace("EarlyFoundation", "early-foundation", StringComparison.Ordinal)
            .Replace("AirMobility", "air-mobility", StringComparison.Ordinal)
            .Replace("BossKill", "boss-kill", StringComparison.Ordinal)
            .Replace("ControlArmor", "control-armor", StringComparison.Ordinal)
            .Replace("Round50", "round50", StringComparison.Ordinal)
            .ToLowerInvariant();
        var stem = $"bullet-{stateName}-{scaleName}";
        var topPath = Path.Combine(output, stem + "-top.png");
        var bottomPath = Path.Combine(output, stem + "-bottom.png");
        var profile = BulletStrategyProfileLoader.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Data"));
        var expected = BulletOperatingBoardPolicy.Evaluate(profile, fixture.Input)
                       ?? throw new InvalidOperationException("Bullet fixture did not resolve.");
        if (expected.State != fixture.State)
            throw new InvalidOperationException($"Bullet fixture mismatch: {fixture.State} != {expected.State}");

        var window = new OverlayWindow
        {
            Topmost = false,
            ShowActivated = false,
            Opacity = 0,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop
        };
        window.SetClickThrough(true);
        window.Render("Bullet", [BulletCard(fixture)], fixture.Stats, [], [],
            fixture.Input.GreenBloodKnown == true, [], "인식 정상",
            greenBloodUsed: fixture.Input.GreenBloodKnown == true,
            stunTarget: fixture.Input.StunTarget,
            inventory: BulletInventory(fixture));
        window.RenderPlannerEvidence(fixture.Input.Round, null, false, null, null,
            fixture.Input.CompletedStoryStage);
        window.UpdateStatus("인식 정상 · Bullet 운영 보드 표시 중");
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        window.Left = SystemParameters.VirtualScreenLeft - 1024;
        window.Opacity = 1;
        ApplyScale(window, scale);
        var scroll = (ScrollViewer?)window.FindName("BoardScrollViewer")
                     ?? throw new InvalidOperationException("candidate board scroll owner missing");
        scroll.ScrollToHome();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        CapturePng(window, topPath, scale);
        scroll.ScrollToEnd();
        FlushRender(window);
        CapturePng(window, bottomPath, scale);
        ValidateCapture(topPath, 540 * scale, 740 * scale);
        ValidateCapture(bottomPath, 540 * scale, 740 * scale);
        ValidateFooter(window);
        var coreRows = ValidateBulletAutomationTree(window, expected);
        foreach (var row in coreRows)
            rows.Add(new BulletEvidenceRow(stateName, row.ControlId, "Core",
                row.DisplayValue, row.AccessibilityName, row.AccessibilityValue,
                96 * scale, scale, topPath, bottomPath, buildSha, sourceFingerprint, "PASS"));
        foreach (var field in expected.Fields)
            rows.Add(new BulletEvidenceRow(stateName, field.AutomationId, field.Kind.ToString(),
                field.DisplayValue, field.AccessibilityName, field.AccessibilityValue,
                96 * scale, scale, topPath, bottomPath, buildSha, sourceFingerprint, "PASS"));
        window.Stats.CloseForApplication();
        window.CloseForApplication();
    }

    private static IReadOnlyList<StatsAdviceEvidence> CaptureSpecialAdvice(
        string output, string buildSha, string sourceFingerprint)
    {
        var fixtures = new[]
        {
            (Name: "none", Advice: (IReadOnlyList<SpecialDismantleAdvice>)
                [new("keep", "로브 루치", false, "핵심 재료")], Expected: (string?)null),
            (Name: "dismantle", Advice: (IReadOnlyList<SpecialDismantleAdvice>)
                [new("keep", "로브 루치", false, "핵심 재료"),
                 new("break", "마가렛", true, "경로 밖")], Expected: "마가렛 분해")
        };
        var evidence = new List<StatsAdviceEvidence>();
        foreach (var fixture in fixtures)
        {
            var path = Path.Combine(output, $"stats-special-{fixture.Name}.png");
            var owner = new OverlayWindow();
            owner.Render("비비 영원 · 연금술", [], EmptyStats(), [], [], false, [],
                "인식 정상", specialAdvice: fixture.Advice);
            var stats = owner.Stats;
            stats.Topmost = false;
            stats.ShowActivated = false;
            stats.Opacity = 0;
            stats.Left = SystemParameters.VirtualScreenLeft;
            stats.Top = SystemParameters.VirtualScreenTop;
            stats.Show();
            stats.Dispatcher.Invoke(() => { },
                System.Windows.Threading.DispatcherPriority.Render);
            stats.Left = SystemParameters.VirtualScreenLeft - 1024;
            stats.Opacity = 1;
            stats.Width = 228;
            stats.Height = 700;
            var scroll = Descendants(stats).OfType<ScrollViewer>().Single();
            scroll.ScrollToEnd();
            stats.UpdateLayout();
            stats.Dispatcher.Invoke(() => { },
                System.Windows.Threading.DispatcherPriority.Render);
            CaptureStatsPng(stats, path);
            ValidateCapture(path, 228, 700);
            ValidateDismantleOnly(stats, fixture.Expected);
            evidence.Add(new StatsAdviceEvidence(fixture.Name, fixture.Expected, path,
                buildSha, sourceFingerprint, "PASS"));
            stats.CloseForApplication();
            owner.CloseForApplication();
        }
        return evidence;
    }

    private static TelemetrySettingsEvidence CaptureTelemetrySettings(
        string output, string buildSha, string sourceFingerprint)
    {
        var path = Path.Combine(output, "telemetry-privacy-settings.png");
        var queueDirectory = Path.Combine(output, "telemetry-fixture-queue");
        var window = new MainWindow(new AppSettings
        {
            AutoScanEnabled = false,
            ClearDataAutoRefresh = false,
            TelemetryEnabled = true,
            TelemetryDisclosureVersion = 0
        }, startRuntime: false, telemetryQueueDirectory: queueDirectory)
        {
            ShowActivated = false,
            Topmost = false,
            Opacity = 0,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop
        };
        window.Show();
        window.Dispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.Render);
        var scroll = (ScrollViewer?)window.FindName("SettingsScrollViewer")
                     ?? throw new InvalidOperationException(
                         "Settings scroll owner missing.");
        scroll.ScrollToEnd();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.Render);

        var telemetry = (CheckBox?)window.FindName("TelemetryCheck")
                        ?? throw new InvalidOperationException(
                            "Telemetry opt-out control missing.");
        var disclosure = (Border?)window.FindName("TelemetryDisclosurePanel")
                         ?? throw new InvalidOperationException(
                             "Telemetry disclosure panel missing.");
        var disclosureText = string.Join(' ', Descendants(disclosure)
            .OfType<TextBlock>().Select(item => item.Text));
        if (telemetry.IsChecked != true ||
            disclosure.Visibility != Visibility.Visible ||
            !disclosureText.Contains("유닛·rawcode·설치/세션 ID", StringComparison.Ordinal) ||
            !disclosureText.Contains("화면·OCR 원문", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Telemetry disclosure or identifier exclusions are not visible.");

        window.Opacity = 1;
        CaptureFrameworkElementPng(scroll, path);
        ValidateCapture(path, scroll.ActualWidth, scroll.ActualHeight);
        window.Close();
        if (Directory.Exists(queueDirectory))
            Directory.Delete(queueDirectory, recursive: true);
        return new TelemetrySettingsEvidence(
            "automatic-default-disclosure-opt-out",
            AutomationProperties.GetName(telemetry),
            disclosureText,
            path,
            buildSha,
            sourceFingerprint,
            "PASS");
    }

    private static MainPendingRecommendationEvidence CaptureMainPendingRecommendation(
        string output, string buildSha, string sourceFingerprint)
    {
        var path = Path.Combine(output, "main-pending-recommendation-100.png");
        var contextPath = Path.Combine(
            output, "main-pending-recommendation-context-100.png");
        var window = new MainWindow(new AppSettings
        {
            AutoScanEnabled = false,
            ClearDataAutoRefresh = false
        }, startRuntime: false)
        {
            ShowActivated = false,
            Topmost = false,
            Opacity = 0,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop
        };
        var sequence = Sequence(RecommendationSequenceStage.RareReward,
            StorySequenceAction.PushStoryForRareReward,
            "스토리 7", "골드 6,000 · 목재 4 · 희귀위습 2",
            "남은 희귀위습은 실제 결과를 본 뒤 상위 가치로 계산합니다.",
            "스토리를 밀어 희귀위습 보상을 받은 뒤 상위 확정을 진행하세요.");
        var view = RecommendationPresentation.PlannerEvidence(
            14, null, false, storySequence: sequence);
        RecommendationBoard.Fill(window.NowPanel, window.FlowPanel, window.BoardPanel,
            [], [], null, _ => { }, plannerEvidence: view);
        window.Show();
        window.Dispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.Render);
        window.Left = SystemParameters.VirtualScreenLeft - 1280;
        window.Opacity = 1;
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.Render);
        var root = (FrameworkElement)window.Content;
        var recommendationSurface = (FrameworkElement?)window.NowPanel.Parent
                                    ?? throw new InvalidOperationException(
                                        "Main recommendation surface missing.");
        CaptureFrameworkElementPng(recommendationSurface, path);
        CaptureFrameworkElementPng(root, contextPath);
        ValidateCapture(
            path, recommendationSurface.ActualWidth, recommendationSurface.ActualHeight);
        ValidateCapture(contextPath, root.ActualWidth, root.ActualHeight);
        ValidatePendingRecommendationState(recommendationSurface, view);
        window.Close();
        return new MainPendingRecommendationEvidence(
            path, contextPath, "main-current-craft-well",
            buildSha, sourceFingerprint, "PASS");
    }

    private static void Capture(string output, string buildSha, string sourceFingerprint,
        Fixture fixture, double scale, List<EvidenceRow> rows,
        List<CaptureValidationRow> captureValidationRows)
    {
        var scaleName = (scale * 100).ToString("0", CultureInfo.InvariantCulture);
        var stem = $"planner-{StateName(fixture.State)}-{scaleName}";
        var topPath = Path.Combine(output, stem + "-top.png");
        var bottomPath = Path.Combine(output, stem + "-bottom.png");
        var view = RecommendationPresentation.PlannerEvidence(fixture.Round, fixture.Applied,
            fixture.Unknown, fixture.Unknown ? LongUnknownReason : null,
            fixture.Sequence, fixture.CurrentManualLatches);
        if (view.State != fixture.State)
            throw new InvalidOperationException($"Fixture state mismatch: {fixture.State} != {view.State}");

        var window = new OverlayWindow
        {
            Topmost = false,
            ShowActivated = false,
            Opacity = 0,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop
        };
        window.SetClickThrough(true);
        window.Render("플래너 검증", [], EmptyStats(), [], [], false, [], "인식 정상");
        if (fixture.State == PlannerEvidenceState.ManualOverride)
        {
            var catalog = new DataCatalog();
            catalog.Load();
            var zoro = catalog.Unit("rawcode:F90H");
            var inventory = zoro.Recipe.Where(pair =>
                    !catalog.Unit(pair.Key).Name.Contains("쿠마", StringComparison.Ordinal))
                .Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
            var rec = new RecommendationEngine(catalog).RecommendNearestCrafts(zoro.Id, inventory, 32)
                .Single(item => item.Route.GoalUnitId == zoro.Id);
            window.Render("조로 · 바운티헌터", [rec], EmptyStats(), [], [], false, [],
                "검증 fixture · 초월쿠마 획득 대기", inventory: inventory);
        }
        if (fixture.State == PlannerEvidenceState.SequenceFirstLegend)
            window.Render("쿠마 전설", [Card("legend", "쿠마 전설", "전설")],
                EmptyStats(), [], [], false, [], "인식 정상",
                storyChildren: _ => [Card("rare", "희귀 재료", "희귀함")]);
        window.RenderPlannerEvidence(fixture.Round, fixture.Applied, fixture.Unknown,
            fixture.Unknown ? LongUnknownReason : null, fixture.Sequence, null,
            fixture.CurrentManualLatches);
        window.UpdateStatus("인식 정상 · 플래너 근거 표시 중");
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        window.Left = SystemParameters.VirtualScreenLeft - 1024;
        window.Opacity = 1;
        ApplyScale(window, scale);
        var scroll = (ScrollViewer?)window.FindName("BoardScrollViewer")
                     ?? throw new InvalidOperationException("candidate board scroll owner missing");
        scroll.ScrollToHome();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        CapturePng(window, topPath, scale);

        scroll.ScrollToEnd();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        CapturePng(window, bottomPath, scale);
        var isPendingCapture =
            fixture.State is PlannerEvidenceState.SequenceStoryReward or
                PlannerEvidenceState.SequenceRareReward or
                PlannerEvidenceState.SequenceTopNavigation;
        var topMetrics = ValidateCapture(
            topPath, 540 * scale, 740 * scale, isPendingCapture);
        var bottomMetrics = ValidateCapture(
            bottomPath, 540 * scale, 740 * scale, isPendingCapture);
        ValidateFooter(window);
        ValidateAutomationTree(window, view);
        if (fixture.State == PlannerEvidenceState.ManualOverride)
        {
            var text = string.Join(" ", Descendants(window).OfType<TextBlock>().Select(item => item.Text))
                .Replace("\u2060", "", StringComparison.Ordinal);
            if (!text.Contains("수동 목표 유지", StringComparison.Ordinal) ||
                !text.Contains("초월쿠마", StringComparison.Ordinal) ||
                !text.Contains("조로", StringComparison.Ordinal) ||
                !text.Contains("자동 판단 조건", StringComparison.Ordinal))
                throw new InvalidOperationException("Manual Zoro status, missing Kuma or automatic evidence lost.");
        }
        if (fixture.State == PlannerEvidenceState.SequenceFirstLegend)
            ValidateFirstLegendCards(window);
        if (isPendingCapture)
        {
            ValidatePendingRecommendationState(window, view);
            ValidateKoreanTextLayout((FrameworkElement)window.Content);
            captureValidationRows.Add(new CaptureValidationRow(
                fixture.State.ToString(), scale, "top", topPath,
                topMetrics!.Value, "PASS", "PASS"));
            captureValidationRows.Add(new CaptureValidationRow(
                fixture.State.ToString(), scale, "bottom", bottomPath,
                bottomMetrics!.Value, "PASS", "PASS"));
        }
        if (fixture.State == PlannerEvidenceState.Unknown)
            ValidateKoreanWrap(window);

        foreach (var field in view.Fields)
            rows.Add(new EvidenceRow(fixture.State.ToString(), field.AutomationId, field.Kind.ToString(),
                field.DisplayValue, field.MachineValue, field.AccessibilityName,
                field.AccessibilityValue, 96 * scale, scale, fixture.KoreanFixture,
                topPath, bottomPath, buildSha, sourceFingerprint, "PASS"));

        if (fixture.State == PlannerEvidenceState.Round21Actionable && Math.Abs(scale - 1) < 0.001)
            File.Copy(topPath, Path.Combine(output,
                "task-18-round-20-adaptive-build-routing.png"), overwrite: true);
        window.Stats.CloseForApplication();
        window.CloseForApplication();
    }

    private static void ApplyScale(OverlayWindow window, double scale)
    {
        var root = (FrameworkElement)window.Content;
        root.LayoutTransform = Math.Abs(scale - 1) < 0.001
            ? Transform.Identity
            : new ScaleTransform(scale, scale);
        window.Width = 540 * scale;
        window.Height = 740 * scale;
        window.UpdateLayout();
    }

    private static void CapturePng(OverlayWindow window, string path, double scale)
    {
        var root = (FrameworkElement)window.Content;
        var width = checked((int)Math.Round(540 * scale));
        var height = checked((int)Math.Round(740 * scale));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        SaveEvidencePng(bitmap, path);
    }

    private static void CaptureFrameworkElementPng(FrameworkElement root, string path)
    {
        var width = Math.Max(1, checked((int)Math.Round(root.ActualWidth)));
        var height = Math.Max(1, checked((int)Math.Round(root.ActualHeight)));
        var bitmap = new RenderTargetBitmap(
            width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        SaveEvidencePng(bitmap, path);
    }

    private static void FlushRender(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.Loaded);
        window.Dispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.Render);
        window.UpdateLayout();
    }

    private static void CaptureStatsPng(StatsOverlayWindow window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap(228, 700, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        SaveEvidencePng(bitmap, path);
    }

    private static void SaveEvidencePng(RenderTargetBitmap bitmap, string path)
    {
        var stride = checked(bitmap.PixelWidth * 4);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);
        CapturePixelContract.FlattenOntoEvidenceBackground(
            pixels, bitmap.PixelWidth, bitmap.PixelHeight, stride);
        var opaque = BitmapSource.Create(
            bitmap.PixelWidth, bitmap.PixelHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(opaque));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static CapturePixelMetrics? ValidateCapture(
        string path, double expectedWidth, double expectedHeight,
        bool validatePixels = false)
    {
        var bytes = File.ReadAllBytes(path);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature))
            throw new InvalidDataException($"Invalid PNG signature: {path}");
        var width = ReadBigEndian(bytes.AsSpan(16, 4));
        var height = ReadBigEndian(bytes.AsSpan(20, 4));
        if (width != (int)Math.Round(expectedWidth) || height != (int)Math.Round(expectedHeight))
            throw new InvalidDataException($"Unexpected PNG dimensions: {path} {width}x{height}");
        using var stream = File.OpenRead(path);
        var decoded = new PngBitmapDecoder(
            stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        var frame = new FormatConvertedBitmap(
            decoded, PixelFormats.Bgra32, null, 0);
        var stride = checked(frame.PixelWidth * 4);
        var pixels = new byte[checked(stride * frame.PixelHeight)];
        frame.CopyPixels(pixels, stride, 0);
        if (pixels.Where((_, index) => index % 4 == 3)
            .Any(alpha => alpha != byte.MaxValue))
            throw new InvalidDataException($"Capture alpha is not opaque: {path}");
        return validatePixels
            ? CapturePixelContract.Validate(
                pixels, frame.PixelWidth, frame.PixelHeight, stride)
            : null;
    }

    private static int ReadBigEndian(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static void ValidateFooter(OverlayWindow window)
    {
        var root = (FrameworkElement)window.Content;
        foreach (var name in new[] { "StatusText", "OverlayVersionText" })
        {
            var element = (FrameworkElement?)window.FindName(name)
                          ?? throw new InvalidOperationException($"Missing footer element {name}");
            var bounds = element.TransformToAncestor(root)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            if (element.Visibility != Visibility.Visible ||
                element.ActualWidth <= 0 || element.ActualHeight <= 0 ||
                bounds.Bottom > root.ActualHeight + 0.5 || bounds.Top < -0.5)
                throw new InvalidOperationException($"Footer escaped shell: {name} {bounds}");
        }
    }

    private static void ValidateAutomationTree(DependencyObject root, PlannerEvidenceView view)
    {
        var ids = Descendants(root).OfType<FrameworkElement>()
            .Select(AutomationProperties.GetAutomationId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        foreach (var field in view.Fields)
            if (!ids.Contains(field.AutomationId, StringComparer.Ordinal))
                throw new InvalidOperationException($"Missing AutomationId {field.AutomationId}");
        if (ids.Count(id => id == "candidate-board-scroll") != 1)
            throw new InvalidOperationException("Candidate board must own one vertical scroll surface.");
    }

    private static IReadOnlyList<BulletCoreEvidence> ValidateBulletAutomationTree(
        DependencyObject root,
        BulletOperatingBoard board)
    {
        var elements = Descendants(root).OfType<FrameworkElement>().ToArray();
        var ids = elements.Select(AutomationProperties.GetAutomationId)
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (ids.Count(id => id == "candidate-board-scroll") != 1 ||
            ids.Count(id => id == "bullet-operating-board") != 1)
            throw new InvalidOperationException(
                "Bullet board must remain inside the sole candidate-board scroll surface.");
        var required = new[]
        {
            "bullet-board-title", "bullet-board-phase", "bullet-board-round",
            "bullet-board-confidence", "bullet-board-action", "bullet-board-objective",
            "bullet-board-route-1", "bullet-board-focus", "bullet-board-gate"
        }.Concat(board.Fields.Select(field => field.AutomationId));
        foreach (var id in required)
            if (!ids.Contains(id, StringComparer.Ordinal))
                throw new InvalidOperationException($"Missing Bullet AutomationId {id}");
        var coreExpected = new[]
        {
            ("bullet-board-phase", board.Phase),
            ("bullet-board-round", board.Round.ToString(CultureInfo.InvariantCulture)),
            ("bullet-board-confidence", board.Confidence),
            ("bullet-board-action", board.Action),
            ("bullet-board-objective", board.Objective),
            ("bullet-board-focus", board.Focus),
            ("bullet-board-gate", board.Gate)
        }.Concat(board.Routes.Select((route, index) =>
            ($"bullet-board-route-{index + 1}", route)));
        var coreEvidence = new List<BulletCoreEvidence>();
        foreach (var (id, expected) in coreExpected)
        {
            var row = elements.Single(element =>
                AutomationProperties.GetAutomationId(element) == id);
            var visible = row is TextBlock text
                ? text
                : Descendants(row).OfType<TextBlock>().Last();
            var visibleValue = visible.Text.Replace("\u2060", "", StringComparison.Ordinal);
            var accessibilityValue = AutomationProperties.GetItemStatus(row);
            if (visibleValue != expected || accessibilityValue != expected)
                throw new InvalidOperationException(
                    $"Bullet core visible/accessibility value mismatch for {id}");
            coreEvidence.Add(new BulletCoreEvidence(
                id, visibleValue, AutomationProperties.GetName(row), accessibilityValue));
        }
        foreach (var field in board.Fields)
        {
            var row = elements.Single(element =>
                AutomationProperties.GetAutomationId(element) == field.AutomationId);
            var visible = Descendants(row).OfType<TextBlock>().Last();
            var visibleValue = visible.Text.Replace("\u2060", "", StringComparison.Ordinal);
            if (visibleValue != field.DisplayValue ||
                AutomationProperties.GetName(row) != field.AccessibilityName ||
                AutomationProperties.GetItemStatus(row) != field.AccessibilityValue ||
                visible.TextWrapping != TextWrapping.Wrap ||
                visible.TextTrimming != TextTrimming.None)
                throw new InvalidOperationException(
                    $"Bullet visible/accessibility value mismatch for {field.AutomationId}");
        }
        return coreEvidence;
    }

    private static void ValidateKoreanWrap(DependencyObject root)
    {
        var row = Descendants(root).OfType<FrameworkElement>().Single(element =>
            AutomationProperties.GetAutomationId(element) == "planner-unknown-signals");
        var value = Descendants(row).OfType<TextBlock>().Last();
        if (value.Text.Replace("\u2060", string.Empty, StringComparison.Ordinal)
                .Replace('\u00A0', ' ') != LongUnknownReason ||
            !value.Text.Contains('\u2060') || value.TextWrapping != TextWrapping.WrapWithOverflow ||
            !value.Text.Contains(
                "회\u2060복\u00A0가\u2060능\u2060성\u2060을\u00A0다\u2060시\u00A0계\u2060산\u2060합\u2060니\u2060다.",
                StringComparison.Ordinal) ||
            AutomationProperties.GetItemStatus(row) != LongUnknownReason ||
            value.TextTrimming != TextTrimming.None || value.ActualHeight <= value.FontSize * 1.5)
            throw new InvalidOperationException("Long Korean unknown reason did not wrap safely.");
    }

    private static void ValidateFirstLegendCards(DependencyObject root)
    {
        var names = Descendants(root).OfType<FrameworkElement>()
            .Select(AutomationProperties.GetName)
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (!names.Any(value => value.Contains("쿠마 - 전설", StringComparison.Ordinal)) ||
            names.Any(value => value.Contains("희귀 재료", StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "First-legend surface must render only the target legendary card.");
    }

    private static void ValidatePendingRecommendationState(
        DependencyObject root, PlannerEvidenceView view)
    {
        var elements = Descendants(root).OfType<FrameworkElement>().ToArray();
        var card = elements.Single(element =>
            AutomationProperties.GetAutomationId(element) ==
            "pending-recommendation-card");
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pending-recommendation-status"] = "상위 추천 준비 중",
            ["pending-recommendation-action"] =
                view[PlannerEvidenceFieldKind.Action].DisplayValue,
            ["pending-recommendation-reason"] =
                "희귀 보상 결과가 상위 경로를 바꿀 수 있어 결과를 먼저 반영합니다.",
            ["pending-recommendation-resume"] =
                "희귀위습 결과가 반영되면 상위·항법 추천이 자동으로 다시 표시됩니다."
        };
        foreach (var (id, expected) in values)
        {
            var row = elements.Single(element =>
                AutomationProperties.GetAutomationId(element) == id);
            var visible = row is TextBlock text
                ? text
                : Descendants(row).OfType<TextBlock>().Last();
            if (visible.Text.Replace("\u2060", "", StringComparison.Ordinal) != expected ||
                AutomationProperties.GetItemStatus(row) != expected ||
                visible.TextWrapping != TextWrapping.Wrap ||
                visible.TextTrimming != TextTrimming.None ||
                visible.ActualWidth <= 0 || visible.ActualHeight <= 0)
                throw new InvalidOperationException(
                    $"Pending recommendation visible/accessibility mismatch for {id}.");
        }
        if (AutomationProperties.GetName(card) != "추천은 계속됩니다" ||
            AutomationProperties.GetItemStatus(card) != "상위 추천 준비 중")
            throw new InvalidOperationException(
                "Pending recommendation card hierarchy mismatch.");
    }

    private static void ValidateKoreanTextLayout(FrameworkElement root)
    {
        foreach (var text in Descendants(root).OfType<TextBlock>()
                     .Where(item => item.IsVisible &&
                                    item.Text.Any(character =>
                                        character is >= '\uAC00' and <= '\uD7A3')))
        {
            var bounds = text.TransformToAncestor(root).TransformBounds(
                new Rect(0, 0, text.ActualWidth, text.ActualHeight));
            var desiredOverflow = text.TextWrapping == TextWrapping.NoWrap &&
                                  text.DesiredSize.Width >
                                  text.ActualWidth + 0.5;
            if (text.TextTrimming != TextTrimming.None ||
                text.ActualWidth <= 0 || text.ActualHeight < text.FontSize ||
                bounds.Left < -0.5 || bounds.Right > root.ActualWidth + 0.5 ||
                desiredOverflow)
                throw new InvalidOperationException(
                    $"Korean text is clipped: {text.Text}; " +
                    $"actual={text.ActualWidth:0.##}x{text.ActualHeight:0.##}; " +
                    $"desired={text.DesiredSize.Width:0.##}x{text.DesiredSize.Height:0.##}; " +
                    $"bounds={bounds}; wrapping={text.TextWrapping}; " +
                    $"trimming={text.TextTrimming}");
        }
    }

    private static void ValidateDismantleOnly(StatsOverlayWindow window, string? expected)
    {
        var visibleText = Descendants(window).OfType<TextBlock>()
            .Where(item => item.IsVisible)
            .Select(item => item.Text)
            .ToArray();
        if (visibleText.Any(text => text.Contains("유지", StringComparison.Ordinal)))
            throw new InvalidOperationException("Keep advice must not be rendered.");
        if (expected is null)
        {
            if (window.SpecialHeader.Visibility != Visibility.Collapsed ||
                window.SpecialPanel.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException(
                    "The special section must collapse when no dismantle action exists.");
            return;
        }
        if (window.SpecialHeader.Visibility != Visibility.Visible ||
            window.SpecialPanel.Visibility != Visibility.Visible ||
            !visibleText.Contains(expected, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Only the actionable dismantle chip must remain visible.");
    }

    private static Recommendation Card(string id, string name, string tier) => new()
    {
        Route = new RouteDefinition
        {
            Id = "craft:" + id,
            GoalUnitId = id,
            Name = name
        },
        RecipeProgress = new RecipeProgress(),
        CompositionUnits =
        [
            new CompositionUnitDetail
            {
                UnitId = id,
                Name = name,
                Tier = tier
            }
        ]
    };

    private static Recommendation BulletCard(BulletFixture fixture)
    {
        var units = new List<CompositionUnitDetail>
        {
            new()
            {
                UnitId = "rawcode:180h", Name = "불릿", Tier = "불멸 [물딜]",
                IsGoal = true, IsRequired = true,
                RequiredCount = 1, SuggestedCount = 1,
                OwnedCount = fixture.Input.BulletCrafted == true ? 1 : 0
            }
        };
        if (fixture.Input.FirstLegendFoundationKnown == true)
            units.Add(new CompositionUnitDetail
            {
                UnitId = "rawcode:530h", Name = "샹크스", Tier = "전설 [스턴]",
                SuggestedCount = 1, OwnedCount = 1
            });
        var flyingIds = new[] { "rawcode:K30h", "rawcode:U20h" };
        for (var index = 0; index < fixture.Input.FlyingCapableLegendCount.GetValueOrDefault(); index++)
            units.Add(new CompositionUnitDetail
            {
                UnitId = flyingIds[index], Name = index == 0 ? "쵸파 유력강화" : "검은수염",
                Tier = "전설 [마딜]", SuggestedCount = 1, OwnedCount = 1
            });
        return new Recommendation
        {
            Route = new RouteDefinition
            {
                Id = "craft:rawcode:180h", GoalUnitId = "rawcode:180h", Name = "Bullet"
            },
            RecipeProgress = new RecipeProgress { RequiredLeafCount = 1 },
            CompositionUnits = units
        };
    }

    private static IReadOnlyList<InventoryEntry> BulletInventory(BulletFixture fixture)
    {
        var entries = new List<InventoryEntry>();
        if (fixture.Input.FirstLegendFoundationKnown == true)
            entries.Add(new InventoryEntry { UnitId = "rawcode:530h", Count = 1 });
        var flyingIds = new[] { "rawcode:K30h", "rawcode:U20h" };
        for (var index = 0; index < fixture.Input.FlyingCapableLegendCount.GetValueOrDefault(); index++)
            entries.Add(new InventoryEntry { UnitId = flyingIds[index], Count = 1 });
        var bossIds = new[] { "rawcode:B90H", "rawcode:V90h" };
        for (var index = 0; index < fixture.Input.BossKillUnitCount.GetValueOrDefault(); index++)
            entries.Add(new InventoryEntry { UnitId = bossIds[index], Count = 1 });
        if (fixture.Input.BulletCrafted == true)
            entries.Add(new InventoryEntry { UnitId = "rawcode:180h", Count = 1 });
        return entries;
    }

    private static InventoryStatSummary EmptyStats() => new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

    private static IReadOnlyList<Fixture> Cases() =>
    [
        new(PlannerEvidenceState.SequenceFirstRare, 4, null, false,
            "첫 희귀함 빠른 완성",
            Sequence(RecommendationSequenceStage.FirstRare,
                StorySequenceAction.FindFirstRare,
                "스토리 2", "골드 800 · 흔함선택위습 3",
                "첫 희귀함을 가장 빠른 완성 순으로 계산합니다.",
                "가장 가까운 희귀함부터 완성하세요.")),
        new(PlannerEvidenceState.SequenceStoryReward, 8, null, false,
            "현재 보상을 확인한 뒤 다시 계산",
            Sequence(RecommendationSequenceStage.StoryReward,
                StorySequenceAction.WaitForStoryReward,
                "스토리 4", "골드 2,000 · 목재 1 · 특별위습 2 · 안흔위습 1",
                "쿠마 전설 결손 기대 31.4% 감소 · 특별 유효 4/33 · 안흔 유효 2/13",
                "현재 스토리 클리어 보상을 확인한 뒤 전설 조합을 다시 계산하세요.",
                "rawcode:fixture-legend", "쿠마 전설", 3140)),
        new(PlannerEvidenceState.SequenceFirstLegend, 10, null, false,
            "첫 전설로 스토리 속도 확보",
            Sequence(RecommendationSequenceStage.FirstLegend,
                StorySequenceAction.CraftLegendNow,
                "스토리 5", "골드 3,000 · 목재 1 · 특별위습 2 · 안흔위습 2",
                "즉시 완성 가능 · 보상보다 스토리 속도 우선",
                "쿠마 전설을 지금 조합해 스토리 속도를 확보하세요.",
                "rawcode:fixture-legend", "쿠마 전설", 10000)),
        new(PlannerEvidenceState.SequenceRareReward, 14, null, false,
            "희귀위습 실제 결과 대기",
            Sequence(RecommendationSequenceStage.RareReward,
                StorySequenceAction.PushStoryForRareReward,
                "스토리 7", "골드 6,000 · 목재 4 · 희귀위습 2",
                "남은 희귀위습은 실제 결과를 본 뒤 상위 가치로 계산합니다.",
                "스토리를 밀어 희귀위습 보상을 받은 뒤 상위 확정을 진행하세요.")),
        new(PlannerEvidenceState.SequenceTopNavigation, 19, null, false,
            "상위·항법 추천 준비",
            Sequence(RecommendationSequenceStage.TopAndNavigation,
                StorySequenceAction.WaitForRound20,
                "어인섬 · 스토리 9", "클리어 보상 반영 완료",
                "희귀 보상 결과를 반영해 상위·항법 후보를 계산합니다.",
                "20라운드까지 현재 패를 유지하며 상위 후보를 계속 갱신합니다.")),
        new(PlannerEvidenceState.Round20Preview, 20,
            Applied(NavigationRecommendationState.Provisional, PlannerPhase.CommitRound20),
            false, "20라운드 상위·항법 갱신",
            Sequence(RecommendationSequenceStage.TopAndNavigation,
                StorySequenceAction.WaitForRound20,
                "어인섬 · 스토리 9", "클리어 보상 확인 중",
                "스토리 희귀 보상 사용 결과가 현재 패에 반영됐습니다.",
                "20라운드까지 현재 패를 유지하며 상위 후보를 계속 갱신합니다.")
            with { TopNavigationUnlocked = true }),
        new(PlannerEvidenceState.Waiting, 10, null, false, "입력 대기 중"),
        new(PlannerEvidenceState.Blocked, 10,
            Applied(NavigationRecommendationState.NoSafeRecommendation,
                PlannerPhase.AwaitMarineford, [AdaptiveBuildBlocker.AwaitingMarineford]),
            false, "마린포드 진행을 기다립니다"),
        new(PlannerEvidenceState.Round21Actionable, 21,
            Applied(NavigationRecommendationState.Actionable, PlannerPhase.Committed),
            false, "직접 항법을 선택하세요"),
        new(PlannerEvidenceState.Committed, 22,
            Applied(NavigationRecommendationState.Locked, PlannerPhase.Committed),
            false, "추천 고정 상태"),
        new(PlannerEvidenceState.ManualOverride, 19,
            Applied(NavigationRecommendationState.NoSafeRecommendation, PlannerPhase.CommitRound20,
                [AdaptiveBuildBlocker.UnknownStoryStage, AdaptiveBuildBlocker.UnknownRareWisps]),
            true, "수동 목표 유지", CurrentManualLatches: new ManualLatches(true, false)),
        new(PlannerEvidenceState.Round24Forced, 24,
            Applied(NavigationRecommendationState.SourceExpectedForced, PlannerPhase.Committed,
                forced: "AlliedForces.DoubleBenefit"), false, "원본 규칙 기대값"),
        new(PlannerEvidenceState.Unknown, 20, null, true, LongUnknownReason)
    ];

    internal static IReadOnlyList<(PlannerEvidenceState State, double Scale)>
        CaptureVariants()
    {
        var baseVariants = Cases().Select(item => (item.State, Scale: 1.0));
        var pendingStates = new[]
        {
            PlannerEvidenceState.SequenceStoryReward,
            PlannerEvidenceState.SequenceRareReward,
            PlannerEvidenceState.SequenceTopNavigation
        };
        var pendingScaleVariants = pendingStates.SelectMany(state =>
            new[] { 0.75, 1.25, 1.5 }.Select(scale => (state, scale)));
        var actionableScaleVariants = new[] { 0.75, 1.25, 1.5 }.Select(scale =>
            (PlannerEvidenceState.Round21Actionable, scale));
        return baseVariants.Concat(pendingScaleVariants)
            .Concat(actionableScaleVariants)
            .Concat(new[] { 0.75, 1.25, 1.5 }.Select(scale =>
                (PlannerEvidenceState.ManualOverride, scale))).ToArray();
    }

    private static IReadOnlyList<BulletFixture> BulletCases()
    {
        const string unknownReason = "현재 인식 입력에 강화 단계 신호가 없습니다.";
        BulletFixture Case(BulletOperatingBoardState state, int round, bool foundation,
            int flying, int boss, double slow, double armor, double stun,
            bool crafted = false, int completedStoryStage = 13)
        {
            var input = new BulletOperatingBoardInput(round, "rawcode:180h", null,
                foundation, completedStoryStage, flying, boss, slow, armor, stun,
                1.4, true, crafted, null,
                unknownReason);
            var stats = EmptyStats() with
            {
                Stun = stun,
                Slow = slow,
                ArmorReduction = armor,
                AirMovementProviders = flying,
                SingleDamageProviders = boss
            };
            return new BulletFixture(state, input, stats);
        }

        return
        [
            Case(BulletOperatingBoardState.EarlyFoundation, 8, false, 0, 0, 0, 0, 0),
            Case(BulletOperatingBoardState.AirMobility, 20, true, 1, 0, 20, 30, 0.3),
            Case(BulletOperatingBoardState.BossKill, 30, true, 2, 1, 40, 60, 0.6),
            Case(BulletOperatingBoardState.StoryDeadline, 30, true, 1, 2, 40, 60,
                0.6, completedStoryStage: 12),
            Case(BulletOperatingBoardState.ControlArmor, 40, true, 2, 2, 40, 60, 0.6),
            Case(BulletOperatingBoardState.Ready, 49, true, 2, 2, 82, 100, 1.4),
            Case(BulletOperatingBoardState.Round50, 50, true, 2, 2, 82, 100, 1.4, true)
        ];
    }

    private static StoryRewardSequenceDecision Sequence(
        RecommendationSequenceStage stage,
        StorySequenceAction action,
        string story,
        string reward,
        string value,
        string decision,
        string? legendId = null,
        string? legendName = null,
        int expectedBp = 0)
    {
        var labels = new[]
        {
            "첫 희귀함", "스토리 보상", "첫 전설", "희귀 보상", "상위+항법"
        };
        var steps = string.Join('\n', labels.Select((label, index) =>
            $"{index + 1} {label} [{(index < (int)stage ? "완료" : index == (int)stage ? "현재" : "대기")}]"));
        return new StoryRewardSequenceDecision(stage, action, story, reward, value,
            decision, steps, legendId, legendName, expectedBp, false);
    }

    private static AdaptivePlanningApplied Applied(NavigationRecommendationState navigationState,
        PlannerPhase phase, ImmutableArray<AdaptiveBuildBlocker> blockers = default,
        ManualLatches manual = default, string? forced = null)
    {
        var route = new AdaptiveRouteLockCandidate(DamageLane.Physical,
            "top_physical", "package_control", 6400, true);
        var state = AdaptiveBuildState.Initial(1) with
        {
            Phase = phase, LockedFirstLegendId = "legend_anchor", RouteLock = route,
            NavigationLockId = navigationState == NavigationRecommendationState.Locked
                ? "PathOfKings.BountyHunter" : null, ManualLatches = manual
        };
        var option = new NavigationIntervalOptionScore(
            "PathOfKings.BountyHunter", 1, 8200, NavigationRiskPosture.FloorDefense, 900,
            new NavigationBasisPointInterval(6100, 6500),
            new NavigationBasisPointInterval(6800, 7200),
            new NavigationBasisPointInterval(7600, 8100),
            new NavigationBasisPointInterval(0, 400),
            new NavigationBasisPointInterval(6500, 7300),
            [new NavigationScenarioScore("fixture", 6100, 7000, 7900, 7200, 200, 6900,
                new NavigationSignedRational(7, 10), new Rational(1, 4), 500, 800,
                new Rational(1, 2))], ["enemy-top-count"], ["source.fixture"]);
        var navigation = new NavigationIntervalScoringResult(navigationState,
            NavigationScoringRegime.GuaranteedRecovery,
            navigationState == NavigationRecommendationState.NoSafeRecommendation
                ? null : option.OptionId,
            navigationState == NavigationRecommendationState.Locked, false, [option],
            navigationState == NavigationRecommendationState.NoSafeRecommendation
                ? [NavigationScoreBlocker.NonDominantIntervals] : [], ["enemy-top-count"]);
        var trace = new AdaptiveDecisionEvent(new AdaptiveDecisionEventInput(
            "fixture-v1", "profile", "map", 1, "input", phase, [],
            [new AdaptiveRouteComponent("top_physical", DamageLane.Physical, 6800, 6400, 7600),
             new AdaptiveRouteComponent("top_magic", DamageLane.Magic, 6200, 6000, 7100)],
            [option.OptionId], 8200, false,
            [new AdaptiveNavigationScenario(option.OptionId, 5000,
                new AdaptiveDecisionInterval(300, 700), new AdaptiveDecisionInterval(900, 1300))],
            AdaptiveNavigationRegime.GuaranteedRecovery,
            navigationState == NavigationRecommendationState.Locked, manual, forced));
        return new AdaptivePlanningApplied("input", state, navigation, trace)
        { SuggestedLegendId = "legend_anchor", Blockers = blockers.IsDefault ? [] : blockers };
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.Ordinal));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string GitHead()
    {
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
        process?.WaitForExit();
        return process?.StandardOutput.ReadToEnd().Trim() ?? "unknown";
    }

    private static string SourceFingerprint()
        => CaptureSourceFingerprint.FromCanonicalFiles();

    private static string StateName(PlannerEvidenceState state) =>
        state.ToString().Replace("Round", "round-", StringComparison.Ordinal)
            .Replace("SequenceFirstRare", "sequence-first-rare", StringComparison.Ordinal)
            .Replace("SequenceStoryReward", "sequence-story-reward", StringComparison.Ordinal)
            .Replace("SequenceFirstLegend", "sequence-first-legend", StringComparison.Ordinal)
            .Replace("SequenceRareReward", "sequence-rare-reward", StringComparison.Ordinal)
            .Replace("SequenceTopNavigation", "sequence-top-navigation", StringComparison.Ordinal)
            .Replace("ManualOverride", "manual-override", StringComparison.Ordinal)
            .Replace("Preview", "-preview", StringComparison.Ordinal)
            .Replace("Actionable", "-actionable", StringComparison.Ordinal)
            .Replace("Forced", "-forced", StringComparison.Ordinal)
            .ToLowerInvariant();

    private sealed record Fixture(PlannerEvidenceState State, int Round,
        AdaptivePlanningApplied? Applied, bool Unknown, string KoreanFixture,
        StoryRewardSequenceDecision? Sequence = null,
        ManualLatches? CurrentManualLatches = null);

    private sealed record EvidenceRow(string State, string ControlId, string Field,
        string DisplayedValue, string MachineValue, string AccessibilityName,
        string AccessibilityValue, double Dpi, double Scale, string KoreanFixture,
        string ScreenshotTop, string ScreenshotBottom, string BuildSha,
        string SourceFingerprint, string Verdict);

    private sealed record BulletFixture(BulletOperatingBoardState State,
        BulletOperatingBoardInput Input, InventoryStatSummary Stats);

    private sealed record BulletEvidenceRow(string State, string ControlId, string Field,
        string DisplayedValue, string AccessibilityName, string AccessibilityValue,
        double Dpi, double Scale, string ScreenshotTop, string ScreenshotBottom,
        string BuildSha, string SourceFingerprint, string Verdict);

    private sealed record BulletCoreEvidence(string ControlId, string DisplayValue,
        string AccessibilityName, string AccessibilityValue);

    private sealed record TelemetrySettingsEvidence(string State,
        string AccessibilityName, string Disclosure, string Screenshot,
        string BuildSha, string SourceFingerprint, string Verdict);

    private sealed record MainPendingRecommendationEvidence(
        string Screenshot, string ContextScreenshot, string Scope,
        string BuildSha, string SourceFingerprint, string Verdict);

    private sealed record CaptureValidationRow(
        string State, double Scale, string Position, string Screenshot,
        CapturePixelMetrics PixelMetrics, string KoreanTextLayout,
        string Verdict);

    private sealed record CurrentCraftEvidenceRow(string State, string DisplayedTarget,
        string AccessibilityName, string AccessibilityValue, string FinalGoal,
        int ProgressPercent, double Dpi, double Scale, string ScreenshotTop,
        string ScreenshotBottom, string BuildSha, string SourceFingerprint, string Verdict);

    private sealed record StatsAdviceEvidence(string State, string? ExpectedText,
        string Screenshot, string BuildSha, string SourceFingerprint, string Verdict);
}
