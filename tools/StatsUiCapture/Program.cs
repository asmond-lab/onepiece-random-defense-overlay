using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrandOverlay;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var app = new App { Execution = OverlayExecutionContext.Fixture(new AppSettings()) };
        app.InitializeComponent(); TelemetryConsentStartup.ClearStartupUri(app);
        var overlay = new OverlayWindow();
        var window = overlay.Stats;
        var mainCoach = new BeginnerCoachView { IsPlanWorkspace = true };
        var catalog = new DataCatalog(); catalog.Load(false);
        var inventory = new List<InventoryEntry> {
            new() { UnitId = "rawcode:U20h", Count = 1 }, new() { UnitId = "rawcode:930h", Count = 1 },
            new() { UnitId = "rawcode:V20h", Count = 1 }, new() { UnitId = "rawcode:180h", Count = 1 } };
        try
        {
            window.SetDisplayMode(OverlayDisplayMode.StatsOnly); window.Show(); window.Left = -4000;
            foreach (var state in new[] { "physical", "magic", "magic-gorosei", "high", "zero", "unknown-target", "pending", "idle", "stale" })
            {
                var entries = state == "zero" ? new List<InventoryEntry>() : inventory;
                if (state == "high") entries = inventory.Select(item => new InventoryEntry { UnitId = item.UnitId, Count = 10 }).ToList();
                var magic = state.StartsWith("magic", StringComparison.Ordinal);
                var gorosei = state == "magic-gorosei" ? GoroseiMode.Warcury : GoroseiMode.None;
                var stunTarget = state == "high" ? 2.2 : 1.4;
                var stats = new InventoryStatsCalculator(catalog).Calculate(entries);
                var current = state is not ("idle" or "stale");
                var frame = new CoachFrame { MatchGeneration = 1, Revision = 1, Round = current ? 40 : 0,
                    CompletedStoryStage = current ? 13 : 0, IsCurrent = current,
                    Difficulty = state == "unknown-target" || !current ? "unknown" : "악몽",
                    GoalId = current ? "rawcode:180h" : null,
                    Inventory = entries.ToImmutableDictionary(item => item.UnitId, item => item.Count) };
                var decision = new CoachDecision(state == "pending" ? CoachActionKind.Waiting : CoachActionKind.Gather,
                    state == "idle" ? "start" : state, "", "", "", "", "")
                    { CraftProgress = state == "pending" ? new("rawcode:D00h", 2, 1, true) : null };
                mainCoach.Render(decision, frame, null);
                overlay.Render("fixture", [], stats, [], [], false, [], state, magicGoal: magic, gorosei: gorosei, stunTarget: stunTarget,
                    inventory: entries);
                overlay.RenderCoach(true, decision, frame, null);
                Verify(window, stats, magic, gorosei, stunTarget, current, frame.HasKnownDifficulty);
                File.WriteAllText(Path.Combine(output, state + "-values.json"), JsonSerializer.Serialize(new
                { State = state, Stats = stats, Current = current, TargetsKnown = window.TargetsKnown, Magic = magic,
                    Core = window.CoreKpiPanel.Children.OfType<StatsMetricView>().Select(item => new { item.Current, item.Target, item.TargetsKnown })
                }, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var scale in new[] { 1d, 1.25, 1.5 })
                {
                    var layout = OverlayLayoutPolicy.StatsLayout(OverlayDisplayMode.StatsOnly);
                    window.Width = layout.Width; window.Height = layout.Height;
                    window.StatsScroll.ScrollToHome(); window.UpdateLayout(); CheckViewport(window);
                    Save(window, Path.Combine(output, state + "-" + scale + ".png"), scale);
                    window.StatsScroll.ScrollToEnd(); window.UpdateLayout();
                    Save(window, Path.Combine(output, state + "-" + scale + "-bottom.png"), scale);
                }
                window.Width = 200; window.Height = 600; window.StatsScroll.ScrollToHome(); window.UpdateLayout(); CheckViewport(window);
                Save(window, Path.Combine(output, state + "-small.png"), 1);
                window.StatsScroll.ScrollToEnd(); window.UpdateLayout(); CheckViewport(window);
                Save(window, Path.Combine(output, state + "-small-bottom.png"), 1);
            }
            Console.WriteLine("STATS_CAPTURE PASS fixture-only 9 states x 4 sizes, current/target/role/source bindings, main coach and overflow");
        }
        finally { window.CloseForApplication(); overlay.CloseForApplication(); app.Shutdown(); }
    }
    private static void Verify(StatsOverlayWindow window, InventoryStatSummary stats, bool magic,
        GoroseiMode gorosei, double stunTarget, bool current, bool targetsKnown)
    {
        Check(window.HasCurrentObservation == current, "Observation validity diverged");
        var valuesKnown = stats.UnknownValueUnitCount == 0;
        targetsKnown = targetsKnown && valuesKnown && !stats.IsLegacyReferenceForSelectedMap;
        Check(window.TargetsKnown == (current && targetsKnown), "Target validity diverged");
        Check(window.StatsScroll.Visibility == (current ? Visibility.Visible : Visibility.Collapsed), "Unknown values remain visible");
        var core = window.CoreKpiPanel.Children.OfType<StatsMetricView>().ToArray();
        Check(core.Length == 3, "Primary metric count changed");
        var actual = new[] { stats.Stun, stats.TotalSlow, magic ? stats.MagicArmorReduction : stats.TotalArmorReduction };
        var targets = new[] { stunTarget, GoroseiEffects.AdjustSlowTarget(102, gorosei),
            magic ? GoroseiEffects.AdjustMagicArmorTarget(1, gorosei) : GoroseiEffects.AdjustArmorTarget(211, gorosei) };
        for (var i = 0; i < core.Length; i++)
        {
            Check(core[i].Current == actual[i] && core[i].Target == targets[i], "Primary binding mismatch");
            var expected = current && targetsKnown ? actual[i] + 0.0001 >= targets[i] ? "met" : "below" : "unknown";
            Check(AutomationProperties.GetItemStatus(core[i]) == expected, "Threshold state mismatch");
        }
        var values = new Dictionary<string, string> {
            ["공증"] = OverlayTheme.Num(stats.TotalAttackBoost), ["공속"] = OverlayTheme.Num(stats.AttackSpeed),
            ["체젠"] = OverlayTheme.Num(stats.HealthRegen), ["마젠"] = OverlayTheme.Num(stats.ManaRegen),
            ["보잡"] = stats.BossControlProviders + "기" };
        if (stats.BerserkControlProviders > 0) values["광보잡"] = stats.BerserkControlProviders + "기";
        if (stats.AirMovementProviders > 0) values["공중이동"] = stats.AirMovementProviders + "기";
        if (stats.TeleportProviders > 0) values["순간이동"] = stats.TeleportProviders + "기";
        if (stats.BurgessProviders > 0) values["바제스"] = stats.BurgessProviders + "기";
        if (magic)
        {
            values["마뎀증"] = OverlayTheme.Num(stats.MagicAmp);
            values["단일"] = stats.SingleDamageProviders + "기"; values["끝딜"] = stats.FinisherDamageProviders + "기";
        }
        else values["암브"] = stats.ArmorBreakProviders + "기";
        if (stats.SourceUnitCount > 0)
        {
            values["폭뎀증"] = OverlayTheme.Num(stats.ExplosionAmp);
            values["모든피해증"] = OverlayTheme.Num(stats.AllDamageAmp);
            values["단일마증"] = OverlayTheme.Num(stats.SingleMagicAmp);
            if (magic)
            {
                values["단일 지표"] = OverlayTheme.Num(stats.SingleDamageWeight);
                values["끝딜 지표"] = OverlayTheme.Num(stats.FinisherDamageWeight);
            }
            Check(window.SourceEvidenceText.Text.Contains("48784") && window.SourceDetailsText.Text.Contains("TMO 48784") &&
                window.SourceDetailsText.Text.Contains("TMO 48129"), "Separate hand-stat and detail-role sources are undisclosed");
        }
        Check(window.SourceEvidenceText.Visibility == (current ? Visibility.Visible : Visibility.Collapsed),
            "Source disclosure visibility does not match observation");
        var rows = Visuals(window.CurrentStatsPanel).Concat(Visuals(window.AdditionalStatsPanel))
            .OfType<FrameworkElement>().Where(element => AutomationProperties.GetAutomationId(element).StartsWith("stats-value:", StringComparison.Ordinal)).ToArray();
        Check(rows.Length == values.Count, $"Support role count changed: {rows.Length} instead of {values.Count}");
        foreach (var row in rows)
        {
            var id = AutomationProperties.GetAutomationId(row);
            var expectedValue = current && valuesKnown ? values[id["stats-value:".Length..]] : "unknown";
            Check(expectedValue == AutomationProperties.GetItemStatus(row), $"Support binding mismatch: {id}");
        }
    }
    private static void CheckViewport(StatsOverlayWindow window)
    {
        foreach (var text in Visuals(window).OfType<TextBlock>().Where(item => item.IsVisible && item.ActualWidth > 0))
        {
            var point = text.TransformToAncestor(window).Transform(new Point());
            Check(point.X >= -1 && point.X + text.ActualWidth <= window.ActualWidth + 1,
                $"Horizontal text overflow: {text.Text} x={point.X} width={text.ActualWidth} window={window.ActualWidth}");
            Check(text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom <= text.ActualHeight + 1,
                $"Clipped text height: {AutomationProperties.GetAutomationId(text)} desired={text.DesiredSize.Height} actual={text.ActualHeight}");
            if (AutomationProperties.GetAutomationId(text) == "stats-footer")
                Check(point.Y + text.ActualHeight <= window.ActualHeight, "Footer outside footprint");
        }
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Save(FrameworkElement view, string path, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth * scale),
            (int)Math.Ceiling(view.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
}
