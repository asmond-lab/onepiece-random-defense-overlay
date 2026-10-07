namespace OrandOverlay;

/// <summary>Image-source sequence. Counts are actual owned units, never planned composition.</summary>
public sealed class BulletGuidePolicy(DataCatalog catalog)
{
    public const string GoalId = "rawcode:180h";
    public const string NavigationId = "PathOfKings.BountyHunter";
    // Source groups, not the older bullet-strategy file (which contains different IDs).
    internal static readonly string[][] OpeningGroups =
    [
        ["530h", "HA0h", "U20h", "MC0h", "M30h", "B30h"],
        ["340h", "630h", "H30h", "V20h"],
        ["Z20h", "N30h", "F30h", "930h", "K30h", "S30h"],
        ["830h", "W20h", "O20h", "X20h", "G30h"]
    ];
    // Source roles include component hands and conditional support, never nominal aura credits.
    internal static readonly string[] EarlyRoleCodes =
        ["rawcode:U20h", "rawcode:V20h", "rawcode:930h", "rawcode:MC0h", "rawcode:530h",
         "rawcode:HA0h", "rawcode:U30h", "rawcode:M30h", "rawcode:630h", "rawcode:B30h",
         "rawcode:Z20h", "rawcode:W20h", "rawcode:O30h", "rawcode:Y30h", "rawcode:540h"];
    internal static readonly string[] ComponentCodes = ["U20h", "V20h", "930h"];
    internal static readonly string[] AirCodes = ["HA0h", "U30h", "930h", "K30h", "F30h"];
    internal static readonly string[] BossCodes = ["U30h", "540h", "MC0h"];
    internal static readonly string[] RetainedCodes =
        ["H20h", "K50h", "B20h", "D20h", "X90h", "K20h", "C20h", "V10h", "E20h",
         "E10h", "610h", "Y00h", "A10h", "F10h", "D10h"];

    // Inventory is a lower-bound observation, never a native T000 success receipt.
    public FastUniqueState ObserveFastUnique(FastUniqueState previous, int round,
        IReadOnlyDictionary<string, int> inventory, bool isCurrent) =>
        previous == FastUniqueState.CompletedVerified ? previous : isCurrent && round is > 0 and < 8 &&
            inventory.Any(pair => pair.Value > 0 && TopGradePolicy.BaseTier(catalog.Unit(pair.Key).Tier) == "희귀함")
                ? FastUniqueState.RarePreviouslyObserved : previous;

    public BulletGuidePlan Plan(int round, int story, IReadOnlyDictionary<string, int> inventory, string difficulty,
        string? confirmedNavigation = null, GoroseiMode gorosei = GoroseiMode.None,
        RouteQuestSnapshot? routeQuests = null, IEnumerable<string>? observedLegendIds = null,
        bool? destructionKingAvailable = null, QueenConversionInput queenInput = QueenConversionInput.Unknown,
        IReadOnlyDictionary<string, int>? observedLegendCounts = null, FastUniqueState fastUnique = FastUniqueState.Unknown,
        int selectionWisps = 0, BulletGuideLearningBridge? learning = null)
    {
        var owned = inventory.Where(pair => pair.Value > 0).ToDictionary(pair => catalog.Unit(pair.Key).Id,
            pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        bool Has(string code) => owned.GetValueOrDefault("rawcode:" + code) > 0;
        var bullet = owned.GetValueOrDefault(GoalId) > 0;
        if (round <= 0)
            return new(BulletGuideStage.RoundUnknown, null, bullet)
            {
                Round = round, Difficulty = difficulty, ConfirmedNavigation = confirmedNavigation,
                FastUnique = fastUnique, ProtectedUnitIds = owned.Keys.ToArray()
            };
        // Mobility counts physical owned bodies, not distinct aura/buff groups.
        var air = AirCodes.Sum(code => owned.GetValueOrDefault("rawcode:" + code));
        var boss = BossCodes.Count(Has);
        var bossException = Has("U30h") && (Has("F30h") || Has("H50h"));
        var components = ComponentCodes.Count(Has);
        var queenConfirmed = queenInput is QueenConversionInput.UserConfirmedMissionsComplete or
            QueenConversionInput.UserConfirmedStoryTooSlow;

        var support = new BulletGuideSupportPolicy(catalog).Evaluate(inventory, confirmedNavigation, gorosei);
        // Each historical type proves at least one body; current multiplicity is stronger
        // only for that type. Never sum duplicate history entries or infer consumed copies.
        var historicalCounts = observedLegendCounts?.Where(pair => pair.Value > 0)
            .ToDictionary(pair => catalog.Unit(pair.Key).Id, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var legends = owned.Keys.Concat(observedLegendIds ?? []).Concat(historicalCounts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(id => id != "rawcode:S80h")
            .Where(id => TopGradePolicy.BaseTier(catalog.Unit(id).Tier) is "전설" or "히든" or "해적선")
            .Sum(id => Math.Max(Math.Max(1, owned.GetValueOrDefault(id)), historicalCounts.GetValueOrDefault(id)));
        bool CandidateAllowed(string id) => (id != "rawcode:IC0h" || queenConfirmed) &&
            !catalog.Unit(id).Recipe.Any(pair => catalog.Unit(pair.Key).Tier != "자원" &&
                owned.GetValueOrDefault(pair.Key) >= pair.Value &&
                Available(id).GetValueOrDefault(pair.Key) < pair.Value) && (catalog.Unit(id).Recipe
            .Where(pair => catalog.Unit(pair.Key).Tier != "자원")
            .Any(pair => owned.GetValueOrDefault(pair.Key) < pair.Value) ||
            BulletGuideCraftSafety.Allows(catalog, id, owned, round, confirmedNavigation,
                gorosei == GoroseiMode.Warcury ? 120 : 100, queenConfirmed,
                legends == 0 && !bullet ? new(BulletGuideStage.FirstLegend, id, false) : null));
        // Source 47 allocates Marco's three shared pieces to distinct support roots.
        // Only roots backed by an observed shared piece (or already owned) are reserved.
        var supportPackage = new[] { (Shared: "220h", Root: "B30h"), (Shared: "M20h", Root: "MC0h"),
                (Shared: "M10h", Root: "O30h") }
            .Where(item => Has(item.Root) || (!support.IsReady && Has(item.Shared)))
            .Select(item => "rawcode:" + item.Root).ToArray();
        var protectedIds = new[] { GoalId }.Concat(!bullet && round < 50
                ? ComponentCodes.Where(Has).Select(code => "rawcode:" + code) : [])
            .Where(id => owned.GetValueOrDefault(id) > 0).Concat(supportPackage)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Dictionary<string, int> Available(string id) => BulletGuideReservations.Available(catalog, owned, protectedIds, id);
        support = new BulletGuideSupportPolicy(catalog).Evaluate(inventory, confirmedNavigation, gorosei, CandidateAllowed,
            learnedWeight: id => learning?.WeightFor(id, difficulty) ?? 0);
        if (round is > 0 and < 8 && fastUnique is FastUniqueState.Unknown or FastUniqueState.TerminalOutcomeUnknown &&
            !owned.Keys.Any(id => TopGradePolicy.BaseTier(catalog.Unit(id).Tier) == "희귀함"))
        {
            var rare = catalog.AllUnits.Where(unit => TopGradePolicy.BaseTier(unit.Tier) == "희귀함" && unit.Recipe.Count > 0)
                .DistinctBy(unit => unit.Id).Where(unit => CandidateAllowed(unit.Id))
                .Select(unit => (unit.Id, Progress: new RecipeCompletionCalculator(catalog.Unit).Calculate([unit.Id], Available(unit.Id))))
                .Select(item => (item.Id, item.Progress,
                    Missing: item.Progress.MissingLeaves.Sum(leaf => leaf.MissingCount),
                    Selectable: item.Progress.MissingLeaves.Where(leaf => catalog.Unit(leaf.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains))
                        .Sum(leaf => leaf.MissingCount)))
                .OrderBy(item => item.Missing == 0 ? 0 : 1)
                .ThenBy(item => item.Missing - Math.Min(Math.Max(0, selectionWisps), item.Selectable))
                .ThenBy(item => item.Missing)
                .ThenByDescending(item => BulletGuideReservations.IsRecipeStep(catalog, GoalId, item.Id))
                .ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
            return Make(BulletGuideStage.FastUniqueRare, rare.Id) with
            {
                SelectionWisps = Math.Max(0, selectionWisps),
                RareCommonDeficit = rare.Progress?.MissingLeaves.Where(leaf => catalog.Unit(leaf.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains))
                    .Sum(leaf => leaf.MissingCount) ?? 0,
                RareOtherDeficit = rare.Progress?.MissingLeaves.Where(leaf => !catalog.Unit(leaf.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains))
                    .Sum(leaf => leaf.MissingCount) ?? 0
            };
        }
        if (queenConfirmed && Has("HA0h") && !Has("IC0h") && CandidateAllowed("rawcode:IC0h"))
            return Make(BulletGuideStage.QueenConversion, "rawcode:IC0h");
        if (!bullet)
        {
            if (legends == 0) return Make(BulletGuideStage.FirstLegend, PickOpening());
            // Prepare every component while there is still a spending window. Future
            // post-assembly aura targets must not indefinitely starve the third legend.
            // The fourth-legend reward gate and actual craft/line-survival checks remain.
            if (components < 3 && (story >= 12 || round >= 30) && !(legends >= 3 && story < 10))
                return Make(BulletGuideStage.BulletMaterials, Pick(ComponentCodes));
            if (legends == 1) return Make(BulletGuideStage.SecondLegend,
                Pick(["MC0h", "M30h", "U20h", "HA0h", "530h", "B30h", "630h"]));
            // Source 42-46: the third-air branch assumes an early slow/boss hand.
            // This is observed-role recovery, not an assertion about acquisition order.
            if (legends == 2 && story < 10 &&
                !owned.Keys.Concat(observedLegendIds ?? []).Concat(historicalCounts.Keys).Any(id => EarlyRoleCodes.Contains(id)))
                return Make(BulletGuideStage.OpeningRoleRecovery,
                    Pick(["MC0h", "M30h", "V20h", "HA0h", "530h", "630h", "Z20h"]));
            if (story < 12 && air < 2 && !Has("S80h") &&
                !new[] { "R50h", "H50h", "O50h", "Q50h" }.Any(Has) &&
                catalog.Unit("rawcode:S80h").Recipe
                    .Where(pair => catalog.Unit(pair.Key).Tier != "자원")
                    .All(pair => owned.GetValueOrDefault(pair.Key) >= pair.Value))
                return Make(BulletGuideStage.BruleePreparation, "rawcode:S80h");
            if (legends >= 3 && story < 10) return Make(BulletGuideStage.FourthLegendReward, null);
            if (air < 2) return Make(BulletGuideStage.AirFoundation,
                Pick(Has("930h") ? ["U30h", "HA0h"] : air > 0 || Has("830h")
                    ? ["930h"] : ["HA0h", "U30h", "930h", "K30h"]));
            if (destructionKingAvailable == true && round is > 0 and < 30 && story == 10 && legends == 3)
            {
                var readyFourth = OpeningGroups.SelectMany(group => group).Select(code => catalog.Unit("rawcode:" + code))
                    .FirstOrDefault(unit => owned.GetValueOrDefault(unit.Id) == 0 && unit.Recipe.Count > 0 &&
                        CandidateAllowed(unit.Id) && unit.Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원")
                            .All(pair => Available(unit.Id).GetValueOrDefault(pair.Key) >= pair.Value) &&
                        BulletGuideCraftSafety.Allows(catalog, unit.Id, owned, round, confirmedNavigation,
                            support.ArmorTarget));
                if (readyFourth is not null) return Make(BulletGuideStage.DestructionRace, readyFourth.Id);
            }
            if (Has("HA0h") && Has("U30h") && !Has("930h"))
                return Make(BulletGuideStage.BulletMaterials, Pick(["930h", "V20h", "U20h"]));
            if (components < 2 && story < 12)
                return Make(BulletGuideStage.BulletMaterials, Pick(ComponentCodes));
        }
        if (!bullet && !support.StunPairReady)
        {
            // Do not postpone an already prepared surviving source stun formation
            // merely because nominal armor/slow targets are not finished yet.
            var readyStun = new[] { "O30h", "Y30h", "Z20h", "W20h", "IC0h" }
                .Where(code => !Has(code)).Select(code => "rawcode:" + code)
                .Where(CandidateAllowed).FirstOrDefault(id =>
                {
                    var target = catalog.Unit(id);
                    if (target.Recipe.Count == 0 || target.Recipe.Any(pair => catalog.Unit(pair.Key).Tier != "자원" &&
                            Available(id).GetValueOrDefault(pair.Key) < pair.Value)) return false;
                    var after = new RecipeCompletionCalculator(catalog.Unit).CalculateAllocation([id], owned)
                        .RemainingInventory.ToDictionary(pair => pair.Key, pair => checked((int)pair.Value));
                    after[id] = after.GetValueOrDefault(id) + 1;
                    return new BulletGuideSupportPolicy(catalog).Evaluate(after, confirmedNavigation, gorosei).StunPairReady;
                });
            if (readyStun is not null) return Make(BulletGuideStage.EarlyStunSupport, readyStun);
        }
        if (boss < 2 && !bossException) return Make(BulletGuideStage.BossSupport,
            !Has("U30h") ? "rawcode:U30h" : Pick(["540h", "MC0h"]));
        if (!support.IsReady) return Make(BulletGuideStage.ControlSupport, support.RecommendedUnitId);
        if (bullet && gorosei == GoroseiMode.Saturn && !Has("K20h"))
            return Make(BulletGuideStage.ControlSupport, "rawcode:K20h");
        if (bullet && round >= 50 && !Has("K20h") && owned.Where(pair =>
                catalog.Unit(pair.Key).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains))
            .Sum(pair => pair.Value) >= 30)
            return Make(BulletGuideStage.ControlSupport, "rawcode:K20h");
        if (!bullet && components < 3)
            return Make(BulletGuideStage.BulletMaterials, Pick(ComponentCodes));
        if (!bullet && round < 50) return Make(BulletGuideStage.HoldRound50, null);
        if (!bullet) return Make(BulletGuideStage.CraftBullet, GoalId);
        return Make(BulletGuideStage.Operating, null);

        BulletGuidePlan Make(BulletGuideStage stage, string? target) => new(stage, target, bullet)
        {
            FastUnique = fastUnique == FastUniqueState.CompletedVerified ? fastUnique : round >= 8 ? FastUniqueState.Expired
                : owned.Keys.Any(id => TopGradePolicy.BaseTier(catalog.Unit(id).Tier) == "희귀함")
                    ? FastUniqueState.CurrentRareOwned : fastUnique,
            KnownLegendLowerBound = legends, QueenInput = queenInput, ProtectedUnitIds = protectedIds, AirCount = air, BossCount = boss, BossException = bossException,
            ActiveHighGambleQuest = routeQuests?.Status("Q006") == RouteQuestStatus.Active,
            HighGamble = routeQuests?.HighGamble ?? HighGambleObservation.Unknown,
            PursueDestructionKing = destructionKingAvailable == true && round is > 0 and < 30 && story < 11,
            ComponentCount = components, Difficulty = difficulty, Support = support,
            Round = round, ConfirmedNavigation = confirmedNavigation
        };

        string? Pick(IEnumerable<string> codes) => codes.Select(code => "rawcode:" + code)
            .Where(id => owned.GetValueOrDefault(id) <= 0 && CandidateAllowed(id))
            .Select((id, rank) => (Id: id, Rank: rank,
                Progress: new RecipeCompletionCalculator(catalog.Unit).Calculate([id], Available(id))))
            .OrderByDescending(item => item.Progress.CompletionRatio)
            .ThenByDescending(item => learning?.WeightFor(item.Id, difficulty) ?? 0)
            .ThenBy(item => item.Rank).Select(item => item.Id).FirstOrDefault();

        string? PickOpening()
        {
            // A lower source group may be used when already craftable. Otherwise preserve source order.
            var recipes = new RecipeCompletionCalculator(catalog.Unit);
            foreach (var group in OpeningGroups)
            {
                var ready = group.Where(code => recipes.Calculate(["rawcode:" + code], Available("rawcode:" + code)).CompletionRatio >= 1).ToArray();
                if (ready.Length > 0 && Pick(ready) is { } allowed) return allowed;
            }
            return Pick(OpeningGroups[0]);
        }
    }
}
