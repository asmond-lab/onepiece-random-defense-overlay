using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OrandOverlay;

internal static partial class DiagnosticCapture
{
    private static void RunNormalProgression(MainWindow main, OverlayWindow overlay, DataCatalog catalog,
        NormalCandidateBrowser model, Action<IReadOnlyDictionary<string, int>, bool> hand,
        Action<string, Window, string> capture, Action<bool, string> check)
    {
        var guide = NormalGuideProfile.LoadBundled();
        var normal = (NormalCandidateView)main.FindName("NormalBrowserView");
        var rare = guide.Where(g => g.StoryFast).Select(g => catalog.Unit(g.UnitId))
            .First(u => NormalCandidateBrowser.Tier(u) is "희귀함" or "희귀" && u.Recipe.Count > 0);
        var initial = rare.Recipe.ToDictionary(p => p.Key, p => p.Value);
        hand(initial, true);
        check(model.ProgressStage == NormalCandidateStage.Rare && model.FollowingProgress, "normal-start-first-rare");
        check(((TextBlock)overlay.Stats.FindName("ObservationText")).Text.Contains("주력 미확정") &&
            ((UniformGrid)overlay.Stats.FindName("CoreKpiPanel")).Columns == 2 &&
            ((UniformGrid)overlay.Stats.FindName("CoreKpiPanel")).Rows == 2, "normal-before-upper-neutral-stats");
        SelectCraftTarget(main, model, rare.Id); Drain();
        check(model.ProgressStage == NormalCandidateStage.Rare, "normal-click-and-complete-materials-do-not-advance");
        foreach (var view in new[] { main.CraftWorkspace })
            check(Visuals(view).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) == "normal-craft-flow"), "normal-craft-flow-in-separate-window");
        capture("flow-rare", main, "main"); capture("flow-rare", overlay, "overlay"); capture("flow-rare", main.CraftWindow, "craft");
        capture("flow-neutral", overlay.Stats, "stats");
        hand(new Dictionary<string, int> { [rare.Id] = 1 }, false);
        check(model.Stage == NormalCandidateStage.Legend && model.ProgressStage == NormalCandidateStage.Legend, "normal-rare-observed-to-legend-hidden");
        var tierSet = model.Snapshot.Groups.SelectMany(g => g.Candidates).Select(c => NormalCandidateBrowser.Tier(c.Unit)).ToHashSet();
        check(tierSet.Contains("전설") && tierSet.Contains("히든"), "normal-legend-hidden-joint-pool");
        capture("flow-legend-hidden", main, "main");
        var hidden = catalog.AllUnits.First(u => NormalCandidateBrowser.Tier(u) == "히든");
        hand(new Dictionary<string, int> { [hidden.Id] = 1 }, false);
        check(model.Stage == NormalCandidateStage.Upper, "normal-hidden-observed-to-upper");
        capture("flow-upper", main, "main"); capture("flow-upper", overlay, "overlay");
        var upperByDirection = new Dictionary<string, UnitDefinition>();
        foreach (var direction in new[] { "physical", "magical" })
        {
            var upper = guide.Where(g => g.DamageType == direction).Select(g => catalog.Unit(g.UnitId))
                .First(u => NormalCandidateBrowser.IsUpper(u) && u.Recipe.Count > 0);
            upperByDirection[direction] = upper;
            var current = new Dictionary<string, int> { [upper.Id] = 1 };
            hand(current, true);
            check(model.Stage == NormalCandidateStage.Utility && model.FirstUpperId == upper.Id && model.FirstUpperDirection == direction,
                "normal-upper-anchor-" + direction);
            var primary = Visuals(overlay.Stats).OfType<StatsMetricView>().Select(v => AutomationProperties.GetAutomationId(v)).ToArray();
            check(primary.Contains("stats-metric:" + (direction == "magical" ? "마방깎" : "방깎")), "normal-stats-primary-" + direction);
            check(!overlay.Stats.HasCurrentObservation && !overlay.Stats.TargetsKnown, "normal-reference-stats-fence-" + direction);
            check(model.Snapshot.Groups.SelectMany(g => g.Candidates).Any(c => c.UsefulSupport && c.RecommendationReason.Length > 0),
                "normal-role-backed-support-reason-" + direction);
            var selected = model.Snapshot.Groups.SelectMany(g => g.Candidates).First(c => c.UsefulSupport).Unit.Id;
            SelectCraftTarget(main, model, selected); Drain();
            capture("flow-support-" + direction, main, "main"); capture("flow-support-" + direction, overlay, "overlay");
            capture("flow-support-" + direction, overlay.Stats, "stats");
            foreach (var pair in new[] { (View: main.CraftWorkspace, Window: (Window)main.CraftWindow, Role: "craft") })
            {
                var scroll = Visuals(pair.View).OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "normal-craft-scroll");
                scroll.ScrollToEnd(); Drain();
                capture("flow-craft-end-" + direction, pair.Window, pair.Role);
                check(scroll.VerticalOffset > 0, "normal-long-craft-reachable-" + direction + "-" + pair.Role);
                scroll.ScrollToHome(); Drain();
            }
            var candidateScroll = Visuals(normal).OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "normal-candidate-scroll");
            candidateScroll.ScrollToVerticalOffset(20); Drain();
            var scrollBefore = candidateScroll.VerticalOffset;
            var sameCard = Field<StackPanel>(normal, "_groups").Children[0];
            var sameFlow = Visuals(main.CraftWorkspace).OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-flow");
            hand(current, false);
            check(ReferenceEquals(sameCard, Field<StackPanel>(normal, "_groups").Children[0]) &&
                ReferenceEquals(sameFlow, Visuals(main.CraftWorkspace).OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-flow")),
                "normal-same-hand-preserves-card-and-craft-tree-" + direction);
            check(Math.Abs(candidateScroll.VerticalOffset - scrollBefore) < 1, "normal-same-hand-keeps-scroll-" + direction);
            model.SetStage(NormalCandidateStage.Rare); Drain(); hand(current, false);
            check(model.Stage == NormalCandidateStage.Rare && model.SelectedUnitId == selected && !model.FollowingProgress,
                "normal-manual-stage-and-selection-preserved-" + direction);
            Click(Visuals(normal).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "normal-follow-progress"));
            check(model.Stage == NormalCandidateStage.Utility && model.FollowingProgress, "normal-return-to-progress-button-" + direction);
            foreach (var role in new[] { "공중이동", "지형무시이동", "순간이동" })
            {
                var provider = guide.First(g => g.MovementRoles.Contains(role));
                current[provider.UnitId] = 1; hand(current, false);
                check(model.MovementCoverage.Single(r => r.Role == role).ObservedUnitIds.Contains(provider.UnitId),
                    "normal-movement-provider-" + direction + "-" + role);
            }
            var movement = Visuals(normal).OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-movement-coverage");
            movement.IsExpanded = true; Drain(); capture("flow-movement-" + direction, main, "main");
            var movementScroll = Field<ScrollViewer>(normal, "_candidateScroll");
            var movementOffset = movementScroll.VerticalOffset;
            movementScroll.ScrollToHome(); Drain(); capture("flow-movement-top-" + direction, main, "main");
            movementScroll.ScrollToVerticalOffset(movementOffset); Drain();
        }
        var affinity = guide.First(g => NormalCandidateBrowser.IsUpper(catalog.Unit(g.UnitId)) &&
            g.RecommendedPartners.Any(id => NormalCandidateBrowser.IsUpper(catalog.Unit(id)) || NormalCandidateBrowser.Tier(catalog.Unit(id)) is "전설" or "히든"));
        hand(new Dictionary<string, int> { [affinity.UnitId] = 1 }, true);
        var partnerGroup = model.Snapshot.Groups.Single(g => g.Name == "주력 궁합");
        check(partnerGroup.Candidates.Count > 0 && partnerGroup.Candidates.All(c => c.UsefulSupport &&
            c.RecommendationReason.Contains("추천 조합")), "normal-explicit-author-partner-highlights");
        SelectCraftTarget(main, model, partnerGroup.Candidates[0].Unit.Id); Drain();
        capture("flow-source-partners", main, "main"); capture("flow-source-partners", overlay, "overlay");
        var ambiguous = upperByDirection.Values.ToDictionary(u => u.Id, _ => 1);
        hand(ambiguous, true);
        check(model.FirstUpperId is null && model.FirstUpperChoices.Count == 2, "normal-midgame-ambiguous-anchor-not-guessed");
        capture("flow-anchor-choice", main, "main");
        var anchorPicker = Visuals(normal).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == "normal-first-upper-picker");
        anchorPicker.SelectedItem = model.FirstUpperChoices.First(u => u.Id == upperByDirection["magical"].Id); Drain();
        check(model.FirstUpperDirection == "magical", "normal-explicit-observed-anchor-choice");
        hand(initial, true);
        check(model.ProgressStage == NormalCandidateStage.Rare && model.FirstUpperId is null && model.SelectedUnitId is null,
            "normal-new-game-resets-progress-anchor-and-selection");
        check(Visuals(normal).OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "normal-candidate-scroll").VerticalOffset == 0,
            "normal-new-game-resets-browsing-scroll");
        SelectCraftTarget(main, model, rare.Id); Drain();
        main.Width = 920; main.Height = 620; main.UpdateLayout();
        capture("flow-rare-920", main, "main");
        var detail = main.CraftWorkspace;
        check(!Visuals(normal).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) == "normal-selected-detail") &&
            detail.TransformToAncestor(main.CraftWindow).Transform(new Point(0, detail.ActualHeight)).Y <= main.CraftWindow.ActualHeight + 1,
            "normal-920-craft-detail-in-separate-window");
        capture("flow-rare-920", main.CraftWindow, "craft");
        var craftScroll = Visuals(main.CraftWorkspace).OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "normal-craft-scroll");
        var firstCard = Visuals(main.CraftWorkspace).OfType<Border>().First(b => AutomationProperties.GetAutomationId(b).StartsWith("normal-craft-step-", StringComparison.Ordinal));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { CraftWindowHeight = main.CraftWindow.Height, CraftWindowActualHeight = main.CraftWindow.ActualHeight, CraftContentHeight = ((FrameworkElement)main.CraftWindow.Content).ActualHeight, CraftWorkspaceHeight = main.CraftWorkspace.ActualHeight, CraftViewport = craftScroll.ViewportHeight,
            ScrollHeight = craftScroll.ActualHeight, ScrollOffset = craftScroll.VerticalOffset,
            CardHeight = firstCard.ActualHeight, CardBottom = firstCard.TranslatePoint(new Point(0, firstCard.ActualHeight), craftScroll).Y,
            ContentScroll = craftScroll.CanContentScroll, MainScale = ((FrameworkElement)main.Content).LayoutTransform.Value.ToString(),
            Materials = Visuals(firstCard).OfType<Border>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("normal-craft-material-", StringComparison.Ordinal))
                .Select(b => new { Name = AutomationProperties.GetName(b), Bottom = b.TranslatePoint(new Point(0, b.ActualHeight), craftScroll).Y, Width = b.ActualWidth }).ToArray() }));
        var viewport = Visuals(craftScroll).OfType<ScrollContentPresenter>().First();
        var firstRecipeContent = Visuals(firstCard).OfType<FrameworkElement>().Where(e =>
            AutomationProperties.GetAutomationId(e).StartsWith("normal-craft-material-", StringComparison.Ordinal) ||
            AutomationProperties.GetAutomationId(e).StartsWith("normal-craft-action-", StringComparison.Ordinal)).ToArray();
        check(firstRecipeContent.Length > 1 && firstRecipeContent.All(e =>
            e.TranslatePoint(new Point(0, 0), viewport).Y >= -1 &&
            e.TranslatePoint(new Point(0, e.ActualHeight), viewport).Y <= viewport.ActualHeight + 1),
            "normal-920-first-recipe-all-materials-visible");
        var longName = Visuals(firstCard).OfType<TextBlock>().Single(t => t.Text == "루피 기어세컨드");
        check(longName.ActualHeight < longName.FontSize * 2, "normal-920-ingredient-name-without-orphan-syllable");
        main.Width = 1280; main.Height = 800; main.UpdateLayout();
        RunCraftVisuals(main, overlay, catalog, model, hand, capture, check);
    }
}
