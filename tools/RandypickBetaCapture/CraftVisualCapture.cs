using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OrandOverlay;

internal static partial class DiagnosticCapture
{
    private static void RunCraftVisuals(MainWindow main, OverlayWindow overlay, DataCatalog catalog,
        NormalCandidateBrowser model, Action<IReadOnlyDictionary<string, int>, bool> hand,
        Action<string, Window, string> capture, Action<bool, string> check)
    {
        var common = catalog.AllUnits.First(u => u.Recipe.Count == 0 && u.Name == "쵸파");
        var counts = new Dictionary<string, int> { [common.Id] = 1 };
        hand(counts, true); model.SetStage(NormalCandidateStage.Upper);
        var goal = catalog.Unit("rawcode:090H");
        var planner = new NormalCraftPlanner(catalog);
        SelectCraftTarget(main, model, goal.Id); Drain();
        var plan = planner.Build(goal.Id, counts);
        check(plan.Steps.Count > 1 && plan.Steps.Any(s => s.CombineCount > 1), "craft-visual-batched-multistep-fixture");
        foreach (var pair in new[] { (View: main.CraftWorkspace, Window: (Window)main.CraftWindow, Role: "craft") })
        {
            var elements = Visuals(pair.View).OfType<FrameworkElement>().ToArray();
            var cards = elements.OfType<Border>().Where(e => AutomationProperties.GetAutomationId(e).StartsWith("normal-craft-step-", StringComparison.Ordinal)).ToArray();
            check(cards.Length == plan.Steps.Count, "craft-visual-all-step-cards-" + pair.Role);
            foreach (var step in plan.Steps)
            {
                var card = cards.Single(c => AutomationProperties.GetAutomationId(c) == "normal-craft-step-" + step.UnitId);
                var toggle = Visuals(card).OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-step-toggle-" + step.UnitId);
                var wasExpanded = toggle.IsExpanded;
                toggle.IsExpanded = true; Drain();
                var descendants = Visuals(card).OfType<FrameworkElement>().ToArray();
                var action = descendants.Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-action-" + step.UnitId);
                var key = descendants.OfType<Border>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-key-" + step.UnitId);
                check(((TextBlock)key.Child).FontSize >= 16 && key.ActualWidth >= 25, "craft-visual-key-readable-" + pair.Role + step.UnitId);
                check(AutomationProperties.GetName(action).Contains(step.CombineCount + "회") &&
                    AutomationProperties.GetName(card).Contains("×" + step.OutputCount), "craft-visual-counts-" + pair.Role + step.UnitId);
                check(!Visuals(action).OfType<Button>().Any(), "craft-visual-keys-are-display-only-" + pair.Role + step.UnitId);
                var materials = descendants.Where(e => AutomationProperties.GetAutomationId(e).StartsWith("normal-craft-material-", StringComparison.Ordinal)).ToArray();
                check(materials.Length == step.Ingredients.Count, "craft-visual-all-ingredients-" + pair.Role + step.UnitId);
                check(descendants.OfType<TextBlock>().All(t => t.FontSize >= 11), "craft-visual-body-minimum-type-" + pair.Role + step.UnitId);
                check(card.ActualWidth <= pair.View.ActualWidth && card.ActualWidth > 0, "craft-visual-card-bounds-" + pair.Role + step.UnitId);
                toggle.IsExpanded = wasExpanded; Drain();
            }
            var scroll = elements.OfType<ScrollViewer>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-scroll");
            var missing = elements.OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-missing-materials");
            check(missing.IsExpanded && missing.TranslatePoint(new Point(0, 0), scroll).Y < scroll.ViewportHeight, "beta-missing-list-before-steps-" + pair.Role);
            var missingTiles = Visuals(missing).OfType<Border>().Where(e => AutomationProperties.GetAutomationId(e).StartsWith("normal-craft-material-missing/", StringComparison.Ordinal)).ToArray();
            check(missingTiles.Length == plan.MissingMaterials.Count, "beta-all-missing-types-visible-" + pair.Role);
            foreach (var material in plan.MissingMaterials)
                check(missingTiles.Any(e => AutomationProperties.GetAutomationId(e) == "normal-craft-material-missing/" + material.UnitId && AutomationProperties.GetName(e) == material.Name + " " + material.MissingCount + "개 부족 · 보유 " + material.OwnedCount + " / 필요 " + material.RequiredCount), "beta-exact-missing-quantity-" + pair.Role + material.UnitId);
            check(plan.ResourceRequirements.Where(r => r.Value > 0).All(r => elements.OfType<TextBlock>().Any(e => AutomationProperties.GetAutomationId(e) == "normal-missing-resource-" + r.Key && e.Text.Contains("보유량 미확인"))), "beta-resource-not-assumed-owned-" + pair.Role);
            ValidateCraftSplit(pair.View, pair.Role, check);
            capture("craft-visual-chain", pair.Window, pair.Role);
            missing.IsExpanded = false; Drain();
            capture("craft-visual-missing-folded", pair.Window, pair.Role);
            var finalCard = cards.Single(c => AutomationProperties.GetAutomationId(c) == "normal-craft-step-" + goal.Id);
            Visuals(finalCard).OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-step-toggle-" + goal.Id).IsExpanded = true;
            Drain();
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + finalCard.TranslatePoint(new Point(0, 0), scroll).Y); Drain();
            capture("craft-visual-chat", pair.Window, pair.Role);
            check(Visuals(finalCard).OfType<TextBlock>().Any(t => t.Text == "선택 조건 미확인"), "craft-visual-chat-selection-unknown-" + pair.Role);
            scroll.ScrollToHome(); Drain();
        }
        var near = plan.MissingMaterials.ToDictionary(m => m.UnitId, m => checked((int)m.RequiredCount));
        var shortCommon = plan.MissingMaterials.First(m => m.Name == "쵸파");
        var shortSpecial = plan.MissingMaterials.First(m => m.Name == "초월쿠마");
        near[shortCommon.UnitId] -= 2; near.Remove(shortSpecial.UnitId);
        hand(near, true); model.SetStage(NormalCandidateStage.Upper); SelectCraftTarget(main, model, goal.Id); Drain();
        var nearPlan = planner.Build(goal.Id, near);
        check(nearPlan.MissingMaterials.Count == 2, "missing-portrait-two-types-fixture");
        foreach (var pair in new[] { (View: main.CraftWorkspace, Window: (Window)main.CraftWindow, Role: "craft") })
        {
            var slots = Visuals(pair.View).OfType<Border>().Where(e => AutomationProperties.GetAutomationId(e).StartsWith("normal-craft-material-missing/", StringComparison.Ordinal)).ToArray();
            check(slots.Length == 2, "missing-portrait-two-types-" + pair.Role);
            foreach (var slot in slots)
            {
                var portrait = Visuals(slot).OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e).StartsWith("normal-missing-portrait-", StringComparison.Ordinal));
                var badge = Visuals(slot).OfType<Border>().Single(e => AutomationProperties.GetAutomationId(e).StartsWith("normal-missing-count-", StringComparison.Ordinal));
                check(portrait.ActualWidth >= 32 && portrait.ActualHeight >= 32 && Visuals(badge).OfType<TextBlock>().Any(t => t.FontSize >= 12 && t.Text.Any(char.IsDigit)), "missing-portrait-readable-" + pair.Role + AutomationProperties.GetAutomationId(slot));
                check(!Visuals(slot).OfType<Button>().Any(), "missing-portrait-display-only-" + pair.Role + AutomationProperties.GetAutomationId(slot));
            }
            ValidateCraftSplit(pair.View, pair.Role, check);
            capture("missing-two", pair.Window, pair.Role);
        }
        near[shortSpecial.UnitId] = 1; hand(near, false);
        check(planner.Build(goal.Id, near).MissingMaterials.Count == 1, "missing-portrait-updates-to-one");
        capture("missing-one", main, "main"); capture("missing-one", overlay, "overlay"); capture("missing-one", main.CraftWindow, "craft");
        near[shortCommon.UnitId] += 2; hand(near, false);
        foreach (var view in new[] { main.CraftWorkspace })
            check(!Visuals(view).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) == "normal-missing-materials"), "missing-portrait-clears-when-materials-present");
        capture("missing-none", main, "main"); capture("missing-none", overlay, "overlay"); capture("missing-none", main.CraftWindow, "craft");
        var unknown = catalog.AllUnits.Where(u => u.Recipe.Count > 0 && u.CombineCommands.Count == 0)
            .Select(u => (Unit: u, Step: planner.Build(u.Id, counts).Steps.LastOrDefault(s => s.UnitId == u.Id)))
            .First(p => p.Step is { CombineKey: null } && p.Step.CombineCommands.Count == 0);
        SelectCraftTarget(main, model, unknown.Unit.Id); Drain();
        foreach (var pair in new[] { (View: main.CraftWorkspace, Window: (Window)main.CraftWindow, Role: "craft") })
        {
            var card = Visuals(pair.View).OfType<Border>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-step-" + unknown.Unit.Id);
            Visuals(card).OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-step-toggle-" + unknown.Unit.Id).IsExpanded = true;
            Drain();
            var scroll = Visuals(pair.View).OfType<ScrollViewer>().Single(e => AutomationProperties.GetAutomationId(e) == "normal-craft-scroll");
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + card.TranslatePoint(new Point(0, 0), scroll).Y); Drain();
            check(Visuals(card).OfType<TextBlock>().Any(t => t.Text == "조합 키·선택 유닛 미확인"), "craft-visual-unknown-never-guessed-" + pair.Role);
            capture("craft-visual-unknown", pair.Window, pair.Role);
        }
        hand(counts, true); model.SetStage(NormalCandidateStage.Upper); SelectCraftTarget(main, model, goal.Id); Drain();
        CaptureDetachedCraft(main, overlay, model, () => hand(counts, false), capture, check);
    }
}
