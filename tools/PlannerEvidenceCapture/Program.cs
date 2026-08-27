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
        var app = new App();
        app.InitializeComponent();
        var rows = new List<EvidenceRow>();
        var captures = Cases().Select(item => (item, scale: 1.0))
            .Concat(new[] { 0.75, 1.25, 1.5 }.Select(scale =>
                (Cases().Single(item => item.State == PlannerEvidenceState.Round21Actionable), scale)))
            .ToList();

        foreach (var (fixture, scale) in captures)
            Capture(output, buildSha, sourceFingerprint, fixture, scale, rows);

        var matrixPath = Path.Combine(output, "planner-ui-evidence-matrix.json");
        File.WriteAllText(matrixPath, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            buildSha,
            sourceFingerprint,
            shell = new { designWidth = 540, designHeight = 740, soleVerticalScrollOwner = "candidate-board-scroll" },
            fixtures = captures.Count,
            rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"WPF_PLANNER_EVIDENCE PASS captures={captures.Count * 2} rows={rows.Count} matrix={matrixPath}");
        return 0;
    }

    private static void Capture(string output, string buildSha, string sourceFingerprint,
        Fixture fixture, double scale, List<EvidenceRow> rows)
    {
        var scaleName = (scale * 100).ToString("0", CultureInfo.InvariantCulture);
        var stem = $"planner-{StateName(fixture.State)}-{scaleName}";
        var topPath = Path.Combine(output, stem + "-top.png");
        var bottomPath = Path.Combine(output, stem + "-bottom.png");
        var view = RecommendationPresentation.PlannerEvidence(fixture.Round, fixture.Applied,
            fixture.Unknown, fixture.Unknown ? LongUnknownReason : null);
        if (view.State != fixture.State)
            throw new InvalidOperationException($"Fixture state mismatch: {fixture.State} != {view.State}");

        var window = new OverlayWindow { Topmost = false, Left = 0, Top = 0 };
        window.RenderPlannerEvidence(fixture.Round, fixture.Applied, fixture.Unknown,
            fixture.Unknown ? LongUnknownReason : null);
        window.UpdateStatus("인식 정상 · 플래너 근거 표시 중");
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        ApplyScale(window, scale);
        CapturePng(window, topPath, scale);

        var scroll = (ScrollViewer?)window.FindName("BoardScrollViewer")
                     ?? throw new InvalidOperationException("candidate board scroll owner missing");
        scroll.ScrollToEnd();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        CapturePng(window, bottomPath, scale);
        ValidateCapture(topPath, 540 * scale, 740 * scale);
        ValidateCapture(bottomPath, 540 * scale, 740 * scale);
        ValidateFooter(window);
        ValidateAutomationTree(window, view);
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
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void ValidateCapture(string path, double expectedWidth, double expectedHeight)
    {
        var bytes = File.ReadAllBytes(path);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature))
            throw new InvalidDataException($"Invalid PNG signature: {path}");
        var width = ReadBigEndian(bytes.AsSpan(16, 4));
        var height = ReadBigEndian(bytes.AsSpan(20, 4));
        if (width != (int)Math.Round(expectedWidth) || height != (int)Math.Round(expectedHeight))
            throw new InvalidDataException($"Unexpected PNG dimensions: {path} {width}x{height}");
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
            if (!element.IsVisible || bounds.Bottom > root.ActualHeight + 0.5 || bounds.Top < -0.5)
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

    private static IReadOnlyList<Fixture> Cases() =>
    [
        new(PlannerEvidenceState.Waiting, 10, null, false, "입력 대기 중"),
        new(PlannerEvidenceState.Blocked, 10,
            Applied(NavigationRecommendationState.NoSafeRecommendation,
                PlannerPhase.AwaitMarineford, [AdaptiveBuildBlocker.AwaitingMarineford]),
            false, "마린포드 진행을 기다립니다"),
        new(PlannerEvidenceState.Round20Preview, 20,
            Applied(NavigationRecommendationState.Provisional, PlannerPhase.CommitRound20),
            false, "20라운드 경로 미리보기"),
        new(PlannerEvidenceState.Round21Actionable, 21,
            Applied(NavigationRecommendationState.Actionable, PlannerPhase.Committed),
            false, "직접 항법을 선택하세요"),
        new(PlannerEvidenceState.Committed, 22,
            Applied(NavigationRecommendationState.Locked, PlannerPhase.Committed),
            false, "추천 고정 상태"),
        new(PlannerEvidenceState.ManualOverride, 22,
            Applied(NavigationRecommendationState.ManualOverride, PlannerPhase.ManualOverride,
                manual: new ManualLatches(true, true)), false, "수동 설정 유지"),
        new(PlannerEvidenceState.Round24Forced, 24,
            Applied(NavigationRecommendationState.SourceExpectedForced, PlannerPhase.Committed,
                forced: "AlliedForces.DoubleBenefit"), false, "원본 규칙 기대값"),
        new(PlannerEvidenceState.Unknown, 20, null, true, LongUnknownReason)
    ];

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
            .Replace("ManualOverride", "manual-override", StringComparison.Ordinal)
            .Replace("Preview", "-preview", StringComparison.Ordinal)
            .Replace("Actionable", "-actionable", StringComparison.Ordinal)
            .Replace("Forced", "-forced", StringComparison.Ordinal)
            .ToLowerInvariant();

    private sealed record Fixture(PlannerEvidenceState State, int Round,
        AdaptivePlanningApplied? Applied, bool Unknown, string KoreanFixture);

    private sealed record EvidenceRow(string State, string ControlId, string Field,
        string DisplayedValue, string MachineValue, string AccessibilityName,
        string AccessibilityValue, double Dpi, double Scale, string KoreanFixture,
        string ScreenshotTop, string ScreenshotBottom, string BuildSha,
        string SourceFingerprint, string Verdict);
}
