using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureBulletGuide(string output)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var settings = new AppSettings { Mode = PlayMode.Normal, GuideNumber = 1, TelemetryEnabled = false };
        var isolatedQueue = Path.Combine(output, "telemetry-disabled");
        Output.EnsureDirectory(isolatedQueue);
        var sentinel = Path.Combine(isolatedQueue, "scenario-sentinel.txt");
        WriteEvidenceText(sentinel, "Non-runtime construction must not delete queued data.");
        var execution = OverlayExecutionContext.Fixture(CaptureInputContract.ValidateSettings(settings));
        var main = new MainWindow(execution);
        Check(File.Exists(sentinel), "Non-runtime construction deleted telemetry queue contents.");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var catalog = (DataCatalog)typeof(MainWindow).GetField("_catalog", flags)!.GetValue(main)!;
        var overlay = (OverlayWindow)typeof(MainWindow).GetField("_overlay", flags)!.GetValue(main)!;
        var view = (BeginnerCoachView)main.FindName("MainCoachView");
        var recognizer = new CoachFrameRecognizer();

        var rows = new List<object>();
        long signalRevision = 1000;
        try
        {
            main.Show();
            main.Left = SystemParameters.VirtualScreenLeft - main.Width - 20;
            Observe("normal", 5, 3, ["120h", "300h"]);
            var selected = WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                PlayModes.Options.Single(option => option.Mode == PlayMode.Guide), (_, frame) => frame.Mode == PlayMode.Guide);
            Check(selected.Frame.GuideNumber == 1 && selected.Frame.GoalId == BulletGuidePolicy.GoalId,
                "Guide1 selection did not reach the production frame.");
            foreach (var surface in new[] { view, overlay.BeginnerView })
                Check(((ComboBox)surface.FindName("GuideChoice")).SelectedItem is GuideOption { Number: 1 },
                    "Main/overlay guide selection disagrees.");
            Observe("opening", 6, 3, ["120h", "300h"]);
            foreach (var surface in new[] { view, overlay.BeginnerView })
            {
                surface.UpdateLayout();
                Check(surface.FindName("GuideSourcePanel") is null && surface.FindName("GuideSourceText") is null,
                    "Guide source evidence must not be rendered in ordinary guidance.");
            }
            var bruleeUnits = new[] { "HA0h", "MC0h", "C20h", "O10h", "X50h", "010h" };
            var bruleePoor = Observe("brulee-wood2", 16, 9, bruleeUnits, lumber: 2);
            Check(bruleePoor.Frame.GuidePlan?.TargetUnitId == "rawcode:S80h" &&
                bruleePoor.Decision.Kind != CoachActionKind.Craft,
                "Brulee preparation ignored actual wood requirement.");
            var bruleeReady = Observe("brulee-wood3", 16, 9, bruleeUnits, lumber: 3);
            Check(bruleeReady.Decision.Kind == CoachActionKind.Craft &&
                bruleeReady.Decision.TargetUnitId == "rawcode:S80h",
                "Source-ready Brulee did not reach executable craft guidance.");
            var prepared = Observe("brulee-owned", 16, 9, ["HA0h", "MC0h", "S80h"]);
            Check(prepared.Frame.GuidePlan?.Stage == BulletGuideStage.AirFoundation,
                "Prepared Brulee was counted as a combat legend.");
            var third = Observe("third-air", 17, 9, ["HA0h", "MC0h", "300h"]);
            Check(third.Frame.GuidePlan is { Stage: BulletGuideStage.AirFoundation, TargetUnitId: "rawcode:930h" },
                "Ordered third legend did not reach the real pipeline.");
            var exchange = Observe("trait-exchange", 17, 9, ["HA0h", "MC0h", "300h"], traits: 1,
                helperState: new(80, 10000, [new("A082", 1, 0)]));
            Check(exchange.Decision.Id == "guide1:exchange-trait-wisp",
                "Observed trait and helper state did not produce exchange guidance.");
            var exchangePoor = Observe("trait-exchange-low-mana", 17, 9, ["HA0h", "MC0h", "300h"], traits: 1,
                helperState: new(79, 10000, [new("A082", 1, 0)]));
            Check(exchangePoor.Decision.Id != "guide1:exchange-trait-wisp", "Insufficient mana permitted exchange.");
            var exchangeEmpty = Observe("trait-exchange-empty", 17, 9, ["HA0h", "MC0h", "300h"], traits: 0,
                helperState: new(80, 10000, [new("A082", 1, 0)]));
            Check(exchangeEmpty.Decision.Id != "guide1:exchange-trait-wisp", "Missing trait permitted exchange.");
            var selection = Observe("selection-wisp", 17, 9, ["HA0h", "MC0h", "300h"], selectionWisps: 1);
            Check(selection.Decision.RewardWispId == "e018", "Owned selection wisp did not reach material guidance.");
            var selectionSpent = Observe("selection-wisp-spent", 17, 9, ["HA0h", "MC0h", "300h"]);
            Check(selectionSpent.Decision.RewardWispId != "e018", "Consumed selection wisp remained actionable.");
            var history = Observe("legend-history", 17, 9, ["HA0h", "300h"]);
            Check(history.Frame.GuidePlan is { Stage: BulletGuideStage.AirFoundation, BossCount: 0 },
                "Legend history was lost or counted as currently owned boss support.");
            var nav = Observe("navigation", 21, 9, ["HA0h", "MC0h", "300h"]);
            Check(nav.Decision.NavigationOptionId == BulletGuidePolicy.NavigationId && nav.Frame.ConfirmedNavigation is null,
                "Guide suggestion was not Bounty, or was falsely confirmed.");
            WaitCoach(main, () => ((Button)overlay.BeginnerView.FindName("ConfirmButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), (_, frame) => frame.ConfirmedNavigation == BulletGuidePolicy.NavigationId);
            var fourth = Observe("fourth-reward-wait", 25, 9, ["HA0h", "MC0h", "930h", "300h"]);
            Check(fourth.Frame.GuidePlan?.Stage == BulletGuideStage.FourthLegendReward &&
                fourth.Frame.CraftSteps.Count == 0, "Fourth legend bypassed story gate.");
            var rayleighChoice = Observe("rayleigh-wisp", 25, 10, ["HA0h", "MC0h", "300h"], transcendenceWisps: 1);
            Check(rayleighChoice.Decision.RewardWispId == "e01A", "Owned transcendence wisp did not reach selection guidance.");
            var rayleighSpent = Observe("rayleigh-wisp-spent", 25, 10, ["HA0h", "MC0h", "300h"]);
            Check(rayleighSpent.Decision.RewardWispId != "e01A", "Spent transcendence wisp remained actionable.");
            var rayleighOwned = Observe("rayleigh-owned", 25, 10, ["HA0h", "MC0h", "X50h", "300h"], transcendenceWisps: 1);
            Check(rayleighOwned.Decision.RewardWispId != "e01A", "Existing Rayleigh requested another conversion.");
            ImmutableArray<CombatUnitState> mirrorUnits =
            [
                new(1, "h08S", 0, null, CombatUnitKind.LocalUnit, new(0, 0), 100, 100, 0, false, false)
                    { MirrorAbility = new("A114", 1, 0) },
                new(2, "h038", 7, null, CombatUnitKind.RecipeExemplar, new(100, 0), 100, 100, 0, false, false)
                    { LegendMarked = true }
            ];
            var mirrorReady = Observe("mirror-caesar", 25, 10, ["HA0h", "MC0h", "S80h"], traits: 1,
                combatObservations: mirrorUnits);
            Check(mirrorReady.Decision.Id == "guide1:mirror-caesar", "Validated Brulee transformation did not reach the UI.");
            var mirrorCooling = Observe("mirror-cooldown", 25, 10, ["HA0h", "MC0h", "S80h"], traits: 1,
                combatObservations: mirrorUnits.SetItem(0, mirrorUnits[0] with
                    { MirrorAbility = new("A114", 1, 5) }));
            Check(mirrorCooling.Decision.Id != "guide1:mirror-caesar", "Cooling Brulee remained actionable.");
            var missionUnits = new[] { "HA0h", "MC0h", "930h", "120h", "620h", "V10h" };
            var mission = Observe("destruction-active", 29, 10, missionUnits, lumber: 3,
                destructionKing: true);
            Check(mission.Frame.GuidePlan is { PursueDestructionKing: true, TargetUnitId: "rawcode:U20h" } &&
                mission.Decision.Kind == CoachActionKind.Craft,
                "Active destruction race did not prioritize the ready fourth legend.");
            var inactiveMission = Observe("destruction-inactive", 29, 10, missionUnits, destructionKing: false);
            Check(inactiveMission.Frame.GuidePlan?.PursueDestructionKing == false,
                "Inactive mission retained race guidance.");
            var expiredMission = Observe("destruction-expired", 30, 10, missionUnits, destructionKing: true);
            Check(expiredMission.Frame.GuidePlan?.PursueDestructionKing == false,
                "Expired destruction deadline remained active.");
            var components = new[] { "930h", "V20h", "U20h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Y30h", "Q30h", "K50h" };
            var craft = Observe("craft-bullet", 50, 13, components, lumber: 10);
            Check(craft.Decision.Kind == CoachActionKind.Craft && craft.Decision.TargetUnitId == BulletGuidePolicy.GoalId,
                "Round50 source-ready recipe failed to produce real Bullet craft instruction.");
            var owned = Observe("owned-bullet", 50, 13, ["180h", "U30h", "540h", "300h"]);
            Check(owned.Frame.GuidePlan?.OwnedBullet == true &&
                owned.Frame.CraftSteps.All(step => step.TargetUnitId != BulletGuidePolicy.GoalId),
                "Actually owned Bullet was ignored or re-crafted.");
            var reward = Observe("reward", 50, 13, ["180h", "U30h", "540h", "300h"], 1);
            Check(reward.Decision.Kind == CoachActionKind.Reward && reward.Decision.CraftDeferredForReward,
                "Reward did not defer guide execution.");
            Observe("operating", 60, 13, ["180h", "U30h", "540h", "300h"]);
            var supported = new[] { "180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Y30h", "Q30h", "K50h" }
                .Concat(Enumerable.Repeat("300h", 20)).ToArray();
            var armor = Observe("upgrade-armor", 60, 13, supported,
                runtime: new(true, 2, 1, 1, "controlled verified tier observation"));
            Check(armor.Decision.Id == "guide1:upgrade:armor", "Verified armor tier did not drive real upgrade guidance.");
            var speed = Observe("upgrade-speed", 60, 13, supported,
                runtime: new(true, 3, 2, 1, "controlled verified tier observation"));
            Check(speed.Decision.Id == "guide1:upgrade:speed", "Tier-only change did not refresh to speed upgrade.");
            var exactCounts = Observe("exact-upgrade-counts", 60, 13, supported,
                runtime: new BulletGuideRuntimeState(true, 2, 2, 1, "controlled exact item observation")
                    .WithExactCounts(new(14, 15, 29)));
            Check(exactCounts.Frame.GuideRuntime.ExactCounts == new BulletUpgradeCounts(14, 15, 29) &&
                exactCounts.Decision.Id == "guide1:upgrade:armor",
                "Exact item counts did not reach the guide.");
            var helperReady = new HelperUnitState(80, 10000, [new("A082", 1, 0)]);
            var helperSeen = Observe("helper-state", 60, 13, supported, helperState: helperReady);
            Check(helperSeen.Frame.HelperState == helperReady, "Helper state did not reach the production frame.");
            var helperCooling = helperReady with { Abilities = [new("A082", 1, 7.5f)] };
            var cooldownChanged = Observe("helper-cooldown-change", 60, 13, supported, helperState: helperCooling);
            Check(cooldownChanged.Frame.HelperState == helperCooling,
                "Cooldown-only change did not refresh the frame.");
            ImmutableArray<CombatUnitState> combatSeen =
            [
                new(1, "h081", 0, null, CombatUnitKind.Bullet, new(0, 0), 1000, 1000, 0, false, false),
                new(2, "o02C", 6, 0, CombatUnitKind.LaneBoss, new(100, 0), 54000000, 54000000, 75, false, false)
            ];
            var combatFrame = Observe("combat-state", 60, 13, supported, combatObservations: combatSeen);
            Check(combatFrame.Frame.CombatObservations.SequenceEqual(combatSeen),
                "Combat observations did not reach the production frame.");
            var damaged = combatSeen.SetItem(1, combatSeen[1] with { Life = 50000000 });
            var damageFrame = Observe("combat-hp-change", 60, 13, supported, combatObservations: damaged);
            Check(damageFrame.Frame.CombatObservations.SequenceEqual(damaged),
                "HP-only change did not refresh the frame.");
            var lineUnits = combatSeen.SetItem(1, combatSeen[1] with { Kind = CombatUnitKind.LaneMonster });
            var lineTarget = Observe("line-target", 60, 13, supported, combatObservations: lineUnits);
            Check(lineTarget.Decision.Id == "guide1:line-target:2", "Observed marked lane target did not reach guidance.");
            var freshTarget = Observe("line-target-fresh", 60, 13, supported,
                combatObservations: lineUnits.SetItem(1, lineUnits[1] with { ArmorBreakStacks = 0 }));
            Check(!freshTarget.Decision.Id.StartsWith("guide1:line-target:", StringComparison.Ordinal),
                "Fresh unmarked target remained recommended.");
            var foreignTarget = Observe("line-target-foreign", 60, 13, supported,
                combatObservations: lineUnits.SetItem(1, lineUnits[1] with { LaneSlot = 1 }));
            Check(!foreignTarget.Decision.Id.StartsWith("guide1:line-target:", StringComparison.Ordinal),
                "Another lane target remained recommended.");
            var helperDisconnected = Observe("helper-disconnected", 60, 13, supported,
                helperState: helperReady, recognitionState: RecognitionState.TransientReadError,
                combatObservations: combatSeen);
            Check(!helperDisconnected.Frame.IsCurrent && helperDisconnected.Frame.HelperState is null &&
                helperDisconnected.Frame.CombatObservations.IsEmpty,
                "Disconnected frame retained known helper/combat values.");
            var helperUnknown = Observe("helper-unknown", 60, 13, supported);
            Check(helperUnknown.Frame.HelperState is null, "Missing helper retained its previous state.");
            var reservedFrame = speed.Frame with
            {
                Inventory = speed.Frame.Inventory
                    .Remove("rawcode:300h").SetItem("luffy_common", 1),
                CommittedCraftUnitId = "luffy_common"
            };
            var reservedDecision = new BeginnerCoachPlanner(catalog).Decide(reservedFrame);
            Check(reservedDecision.Kind != CoachActionKind.Upgrade,
                "Reserved common was offered as upgrade payment.");
            view.Render(reservedDecision, reservedFrame, catalog.Unit(BulletGuidePolicy.GoalId));
            overlay.BeginnerView.Render(reservedDecision, reservedFrame, catalog.Unit(BulletGuidePolicy.GoalId));
            Capture("upgrade-reserved-common");
            var blood = Observe("greenblood-owned", 60, 13, supported, greenBlood: true);
            Check(blood.Decision.Kind == CoachActionKind.Item && blood.Decision.TargetUnitId == "rawcode:U30h",
                "Guide Green Blood did not reach the real item instruction.");
            var bloodReward = Observe("greenblood-reward-wait", 60, 13, supported, 1, greenBlood: true);
            Check(bloodReward.Decision.Kind == CoachActionKind.Reward,
                "Guide Green Blood bypassed the reward hold.");
            var duplicate = Observe("duplicate-special-sale", 60, 13,
                supported.Concat(new[] { "D20h", "Y00h" }).ToArray());
            Check(duplicate.Decision.Kind == CoachActionKind.Economy &&
                duplicate.Decision.TargetUnitId == "rawcode:Y00h",
                "Unreserved duplicate special did not produce sale advice.");
            var saturn = Observe("saturn-chopper", 60, 13, supported, gorosei: GoroseiMode.Saturn, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Saturn, "Controlled planning identity; current effects unverified", []));
            Check(saturn.Frame.GuidePlan?.TargetUnitId == "rawcode:K20h",
                "Observed Saturn did not request the source Chopper supplement.");
            var saturnReady = Observe("saturn-chopper-owned", 60, 13,
                supported.Concat(new[] { "K20h" }).ToArray(), gorosei: GoroseiMode.Saturn, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Saturn, "Controlled planning identity; current effects unverified", []));
            Check(saturnReady.Frame.GuidePlan?.TargetUnitId is null,
                "Owned Chopper was requested again.");
            var queenUnknown = Observe("queen-bonclay-unknown", 60, 13,
                supported.Where(code => code != "Y30h").Concat(new[] { "HA0h", "I20h", "L00h" }).ToArray(),
                lumber: 0);
            Check(queenUnknown.Frame.GuidePlan?.QueenInput == QueenConversionInput.Unknown &&
                !(queenUnknown.Decision.Kind == CoachActionKind.Craft && queenUnknown.Decision.TargetUnitId == "rawcode:IC0h"),
                "Unconfirmed Queen conversion was offered.");
            Check(((ComboBox)view.FindName("QueenCondition")).IsEnabled,
                "Queen user confirmation control was not enabled.");
            WaitCoach(main, () => ((ComboBox)view.FindName("QueenCondition")).SelectedValue =
                QueenConversionInput.UserConfirmedMissionsComplete,
                (_, frame) => frame.GuidePlan?.QueenInput == QueenConversionInput.UserConfirmedMissionsComplete);
            var queenPair = Observe("queen-bonclay-ready", 60, 13,
                supported.Where(code => code != "Y30h").Concat(new[] { "HA0h", "I20h", "L00h" }).ToArray(),
                lumber: 0);
            Check(queenPair.Decision.Kind == CoachActionKind.Craft &&
                queenPair.Decision.TargetUnitId == "rawcode:IC0h",
                "Ready source Queen pair did not reach the actual craft instruction.");
            var spareCommons = Observe("spare-commons-chopper", 60, 13,
                supported.Concat(Enumerable.Repeat("300h", 10)).ToArray());
            Check(spareCommons.Frame.GuidePlan?.TargetUnitId == "rawcode:K20h",
                "Thirty observed commons did not enable the source Chopper supplement.");
            var quest = Observe("high-gamble-quest", 60, 13, supported, highGambleQuest: false);
            Check(quest.Frame.GuidePlan?.ActiveHighGambleQuest == true,
                "Verified active Q006 was not reflected in the guide.");
            var questDone = Observe("high-gamble-completed", 60, 13, supported, highGambleQuest: true);
            Check(questDone.Frame.GuidePlan?.ActiveHighGambleQuest == false,
                "Completed Q006 retained gambling advice.");
            var clearRewards = Observe("clear-count-40", 60, 13, supported, lumber: 0, loadedClearCount: 40);
            Check(clearRewards.Frame.LoadedClearCount == 40,
                "Observed map clear count did not reach the coach.");
            Check(clearRewards.Frame.Signals.GetValueOrDefault("lumber") == 0 &&
                clearRewards.Frame.RewardWisps.Count == 0,
                "Predicted clear rewards were added to observed resources.");
            foreach (var mode in new[] { PlayMode.Beginner, PlayMode.Normal, PlayMode.Guide, PlayMode.Manual })
                foreach (var rewardRound in new[] { 9, 10, 30, 31 })
                {
                    var rewardFrame = clearRewards.Frame with { Mode = mode, Round = rewardRound };
                    foreach (var surface in new[] { view, overlay.BeginnerView })
                    {
                        surface.SetCompact(mode != PlayMode.Beginner);
                        surface.Render(clearRewards.Decision, rewardFrame, catalog.Unit(BulletGuidePolicy.GoalId));
                        Check(((TextBlock)surface.FindName("ClearRewardsText")).Text ==
                            LoginClearRewards.ForCount(40)?.Describe(rewardRound),
                            "Shipped reward schedule differs between view and policy.");
                    }
                    Capture($"clear-count-{mode}-{rewardRound}");
                }
            var lowerCount = Observe("clear-count-35", 60, 13, supported, loadedClearCount: 35);
            Check(lowerCount.Frame.LoadedClearCount == 35, "Clear-count-only change did not refresh.");
            Observe("clear-count-unknown", 60, 13, supported);
            foreach (var surface in new[] { view, overlay.BeginnerView })
                Check(((Expander)surface.FindName("ClearRewardsPanel")).Visibility == Visibility.Collapsed,
                    "Unknown clear count retained previous rewards.");
            ((Expander)view.FindName("DetailsPanel")).IsExpanded = true;
            ((Expander)overlay.BeginnerView.FindName("DetailsPanel")).IsExpanded = true;
            Capture("help-expanded");
            ((ScrollViewer)view.FindName("CoachScroll")).ScrollToBottom();
            ((ScrollViewer)overlay.BeginnerView.FindName("CoachScroll")).ScrollToBottom();
            Capture("help-bottom");
            ((Expander)view.FindName("DetailsPanel")).IsExpanded = false;
            ((Expander)overlay.BeginnerView.FindName("DetailsPanel")).IsExpanded = false;
            var mirrorComplete = Observe("mirror-consumed", 60, 13, ["HA0h", "MC0h", "830h", "300h"],
                traits: 1, combatObservations: mirrorUnits);
            Check(mirrorComplete.Decision.Id != "guide1:mirror-caesar", "Consumed Brulee remained actionable.");
            // Put Gorosei variants after neutral scenarios: None means unknown, not a reset.
            var warcury = Observe("warcury-armor-target", 60, 13, ["180h", "U30h", "540h"],
                gorosei: GoroseiMode.Warcury, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Warcury, "Controlled planning identity; current effects unverified", []));
            Check(warcury.Frame.GuidePlan?.Support?.ArmorTarget == 120 &&
                warcury.Decision.Milestone.Contains("오라깎 120"),
                "Warcury evaluated armor target did not reach the production decision.");
            var twoAir = Observe("duplicate-air-bodies", 60, 13, ["930h", "930h", "MC0h"]);
            Check(twoAir.Frame.GuidePlan is { AirCount: 2 } &&
                twoAir.Frame.GuidePlan.Stage != BulletGuideStage.AirFoundation,
                "Two current mobile bodies were treated as one distinct type.");
            Observe("wipe-first", 60, 13, []);
            var ended = Observe("finished", 60, 13, []);
            Check(ended.Decision.Kind == CoachActionKind.Finished, "Finished guide still emits actions.");
            var next = Observe("new-session", 2, 0, ["300h"], helperState: helperReady);
            Check(next.Frame.GuidePlan is { OwnedBullet: false, Stage: BulletGuideStage.FirstLegend } &&
                next.Frame.ConfirmedNavigation is null && next.Frame.HelperState is null,
                "New session retained guide/nav/helper state.");
            Observe("new-session-navigation", 21, 9, ["HA0h", "U30h", "MC0h"]);
            WaitCoach(main, () => ((Button)overlay.BeginnerView.FindName("ConfirmButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), (_, frame) => frame.ConfirmedNavigation == BulletGuidePolicy.NavigationId);
            var kingRedIngredients = catalog.Unit("rawcode:H30h").Recipe
                .Where(pair => catalog.Unit(pair.Key).Tier != "자원")
                .SelectMany(pair => Enumerable.Repeat(catalog.Unit(pair.Key).Rawcodes[0], pair.Value));
            var kingRedRace = Observe("king-red-ready-fourth", 29, 10,
                new[] { "HA0h", "U30h", "MC0h" }.Concat(kingRedIngredients).ToArray(),
                lumber: 3, destructionKing: true);
            Check(kingRedRace.Frame.GuidePlan is { Stage: BulletGuideStage.DestructionRace, TargetUnitId: "rawcode:H30h" } &&
                kingRedRace.Decision.Kind == CoachActionKind.Craft,
                "Ready safe fourth legend was hidden behind an unready Shiki target.");
            var noGain = Observe("shared-buff-no-gain", 60, 13,
                ["180h", "U30h", "540h", "O30h", "A20h", "H20h", "510h"]);
            Check(noGain.Frame.GuidePlan?.TargetUnitId is not null and not "rawcode:Y30h",
                "Shared B029 no-gain candidate remained the armor target.");
            // Overnight additions are controlled observations, never live game evidence.
            Observe("overnight-new-session", 2, 0, ["300h"]);
            Observe("overnight-navigation", 21, 9, ["HA0h", "U30h", "MC0h"]);
            WaitCoach(main, () => ((Button)overlay.BeginnerView.FindName("ConfirmButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), (_, frame) => frame.ConfirmedNavigation == BulletGuidePolicy.NavigationId);
            var reservedUnit = new CombatUnitState(501, "h00N", 0, null, CombatUnitKind.LocalUnit,
                new(0, 0), 100, 100, 0, false, false)
            { UncommonSaleAbility = new("A0B8", 1, 0) };
            var reserved = Observe("overnight-uncommon-reserved", 50, 13, ["180h", "N00h"],
                combatObservations: [reservedUnit]);
            Check(reserved.Frame.GuidePlan is { Stage: BulletGuideStage.BossSupport, TargetUnitId: "rawcode:U30h" },
                "Usopp plus Bullet did not follow live Plan BossSupport targeting Red Force.");
            Check(!reserved.Decision.Id.StartsWith("guide1:sell-uncommon:", StringComparison.Ordinal),
                "Reserved Red Force material was offered as unused uncommon sale.");
            var saleCodes = supported.Concat(new[] { "830h", "A00h" }).ToArray();
            var saleUnit = new CombatUnitState(504, "h00A", 0, null, CombatUnitKind.LocalUnit,
                new(0, 0), 100, 100, 0, false, false)
            { UncommonSaleAbility = new("A0B8", 1, 0) };
            var saleReady = Observe("overnight-uncommon-sale", 50, 13, saleCodes,
                combatObservations: [saleUnit]);
            Check(saleReady.Frame.GuidePlan is { Stage: BulletGuideStage.Operating, TargetUnitId: null },
                "Surplus uncommon fixture did not reach live Plan Operating.");
            Check(saleReady.Decision.Id.StartsWith("guide1:sell-uncommon:", StringComparison.Ordinal),
                "Verified unused uncommon sale did not reach the controlled production UI.");
            var saleUnknown = Observe("overnight-uncommon-unknown", 50, 13, saleCodes);
            Check(!saleUnknown.Decision.Id.StartsWith("guide1:sell-uncommon:", StringComparison.Ordinal),
                "Missing native sale attachment was promoted to actionable UI.");
            var shipUnit = new CombatUnitState(502, "h05Y", 0, null, CombatUnitKind.LocalUnit,
                new(0, 0), 100, 100, 0, false, false)
            { AncientShipAbility = new("A0KB", 1, 0) };
            var shipReady = Observe("overnight-ancient-ship", 50, 13,
                ["180h", "530h", "510h", "Y50h"], lumber: 100, combatObservations: [shipUnit]);
            Check(shipReady.Decision.Id == "guide1:ancient-to-pirate",
                "Current red-force ship shortage did not reach deterministic conversion UI.");
            var shipConsumed = Observe("overnight-ancient-consumed", 50, 13,
                ["180h", "530h", "510h", "060h"], lumber: 100 - 8);
            Check(shipConsumed.Decision.Id != "guide1:ancient-to-pirate",
                "Consumed ancient ship remained actionable in the production UI.");
            Observe("overnight-milestone-50-no-common", 50, 13, ["180h"], runtime:
                new BulletGuideRuntimeState(true, 3, 1, 0, "제어 관측")
                    .WithExactCounts(new(0, 1, 30)));
            foreach (var surface in new[] { view, overlay.BeginnerView })
                Check(((TextBlock)surface.FindName("ReadinessSummary")).Text.Contains("50라 최소 목표 달성."),
                    "Known round-50 milestone was hidden when no common sacrifice was available.");
            Observe("overnight-milestone-60-max", 60, 13, ["180h"], runtime:
                new BulletGuideRuntimeState(true, 3, 3, 0, "제어 관측")
                    .WithExactCounts(new(0, 30, 30)));
            foreach (var surface in new[] { view, overlay.BeginnerView })
                Check(((TextBlock)surface.FindName("ReadinessSummary")).Text.Contains("60라 풀강 목표 달성"),
                    "Completed round-60 milestone did not reach both rendered surface controls.");
            var placedBullet = new CombatUnitState(503, "h081", 0, null, CombatUnitKind.Bullet,
                new(-4660, 6032), 100, 100, 0, false, false);
            Observe("overnight-placement-distance", 60, 13, ["180h"], combatObservations: [placedBullet]);
            foreach (var surface in new[] { view, overlay.BeginnerView })
                Check(((TextBlock)surface.FindName("ReadinessSummary")).Text.Contains("현재 거리 500"),
                    "Current spawn distance failed to reach both production surface controls.");
            Observe("overnight-placement-unknown", 60, 13, ["180h"]);
            foreach (var surface in new[] { view, overlay.BeginnerView })
                Check(!((TextBlock)surface.FindName("ReadinessSummary")).Text.Contains("현재 거리 500"),
                    "Missing position retained stale placement information.");
            var blackMariaUnit = new CombatUnitState(504, "h04U", 0, null, CombatUnitKind.LocalUnit,
                new(0, 0), 100, 100, 0, false, false) { BlackMariaSelectedMode = BlackMariaMode.Burn };
            var blackMariaStun = Observe("blackmaria-stun-shortage", 60, 13,
                ["180h", "U40h", "Q30h", "M30h", "K50h"], combatObservations: [blackMariaUnit]);
            Check(blackMariaStun.Decision.Id == "guide1:blackmaria:Stun" &&
                blackMariaStun.Frame.GuidePlan?.Support is { SlowPotential: 92, StunPairReady: false },
                "BlackMaria stun role did not use the actual support plan.");
            CheckBlackMariaRendered("스턴 부족 → 스턴 선택 권고", "선택 관측: 화상");
            var blackMariaSlow = Observe("blackmaria-slow-shortage", 60, 13,
                ["180h", "U40h", "Z20h"], combatObservations: [blackMariaUnit]);
            Check(blackMariaSlow.Decision.Id == "guide1:blackmaria:Slow" &&
                blackMariaSlow.Frame.GuidePlan?.Support is { SlowPotential: 7, StunPairReady: true },
                "BlackMaria selected mode was credited as active slow.");
            CheckBlackMariaRendered("이감 부족 → 이감 선택 권고", "선택 관측: 화상");
            var blackMariaBoth = Observe("blackmaria-both-shortages", 60, 13,
                ["180h", "U40h"], combatObservations: [blackMariaUnit]);
            Check(blackMariaBoth.Decision.Id is not ("guide1:blackmaria:Stun" or "guide1:blackmaria:Slow"),
                "Both missing roles produced an invented single-mode priority.");
            CheckBlackMariaRendered("스턴·이감 모두 부족", "고정 우선순위 없음");
            Observe("blackmaria-selection-unknown", 60, 13, ["180h", "U40h", "Z20h"]);
            CheckBlackMariaRendered("이감 부족 → 이감 선택 권고", "선택 관측: 미확인");
            Observe("blackmaria-foreign", 60, 13, ["180h", "U40h", "Z20h"],
                combatObservations: [blackMariaUnit with { Owner = 7, Kind = CombatUnitKind.RecipeExemplar }]);
            CheckBlackMariaRendered("이감 부족 → 이감 선택 권고", "선택 관측: 미확인");
            var blackMariaDisconnected = Observe("blackmaria-disconnected", 60, 13,
                ["180h", "U40h", "Z20h"], combatObservations: [blackMariaUnit],
                recognitionState: RecognitionState.TransientReadError);
            Check(!blackMariaDisconnected.Frame.IsCurrent && blackMariaDisconnected.Frame.CombatObservations.IsEmpty,
                "BlackMaria disconnect retained selection or role advice");
            foreach (var surface in new[] { view, overlay.BeginnerView })
                Check(!((TextBlock)surface.FindName("ReadinessSummary")).Text.Contains("왜곡 블랙마리아") &&
                    !((TextBlock)surface.FindName("OperationText")).Text.Contains("선택 관측: 화상"),
                    "BlackMaria disconnect retained selection or role advice");
            // Fixture scan evidence only; exact wood menu and native/live execution remain unknown.
            var sHawk = new CombatUnitState(601, "h0A3", 0, null, CombatUnitKind.LocalUnit,
                new(0, 0), 100, 100, null, false, false);
            var nasjuroLive = Observe("nasjuro-live", 60, 13, ["180h", "3A0h", "060h"],
                gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Controlled planning identity; current effects unverified", []), combatObservations: [sHawk], transcendenceWisps: 1);
            Check(nasjuroLive.Frame.Gorosei is { IsCurrent: true, Mode: GoroseiMode.Nasjuro } &&
                nasjuroLive.Frame.Gorosei.RecognitionRevision == nasjuroLive.Frame.RecognitionRevision &&
                nasjuroLive.Decision.Id.StartsWith("guide1:nasjuro-wisp:", StringComparison.Ordinal) &&
                nasjuroLive.Decision.TargetUnitId is null && nasjuroLive.Decision.RewardWispId is null,
                "Identity-only Nasjuro detailed scan did not reach safe planning advice; not live verification.");
            CheckNasjuroRendered(true);
            Observe("nasjuro-dead", 60, 13, ["180h", "3A0h", "060h"], gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Controlled planning identity; current effects unverified", []),
                combatObservations: [sHawk with { Life = 0 }], transcendenceWisps: 1);
            CheckNasjuroRendered(false);
            Observe("nasjuro-unobserved", 60, 13, ["180h", "3A0h", "060h"],
                gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Controlled planning identity; current effects unverified", []), transcendenceWisps: 1);
            CheckNasjuroRendered(false);
            var noWisp = Observe("nasjuro-no-wisp", 60, 13, ["180h", "3A0h", "060h"],
                gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Controlled planning identity; current effects unverified", []), combatObservations: [sHawk]);
            Check(noWisp.Frame.RewardWisps.Count == 0 && noWisp.Decision.RewardWispId is null,
                "Direction invented an available wisp or exact choice.");
            CheckNasjuroRendered(true);
            var nasjuroNone = Observe("nasjuro-none", 60, 13, ["180h", "3A0h", "060h"], combatObservations: [sHawk]);
            Check(!nasjuroNone.Frame.Gorosei.IsCurrent && nasjuroNone.Frame.Gorosei.Mode == GoroseiMode.None &&
                nasjuroNone.Frame.RecognitionRevision > noWisp.Frame.RecognitionRevision,
                "Same inventory None failed to create a new current-scan frame.");
            CheckNasjuroRendered(false);
            var protectedShip = Observe("nasjuro-ship-protected", 60, 13, ["180h", "3A0h", "060h", "Y50h"],
                gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Controlled planning identity; current effects unverified", []), combatObservations: [sHawk, shipUnit], lumber: 100, transcendenceWisps: 1);
            Check(protectedShip.Frame.GuidePlan?.TargetUnitId == "rawcode:U30h" &&
                protectedShip.Decision.PreservedMaterialCounts.GetValueOrDefault("rawcode:060h") == 1 &&
                protectedShip.Decision.Id is not ("guide1:ancient-to-pirate" or "guide1:select-rayleigh-ship"),
                "Actual guide target lost its reserved pirate ship.");
            CheckNasjuroRendered(true);
            var shipShortage = Observe("nasjuro-ship-shortage", 60, 13, ["180h", "3A0h"],
                gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Controlled planning identity; current effects unverified", []), combatObservations: [sHawk], transcendenceWisps: 1);
            Check(shipShortage.Decision.OperationGuide.Contains("추가 배 부족") &&
                !shipShortage.Decision.OperationGuide.Contains("8:2"), "Ship demand did not change the actual direction.");
            CheckNasjuroRendered(false);
            var upgradeCodes = supported.Concat(new[] { "3A0h" }).ToArray();
            var upgradeWisp = Observe("nasjuro-upgrade-wisp", 60, 13, upgradeCodes,
                gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Controlled planning identity; current effects unverified", []), combatObservations: [sHawk], transcendenceWisps: 1,
                runtime: new(true, 2, 1, 1, "controlled tiers"));
            Check(upgradeWisp.Decision.Id.StartsWith("guide1:nasjuro-wisp:", StringComparison.Ordinal),
                "Current wisp did not reach conditional guidance.");
            var upgradeSpent = Observe("nasjuro-upgrade-spent", 60, 13, upgradeCodes,
                gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Controlled planning identity; current effects unverified", []), combatObservations: [sHawk],
                runtime: new(true, 2, 1, 1, "controlled tiers"));
            Check(upgradeSpent.Decision.Id == "guide1:upgrade:armor" &&
                upgradeSpent.Decision.OperationGuide.Contains("8:2"), "Spent wisp permanently hid the next eligible upgrade.");
            CheckNasjuroRendered(true);
            var nasjuroDisconnected = Observe("nasjuro-disconnected", 60, 13, ["180h", "3A0h"],
                gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Controlled planning identity; current effects unverified", []), combatObservations: [sHawk], recognitionState: RecognitionState.TransientReadError);
            Check(!nasjuroDisconnected.Frame.Gorosei.IsCurrent && nasjuroDisconnected.Frame.CombatObservations.IsEmpty,
                "Disconnected Nasjuro retained fresh marker/combat.");
            var resetWarcury = Observe("nasjuro-reset-warcury", 2, 0, ["300h"], gorosei: GoroseiMode.Warcury, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Warcury, "Fixture identity only; before round 50, no active effect", []));
            Check(resetWarcury.Frame.MatchGeneration > nasjuroLive.Frame.MatchGeneration &&
                resetWarcury.Frame.Gorosei is { IsCurrent: true, Mode: GoroseiMode.Warcury } &&
                resetWarcury.Frame.GuidePlan?.Support?.ArmorTarget == 120 && !resetWarcury.Frame.Gorosei.EffectsActiveVerified,
                "Latest user correction: new match plans Warcury 120 before round 50 without applying current effect.");
            var resetNasjuro = Observe("nasjuro-reset-nasjuro", 2, 0, ["180h", "3A0h"],
                gorosei: GoroseiMode.Nasjuro, goroseiMarker: new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Nasjuro, "Fixture identity only; before round 50, no active effect", []), combatObservations: [sHawk]);
            Check(resetNasjuro.Frame.MatchGeneration > resetWarcury.Frame.MatchGeneration &&
                resetNasjuro.Frame.Gorosei is { IsCurrent: true, Mode: GoroseiMode.Nasjuro } &&
                resetNasjuro.Frame.CombatObservations.IsEmpty && !resetNasjuro.Decision.OperationGuide.Contains("8:2"),
                "Nasjuro boundary discarded first fresh marker or reused combat");
            var resetNone = Observe("nasjuro-reset-none", 2, 0, ["300h"]);
            Check(!resetNone.Frame.Gorosei.IsCurrent && resetNone.Frame.GuidePlan?.Support?.ArmorTarget == 100,
                "None boundary reused previous match armor/marker.");
            QueenEventChecks();
            StopEventProbe();
            WriteEvidenceText(Path.Combine(output, "bullet-guide-ui.json"), JsonSerializer.Serialize(new
            {
                Kind = "real-WPF-main-overlay-production-scan-pipeline-controlled-recognition",
                Limitation = "Controlled selected identities plan completed-deck specs from early rounds; current native effects remain unverified. Not a live Warcraft clear or native validation.", Rows = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"BULLET_GUIDE_UI PASS scenarios={rows.Count}");
        }
        finally
        {
            main.Close(); overlay.Stats.CloseForApplication(); overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        (CoachDecision Decision, CoachFrame Frame) Observe(string name, int round, int story, string[] codes, int reward = 0,
            BulletGuideRuntimeState? runtime = null, long? lumber = null, bool greenBlood = false,
            GoroseiMode gorosei = GoroseiMode.None, bool? highGambleQuest = null,
            int? loadedClearCount = null, bool? destructionKing = null, HelperUnitState? helperState = null,
            RecognitionState recognitionState = RecognitionState.Ready,
            ImmutableArray<CombatUnitState> combatObservations = default, long? traits = null, int selectionWisps = 0,
            int transcendenceWisps = 0, GoroseiMarkerSnapshot? goroseiMarker = null,
            NativeNavigationSnapshot? nativeNavigation = null)
        {
            var entries = codes.GroupBy(code => code).Select(group => new InventoryEntry
                { UnitId = catalog.AllUnits.First(unit => unit.Rawcodes.Contains(group.Key)).Id, Count = group.Count() }).ToList();
            if (greenBlood) entries.Add(new InventoryEntry { UnitId = "item_greenblood", Count = 1 });
            var fields = new List<RuntimeRecommendationField>();
            if (lumber is { } wood) fields.Add(new(PlanningValue.Known("lumber", wood), 10000, []));
            if (traits is { } points) fields.Add(new(PlanningValue.Known("trait-points", points), 10000, []));
            var wisps = ImmutableDictionary<string, int>.Empty;
            if (reward > 0) wisps = wisps.Add("e019", reward);
            if (selectionWisps > 0) wisps = wisps.Add("e018", selectionWisps);
            if (transcendenceWisps > 0) wisps = wisps.Add("e01A", transcendenceWisps);
            recognizer.Next = new RecognitionResult
            {
                State = recognitionState, Entries = entries, Status = "Guide1 제어 선택 관측 · 현재 효과 검증 아님",
                HelperState = helperState,
                CombatObservations = combatObservations.IsDefault ? [] : combatObservations,
                LoadedClearCount = loadedClearCount,
                ConfirmsSessionBoundary = BulletGuideRowProjection.ConfirmsSessionBoundary(name),
                GuideRuntime = runtime ?? BulletGuideRuntimeState.Unknown,
                RecommendationInputs = fields.Count > 0 ? new NavigationStateSnapshot(
                    ((AdaptivePlanningCompositionRoot)typeof(MainWindow).GetField("_adaptivePlanning", flags)!.GetValue(main)!).MatchGeneration,
                    ++signalRevision, RuntimeRecommendationSnapshotState.Current, true,
                    fields.ToImmutableArray()) : null,
                MapSignals = new MapSignals(Math.Min(14, story + 1), null, story, wisps)
                {
                    NativeNavigation = nativeNavigation ?? NativeNavigationSnapshot.Unknown,
                    DestructionKingAvailable = destructionKing,
                    RouteQuests = highGambleQuest is { } complete ? RouteQuestSnapshot.FromVerifiedSlots(
                        [new(0, "Q006", complete), new(1, "Q001", false), new(2, "Q002", false)]) :
                        RouteQuestSnapshot.Unknown
                },
                Diagnostics = BulletSyntheticGoroseiProducer.Diagnostics(round, entries, gorosei, goroseiMarker, null)
            };
            var request = recognizer.Next;
            var expectedGeneration = ((AdaptivePlanningCompositionRoot)typeof(MainWindow)
                .GetField("_adaptivePlanning", flags)!.GetValue(main)!).MatchGeneration +
                (RecognitionPolicy.ShouldResetBeforeReadyInventory(request) ? 1 : 0);
            var expectedRevision = (long)typeof(MainWindow).GetField("_recognitionRevision", flags)!.GetValue(main)! + 1;
            var previous = (CoachFrame?)typeof(MainWindow).GetField("_lastCoachFrame", flags)!.GetValue(main);
            var expectedInventory = request.ShouldReplaceInventory
                ? entries.ToImmutableDictionary(entry => entry.UnitId, entry => entry.Count)
                : previous?.Inventory ?? ImmutableDictionary<string, int>.Empty;
            var expectedMarker = request.ShouldReplaceInventory ? request.Diagnostics.GoroseiMarker : GoroseiMarkerSnapshot.Unknown;
            var result = WaitCoach(main, () => _ = ScanFixtureAsync(main, request),
                (_, frame) => BulletGuideRowProjection.MatchesObservation(frame, round, expectedGeneration,
                    expectedRevision, expectedInventory, expectedMarker));
            rows.Add(BulletGuideRowProjection.Observe(name, result.Decision, result.Frame));
            Capture(name);
            Console.WriteLine($"GUIDE1 {name} stage={result.Frame.GuidePlan?.Stage} action={result.Decision.Kind} gorosei={result.Frame.Gorosei.Source} evidence={result.Frame.Gorosei.EffectEvidence}");
            return result;
        }

        void StopEventProbe()
        {
            ImmutableArray<CombatUnitState> oldCombat =
            [new(9901, "h081", 0, null, CombatUnitKind.Bullet, new(0, 0), 100, 100, 0, false, false)];
            var stopMarker = new GoroseiMarkerSnapshot(GoroseiMarkerStatus.SelectedIdentity,
                GoroseiMode.Nasjuro, "Stop fixture identity, not verified effect", []);
            var stopNative = new NativeNavigationSnapshot(NativeNavigationStatus.Selected,
                BulletGuidePolicy.NavigationId, "Stop fixture native snapshot");
            var seen = Observe("stop-event-old-combat", 60, 13, ["180h", "HA0h"],
                gorosei: GoroseiMode.Nasjuro, combatObservations: oldCombat,
                goroseiMarker: stopMarker, nativeNavigation: stopNative);
            Check(seen.Frame.Gorosei.IsCurrent && seen.Frame.Gorosei.Marker == stopMarker &&
                seen.Frame.NativeNavigation.Status == NativeNavigationStatus.Selected,
                "Stop detailed/native precondition missing.");
            Check(seen.Frame.CombatObservations.SequenceEqual(oldCombat), "Stop precondition has no old combat.");
            WaitCoach(main, () => ((ComboBox)view.FindName("QueenCondition")).SelectedValue =
                QueenConversionInput.UserConfirmedMissionsComplete,
                (_, frame) => frame.GuidePlan?.QueenInput == QueenConversionInput.UserConfirmedMissionsComplete);
            WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                PlayModes.Options.Single(option => option.Mode == PlayMode.Manual), (_, frame) => frame.Mode == PlayMode.Manual);
            // Boundary setup only: no fabricated CoachFrame and no runtime gate override.
            var automatic = (Dictionary<string, InventoryEntry>)typeof(MainWindow).GetField("_automatic", flags)!.GetValue(main)!;
            automatic.Clear();
            var beforeGeneration = (int)typeof(MainWindow).GetField("_scanGeneration", flags)!.GetValue(main)!;
            var scan = (CheckBox)main.FindName("AutoScanCheck");
            scan.IsChecked = false;
            scan.RaiseEvent(new RoutedEventArgs(CheckBox.UncheckedEvent));
            var afterGeneration = (int)typeof(MainWindow).GetField("_scanGeneration", flags)!.GetValue(main)!;
            var retainedCombat = (ImmutableArray<CombatUnitState>)typeof(MainWindow).GetField("_combatObservations", flags)!.GetValue(main)!;
            WriteEvidenceText(Path.Combine(output, "stop-event-probe.json"), JsonSerializer.Serialize(new
            {
                Kind = "actual-unchecked-event-fixture-isolation-probe-not-stop-validation",
                Mode = PlayMode.Manual, AutomaticCount = automatic.Count,
                OldCombatCount = oldCombat.Length, RetainedCombatCount = retainedCombat.Length,
                ProductionStopBranchExecuted = afterGeneration > beforeGeneration,
                beforeGeneration, afterGeneration,
                Limitation = "Runtime guard prevents Fixture from executing production Stop body"
            }, new JsonSerializerOptions { WriteIndented = true }));
            Check(afterGeneration == beforeGeneration && retainedCombat.SequenceEqual(oldCombat),
                "Fixture guard did not block the production Unchecked body.");
            Console.WriteLine("STOP_EVENT_PROBE NOT_VALIDATED runtime-isolation-guard");
            var manualNavigation = (NavigationSessionState)typeof(MainWindow).GetField("_navigationSession", flags)!.GetValue(main)!;
            manualNavigation.Confirm("AlliedForces.EmergencyCall"); // explicit fixture user provenance, not native
            var queenBefore = (QueenConversionInput)typeof(MainWindow).GetField("_queenInput", flags)!.GetValue(main)!;
            var stopped = WaitCoach(main, () => main.StopControlledObservation(),
                (_, frame) => frame.Mode == PlayMode.Manual && !frame.IsCurrent);
            Check(stopped.Decision.Kind == CoachActionKind.Recognition && stopped.Frame.CombatObservations.IsEmpty &&
                stopped.Frame.NativeNavigation.Status == NativeNavigationStatus.Unknown && !stopped.Frame.Gorosei.IsCurrent &&
                stopped.Frame.Gorosei.Marker?.Status == GoroseiMarkerStatus.Unknown,
                "Common Stop transition retained current planner/native/combat/Gorosei detail.");
            Check(stopped.Frame.ConfirmedNavigation == manualNavigation.ConfirmedOptionId &&
                (QueenConversionInput)typeof(MainWindow).GetField("_queenInput", flags)!.GetValue(main)! == queenBefore,
                "Common Stop discarded independent user provenance.");
            var commonGeneration = (int)typeof(MainWindow).GetField("_scanGeneration", flags)!.GetValue(main)!;
            Check(commonGeneration > afterGeneration && stopped.Frame.RecognitionRevision > seen.Frame.RecognitionRevision,
                "Common Stop did not fence old results.");
            Capture("stop-common-transition-manual-zero");
            rows.Add(BulletGuideRowProjection.StopZero(stopped.Decision, stopped.Frame));
            scan.IsChecked = true; // guarded event stays non-runtime; only the next controlled result resumes
            var resumed = Observe("stop-common-transition-resumed", 60, 13, ["180h", "HA0h"],
                gorosei: GoroseiMode.Nasjuro, combatObservations: oldCombat,
                goroseiMarker: stopMarker, nativeNavigation: stopNative);
            Check(resumed.Frame.IsCurrent && resumed.Frame.CombatObservations.SequenceEqual(oldCombat) &&
                resumed.Frame.RecognitionRevision > stopped.Frame.RecognitionRevision &&
                resumed.Frame.MatchGeneration == stopped.Frame.MatchGeneration,
                "Fresh controlled scan did not resume the stopped session.");
            scan.IsChecked = false;
            var retained = WaitCoach(main, () => main.StopControlledObservation(), (_, frame) => !frame.IsCurrent);
            Check(retained.Frame.Inventory.Count == resumed.Frame.Inventory.Count &&
                retained.Frame.Inventory.All(pair => resumed.Frame.Inventory.GetValueOrDefault(pair.Key) == pair.Value),
                "Stop discarded inventory retained in Manual mode.");
            Capture("stop-common-transition-manual-retained");
            rows.Add(BulletGuideRowProjection.StopRetained(retained.Decision, retained.Frame));
            WaitCoach(main, () => ((ComboBox)main.FindName("PlayModeCombo")).SelectedItem =
                PlayModes.Options.Single(option => option.Mode == PlayMode.Guide), (_, frame) => frame.Mode == PlayMode.Guide);
            scan.IsChecked = true; // reset observation still uses only the controlled scan path
            var reset = Observe("stop-event-reset", 2, 0, ["300h"]);
            Check(((ImmutableArray<CombatUnitState>)typeof(MainWindow).GetField("_combatObservations", flags)!.GetValue(main)!).IsEmpty,
                "Actual reset retained combat after Stop probe.");
            Check(reset.Frame.MatchGeneration > seen.Frame.MatchGeneration && reset.Frame.CombatObservations.IsEmpty,
                "Stop probe reset reused old combat/generation.");
        }

        void QueenEventChecks()
        {
            var owned = Observe("queen-event-owned", 60, 13, ["180h", "HA0h", "300h"]);
            var inputs = new[] { QueenConversionInput.UserConfirmedMissionsComplete,
                QueenConversionInput.UserConfirmedStoryTooSlow };
            var surfaces = new[] { view, overlay.BeginnerView };
            for (var index = 0; index < surfaces.Length; index++)
            {
                var combo = (ComboBox)surfaces[index].FindName("QueenCondition");
                Check(combo.IsEnabled && combo.IsVisible, "Queen input was not usable on its rendered surface.");
                var input = inputs[index];
                var changed = WaitCoach(main, () => combo.SelectedValue = input,
                    (_, frame) => frame.GuidePlan?.QueenInput == input);
                Check(changed.Frame.GuideRuntime == owned.Frame.GuideRuntime,
                    "User evidence is not native observation");
                foreach (var surface in surfaces)
                    Check(Equals(((ComboBox)surface.FindName("QueenCondition")).SelectedValue, input),
                        "Queen user input disagrees between main and overlay");
                var stale = changed.Frame;
                foreach (var rejected in new[] { stale with { Revision = stale.Revision - 1 },
                    stale with { MatchGeneration = stale.MatchGeneration - 1 } })
                {
                    // Re-render an old view token; dispatch the actual SelectionChanged event.
                    surfaces[index].Render(changed.Decision, rejected, catalog.Unit(BulletGuidePolicy.GoalId));
                    combo.SelectedValue = QueenConversionInput.KeepKing;
                    Check((QueenConversionInput)typeof(MainWindow).GetField("_queenInput", flags)!.GetValue(main)! == input,
                        "Stale rendered Queen token was accepted.");
                    surfaces[index].Render(changed.Decision, changed.Frame, catalog.Unit(BulletGuidePolicy.GoalId));
                }
                Capture($"queen-event-surface-{index}");
                rows.Add(BulletGuideRowProjection.Queen(index, changed.Decision, changed.Frame, input));
            }
            var reset = Observe("queen-event-reset", 2, 0, ["HA0h", "300h"]);
            Check((QueenConversionInput)typeof(MainWindow).GetField("_queenInput", flags)!.GetValue(main)! ==
                QueenConversionInput.Unknown, "Actual reset retained Queen user confirmation.");
            Check(reset.Frame.MatchGeneration > owned.Frame.MatchGeneration &&
                reset.Frame.GuidePlan?.QueenInput == QueenConversionInput.Unknown &&
                reset.Frame.CombatObservations.IsEmpty, "Queen reset reused old session evidence.");
            foreach (var surface in surfaces)
                Check(Equals(((ComboBox)surface.FindName("QueenCondition")).SelectedValue, QueenConversionInput.Unknown),
                    "Queen reset failed to synchronize both surfaces.");
        }

        void CheckNasjuroRendered(bool actual)
        {
            foreach (var surface in new[] { view, overlay.BeginnerView })
            {
                var operation = (TextBlock)surface.FindName("OperationText");
                var unknown = (TextBlock)surface.FindName("UnknownText");
                surface.UpdateLayout();
                Check(operation.IsVisible && unknown.IsVisible && operation.Text.Contains("8:2") == actual &&
                    operation.Text.Contains("메뉴 미확인"),
                    "Nasjuro None retained actual guidance on main or overlay");
            }
        }

        void CheckBlackMariaRendered(string role, string selection)
        {
            foreach (var surface in new[] { view, overlay.BeginnerView })
            {
                var support = (TextBlock)surface.FindName("ReadinessSummary");
                var operation = (TextBlock)surface.FindName("OperationText");
                surface.UpdateLayout();
                Check(support.IsVisible && operation.IsVisible &&
                    support.Text.Contains(role) && support.Text.Contains(selection) &&
                    operation.Text.Contains(role) && operation.Text.Contains(selection),
                    "BlackMaria render did not match support/operation on main and overlay");
            }
        }

        void Capture(string name)
        {
            if (name.StartsWith("clear-count", StringComparison.Ordinal))
                foreach (var surface in new[] { view, overlay.BeginnerView })
                {
                    var panel = (Expander)surface.FindName("ClearRewardsPanel");
                    panel.IsExpanded = true;
                    DependencyObject node = (TextBlock)surface.FindName("OperationText");
                    while (node is not Expander)
                        node = LogicalTreeHelper.GetParent(node) ??
                            System.Windows.Media.VisualTreeHelper.GetParent(node) ??
                            throw new InvalidOperationException("Operation expander parent was not found.");
                    ((Expander)node).IsExpanded = false;
                    ((ScrollViewer)surface.FindName("CoachScroll")).ScrollToBottom();
                }
            if (name.StartsWith("high-gamble", StringComparison.Ordinal) ||
                name.StartsWith("blackmaria-", StringComparison.Ordinal) ||
                name.StartsWith("nasjuro-", StringComparison.Ordinal))
                foreach (var surface in new[] { view, overlay.BeginnerView })
                {
                    DependencyObject node = (TextBlock)surface.FindName("OperationText");
                    while (node is not Expander)
                        node = LogicalTreeHelper.GetParent(node) ??
                            System.Windows.Media.VisualTreeHelper.GetParent(node) ??
                            throw new InvalidOperationException("Operation expander parent was not found.");
                    ((Expander)node).IsExpanded = true;
                    ((ScrollViewer)surface.FindName("CoachScroll")).ScrollToBottom();
                }
            if (name.StartsWith("stop-common-transition", StringComparison.Ordinal) ||
                name.StartsWith("overnight-uncommon", StringComparison.Ordinal) ||
                name.StartsWith("overnight-ancient", StringComparison.Ordinal))
                foreach (var surface in new[] { view, overlay.BeginnerView })
                    ((ScrollViewer)surface.FindName("CoachScroll")).ScrollToTop();
            overlay.Show();
            overlay.Left = SystemParameters.VirtualScreenLeft - overlay.Width - 20;
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                ApplyScale(overlay, scale); overlay.UpdateLayout(); main.UpdateLayout();
                if (name.StartsWith("overnight-milestone", StringComparison.Ordinal) ||
                    name.StartsWith("overnight-placement", StringComparison.Ordinal))
                    foreach (var surface in new[] { view, overlay.BeginnerView })
                    {
                        ((TextBlock)surface.FindName("ReadinessSummary")).BringIntoView();
                        surface.UpdateLayout();
                    }
                SaveCoachWindow(overlay, Path.Combine(output, $"overlay-{name}-{scale * 100:0}.png"));
            }
            ApplyScale(overlay, 1);
            SaveCoachWindow(main, Path.Combine(output, $"main-{name}.png"));
        }
        static IEnumerable<DependencyObject> Descendants(DependencyObject node)
        {
            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); index++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, index);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
