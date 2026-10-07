using OrandOverlay;

namespace Map2322VisualCapture;

// Machine-readable inventory shared by the executable and source-contract tests.
public sealed record NavigationCheck(string Id, string CategoryId, bool ResolvesInSelectedCatalog, bool ManualOnly);
public sealed record StoryCheck(int Index, string ObjectiveRawcode, int OwnerId, bool SourceMatches, bool BaseOnly);
public sealed record YujiroCheck(string AppRawcode, string SourceRawcode, string[] FixedUnits,
    long Gold, long Wood, string[] UnresolvedConditions, bool FixedOnlyReady, bool DistinctPickReady,
    bool CombineCommandAvailable);
public sealed record KaidoCheck(string Dragon, string Hybrid, string Unknown, bool DragonAir,
    bool HybridAir, bool UnknownAir);
public sealed record ReferenceCheck(bool BasicAccepted, bool FullAccepted, bool WrongFingerprintDenied,
    bool WrongVersionDenied, bool ExpiredDenied, bool MissingArchiveDenied);
public sealed record SourceChecks(string FixtureMapVersion, NavigationCheck[] Navigation, StoryCheck[] Story,
    YujiroCheck Yujiro, KaidoCheck Kaido, ReferenceCheck References, bool Legacy321StillLoads);

public static class CaptureContract
{
    public static IReadOnlyDictionary<string, int> FixtureInventory() => new Dictionary<string, int>
    {
        ["rawcode:T60h"] = 1, ["rawcode:Y20h"] = 1, ["ivankov_hidden"] = 1,
        ["rawcode:G60h"] = 1, ["rawcode:I10h"] = 3
    };

    public static string[] ScreenshotScenarios(SourceChecks checks) =>
        checks.FixtureMapVersion == "2.323"
            ? new[] { "default-2323-main-normal.png", "default-2323-craft.png", "default-2323-stats.png",
                "default-2323-navigation.png", "default-2323-profile.png", "default-reference-full.png",
                "default-reference-basic.png", "default-reference-stats.png", "default-reference-inventory.png",
                "default-yujiro-empty-top.png", "default-yujiro-empty-bottom.png",
                "default-reference-wrong-fingerprint.png", "default-reference-missing-archive.png", "default-reference-expired.png" }
            : new[] { "small", "default", "large" }.SelectMany(size =>
            new[] { "main-normal", "normal-overlay", "stats-overlay", "independent-craft", "aux-inventory", "aux-profile" }
                .Select(surface => size + "-" + surface + ".png"))
            .Concat(checks.Navigation.Select(option => "default-navigation-" + option.Id + ".png"))
            .Concat(new[] { "basic", "full", "wrong-fingerprint", "missing-archive", "expired" }
                .Select(state => "default-reference-" + state + ".png"))
            .Concat(new[] { "default-reference-inventory.png", "default-reference-stats.png",
                "default-yujiro-empty-top.png", "default-yujiro-empty-bottom.png",
                "default-craft-bottom.png", "default-candidate-scrolled.png",
                "default-historical-2320.png", "default-small-craft-resized.png", "default-large-craft-resized.png",
                "default-small-unit-resized.png", "default-large-unit-resized.png",
                "default-consent-top.png", "default-consent-bottom.png", "default-consent-decline.png",
                "default-historical-2320-expanded.png", "default-historical-2320-bottom.png",
                "default-yujiro-owned-top.png", "default-yujiro-owned-bottom.png", "default-update-deferred.png", "default-update-ready.png",
                "default-update-busy.png" }).ToArray();

    public static bool MatchesCapture(string expected, string bound, string before, string after, string hash,
        long hwnd, long paintedHwnd, long paintBoundary, long captureBoundary) =>
        expected == bound && expected == before && expected == after &&
        hash.Length == 64 && hwnd != 0 && hwnd == paintedHwnd &&
        paintBoundary > 0 && captureBoundary >= paintBoundary;

    public static bool MatchesNavigationReceipt(string expectedCaption, string expectedSummary,
        string captionBefore, string captionAfter, string summaryBefore, string summaryAfter) =>
        captionBefore == expectedCaption && captionAfter == expectedCaption &&
        summaryBefore == expectedSummary && summaryAfter == expectedSummary;

    public static bool MatchesYujiroInvoke(string automationId, bool visible, bool hasInvokePattern,
        string? selectedId, string[] unresolvedConditions) =>
        automationId == "normal-candidate-rawcode:2C0h" && visible && hasInvokePattern &&
        selectedId == "rawcode:2C0h" && unresolvedConditions.SequenceEqual(["KING:h0C2", "PICK:A800"]);

    public static bool CoversScreenshotOwnedHandles(IEnumerable<long> screenshotHwnds, IEnumerable<long> ownedHwnds) =>
        screenshotHwnds.All(h => h != 0 && ownedHwnds.Contains(h));

    public static bool DistinctSelectorFrames(string firstCaption, string secondCaption, string firstHash, string secondHash) =>
        firstCaption == secondCaption || firstHash != secondHash;

    public static SourceChecks Inspect(string mapVersion = "2.322")
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: mapVersion);
        var navigation = MapNavigationCatalog.Options(catalog.MapBundle!).Select(option =>
            new NavigationCheck(option.Id, option.CategoryId,
                MainWindow.ResolveVersionNavigation(catalog, option.Id).Id == option.Id &&
                MainWindow.VersionNavigationsForCategory(catalog, option.CategoryId).Any(x => x.Id == option.Id),
                catalog.MapVersion == mapVersion &&
                catalog.MapBundle!.Navigation.Options.Any(x => x.Id == option.Id && x.RegistrationLines.Count > 0))).ToArray();
        var profile = MainWindow.LoadApplicationStoryProfile(catalog);
        var story = profile.Stages.Select((stage, index) => new StoryCheck(index + 1, stage.ObjectiveRawcode,
            stage.OwnerId, profile.ResolveActiveStage(stage.ObjectiveRawcode, stage.OwnerId) == stage &&
            stage.ObjectiveRawcode == catalog.MapBundle!.Story.Stages[index].ObjectiveRawcode,
            !stage.RewardComponents.ContributionAtLeast25Percent.Any() &&
            !stage.RewardComponents.Mvp.Any() && !stage.RewardComponents.HiddenOrSideEffect.Any() &&
            stage.RewardComponents.EveryPlayerBase.All(x => x.Condition == "ActivePlayer"))).ToArray();
        var fixedUnits = new Dictionary<string, int> { ["T60h"] = 1, ["Y20h"] = 1, ["Y30h"] = 1, ["G60h"] = 1 };
        var planner = new NormalCraftPlanner(catalog);
        var fixedStep = planner.Build2322Yujiro(fixedUnits).Steps.Single();
        var withPick = new Dictionary<string, int>(fixedUnits) { ["T60h"] = 2 };
        var distinctStep = planner.Build2322Yujiro(withPick,
            [new CraftTargetInstance("fixed", "T60h", true), new CraftTargetInstance("distinct", "T60h", true)]).Steps.Single();
        var yujiro = new YujiroCheck("2C0h", "h0C2", ["T60h", "Y20h", "Y30h", "G60h"],
            planner.Build2322Yujiro(fixedUnits).ResourceRequirements.Gold,
            planner.Build2322Yujiro(fixedUnits).ResourceRequirements.Lumber,
            ["KING:h0C2", "PICK:A800"], fixedStep.IsMaterialReady,
            distinctStep.IsMaterialReady, distinctStep.CombineKey is not null);
        var kaido = new KaidoCheck("DA0h", "WB0h", "M70h",
            Map2322KaidoAirRole.Decide(mapVersion, "rawcode:DA0h", ["M70h"], true) == Map2322KaidoMovement.FlyingDragon,
            Map2322KaidoAirRole.Decide(mapVersion, "rawcode:WB0h", ["M70h"], true) == Map2322KaidoMovement.FlyingDragon,
            Map2322KaidoAirRole.Decide(mapVersion, "rawcode:M70h", ["M70h"], true) == Map2322KaidoMovement.FlyingDragon);
        var now = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
        var context = new string('A', 64);
        var basic = DiagnosticBasicInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, context, 1, 0, now.AddMilliseconds(-100), now,
            TimeSpan.FromMilliseconds(100), [], new string('B', 64), new string('C', 64));
        var full = DiagnosticInventoryObservation.Create(catalog, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, context, 2, 0, now.AddMilliseconds(-100), now,
            TimeSpan.FromMilliseconds(100), [], []);
        var matchingArchive = new RuntimeMapIdentityResult(RuntimeMapIdentityState.Proven, RuntimeMapIdentityFailure.None,
            "", null, "", "", mapVersion == "2.323" ? Map2323SourceContract.ArchiveLengthBytes : Map2322SourceContract.ArchiveLengthBytes,
            mapVersion == "2.323" ? Map2323SourceContract.ArchiveSha256 : Map2322SourceContract.ArchiveSha256, 0, 0, 0, 0);
        var references = new ReferenceCheck(basic.Availability == DiagnosticInventoryAvailability.Ready,
            DiagnosticInventoryConsumerPolicy.CanPresent(full, now, mapVersion, catalog.SelectedDatasetFingerprint, context, 2, 0),
            !DiagnosticInventoryConsumerPolicy.CanPresent(full, now, mapVersion, DiagnosticInventoryObservation.PinnedDatasetFingerprint, context, 2, 0),
            !DiagnosticInventoryConsumerPolicy.CanPresent(full, now, "2.321", catalog.SelectedDatasetFingerprint, context, 2, 0),
            !DiagnosticInventoryConsumerPolicy.CanPresent(full, now.AddSeconds(3), mapVersion, catalog.SelectedDatasetFingerprint, context, 2, 0),
            MapDatasetRuntimePolicy.AllowsObservedArchive(mapVersion, matchingArchive) &&
            !MapDatasetRuntimePolicy.AllowsObservedArchive(mapVersion, null));
        var legacy = new DataCatalog(); legacy.Load(loadCarryPolicy: false, mapVersion: "2.321");
        return new(catalog.MapVersion, navigation, story, yujiro, kaido, references, legacy.MapVersion == "2.321");
    }
}
