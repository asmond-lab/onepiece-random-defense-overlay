using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureNavigationStates(string output, bool liveQuests)
    {
        System.Threading.SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext());
        var observed = RouteQuestSnapshot.FromVerifiedSlots([
            new(0, "Q008", true), new(1, "Q016", true), new(2, "Q011", false)]);
        if (liveQuests)
        {
            var liveCatalog = new DataCatalog();
            liveCatalog.Load();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var live = new WarcraftMemoryRecognitionService(liveCatalog)
                .RecognizeAsync(new AppSettings(), timeout.Token).GetAwaiter().GetResult();
            observed = live.MapSignals.RouteQuests;
            Console.WriteLine($"LIVE_QUEST state={live.State} verified={observed.IsVerified}\n{observed.Describe()}");
            if (!observed.IsVerified) throw new InvalidOperationException("Live quest observation unavailable");
        }
        var states = new[] { "unselected", "candidate", "confirmed", "reset" };
        foreach (var scale in new[] { 0.75, 1.0, 1.5 })
        foreach (var state in states)
        {
            var session = new NavigationSessionState();
            if (state is "confirmed" or "reset") session.Confirm("PathOfKings.BountyHunter");
            if (state == "reset") session.Reset();
            var result = state == "unselected" ? null :
                new NavigationIntervalScoringResult(NavigationRecommendationState.Provisional,
                    NavigationScoringRegime.SecureCore, "PathOfKings.BountyHunter", false, false, [], [], []);
            var catalog = new DataCatalog();
            catalog.Load();
            var zoro = catalog.Unit("rawcode:F90H");
            var inventory = zoro.Recipe.Where(pair => !catalog.Unit(pair.Key).Name.Contains("쿠마", StringComparison.Ordinal))
                .Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
            var rec = new RecommendationEngine(catalog).RecommendNearestCrafts(zoro.Id, inventory, 32)
                .Single(item => item.Route.GoalUnitId == zoro.Id);
            var source = new AdaptivePlanningInputSource
            {
                MatchGeneration = 1, RecognitionRevision = 1, Round = 21, Phase = PlannerPhase.Committed,
                GoalUnitId = zoro.Id, Inventory = inventory,
                Units = catalog.AllUnits.GroupBy(unit => unit.Id).ToDictionary(g => g.Key, g => g.First()),
                NavigationOptionId = "PathOfKings.BountyHunter", GoroseiMode = GoroseiMode.None,
                ManualLatches = new(true, false),
                RouteQuests = state == "reset" ? RouteQuestSnapshot.Unknown :
                    observed
            };
            var window = new OverlayWindow { Topmost = false, ShowActivated = false, Opacity = 0,
                Left = SystemParameters.VirtualScreenLeft, Top = SystemParameters.VirtualScreenTop };
            window.SetClickThrough(true);
            window.Render(session.Header("조로"), [rec], EmptyStats(), [], [], false, [], "격리된 검증 fixture", inventory: inventory);
            window.RenderNavigationContext(NavigationSessionState.Candidate(result),
                NavigationComparisonPresentation.Describe(result, RouteQuestEvaluation.Evaluate(source)));
            window.Show();
            FlushRender(window);
            window.Left = SystemParameters.VirtualScreenLeft - 2000;
            window.Opacity = 1;
            ApplyScale(window, scale);
            var title = (TextBlock)window.FindName("GoalText");
            if (title.Text != session.Header("조로")) throw new InvalidOperationException("Navigation title mismatch");
            var scroll = (ScrollViewer)window.FindName("BoardScrollViewer");
            var stem = $"navigation-{state}-{scale * 100:0}";
            CapturePng(window, Path.Combine(output, stem + "-top.png"), scale);
            scroll.ScrollToEnd();
            FlushRender(window);
            CapturePng(window, Path.Combine(output, stem + "-bottom.png"), scale);
            ValidateFooter(window);
            window.Close();
            window.Stats.Close();
        }
        var main = new MainWindow(FixtureContext(new AppSettings { AutoScanEnabled = false,
            ClearDataAutoRefresh = false }))
        { Topmost = false, ShowActivated = false, Opacity = 0 };
        main.Show();
        FlushRender(main);
        var signalsField = typeof(MainWindow).GetField("_mapSignals",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var signatureMethod = typeof(MainWindow).GetMethod("BuildScanSignature",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var before = signatureMethod.Invoke(main, null);
        var visibleGoals = (List<UnitDefinition>)typeof(MainWindow).GetMethod("GoalUnits",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(main, null)!;
        var mainCatalog = new DataCatalog();
        mainCatalog.Load();
        var expectedGoals = mainCatalog.AllUnits.Where(unit => unit.Recipe.Count > 0 &&
            unit.Tier.Split('[', 2)[0].Trim() is "신비함" or "초월" or "불멸" or "영원" or "제한됨")
            .Select(unit => unit.Id).Distinct().Order().ToArray();
        if (!visibleGoals.Select(unit => unit.Id).Order().SequenceEqual(expectedGoals))
            throw new InvalidOperationException("Goal choices excluded catalog units by historical records");
        signalsField.SetValue(main, MapSignals.Empty with { RouteQuests = observed });
        if (Equals(before, signatureMethod.Invoke(main, null)))
            throw new InvalidOperationException("Quest-only update did not invalidate scan signature");
        main.RenderNavigationContext();
        main.Left = SystemParameters.VirtualScreenLeft - 3000;
        main.Opacity = 1;
        var settings = (ScrollViewer)main.FindName("SettingsScrollViewer");
        ((Button)main.FindName("ConfirmNavigationButton")).BringIntoView();
        FlushRender(main);
        CaptureFrameworkElementPng(settings, Path.Combine(output, "navigation-settings.png"));
        var confirm = (Button)main.FindName("ConfirmNavigationButton");
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        main.RenderNavigationContext();
        var status = (TextBlock)main.FindName("NavigationStatusText");
        if (!status.Text.Contains("사용자 확인", StringComparison.Ordinal))
            throw new InvalidOperationException("Confirmation control did not record user selection");
        typeof(MainWindow).GetMethod("ClearNavigation_OnClick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(main, [confirm, new RoutedEventArgs()]);
        main.RenderNavigationContext();
        if (!status.Text.Contains("항법 미선택", StringComparison.Ordinal))
            throw new InvalidOperationException("Clear selection did not restore unselected status");
        main.Close();
        Console.WriteLine("NAVIGATION_UI PASS states=4 scales=3 screenshots=25");
    }
}
