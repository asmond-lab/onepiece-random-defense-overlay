namespace OrandOverlay;

public static class BulletGuideCraftSafety
{
    // Opening exempts the first legend from support composition retention, not execution checks.
    // Only its active recipe qualifies; stale phase labels are not evidence.
    private static bool FirstLegendRecipe(DataCatalog catalog, string targetId,
        IReadOnlyDictionary<string, int> inventory, BulletGuidePlan? plan) =>
        plan is { Stage: BulletGuideStage.FirstLegend, OwnedBullet: false, KnownLegendLowerBound: 0,
            TargetUnitId: { } root } &&
        TopGradePolicy.BaseTier(catalog.Unit(root).Tier) is ("전설" or "히든" or "해적선") &&
        BulletGuidePolicy.OpeningGroups.SelectMany(group => group).Any(code => root == "rawcode:" + code) &&
        !inventory.Any(pair => pair.Value > 0 &&
            (pair.Key == BulletGuidePolicy.GoalId || pair.Key != "rawcode:S80h" &&
                TopGradePolicy.BaseTier(catalog.Unit(pair.Key).Tier) is "전설" or "히든" or "해적선")) &&
        BulletGuideReservations.IsRecipeStep(catalog, root, targetId);

    // Completion allocation may satisfy a root with an owned result. A new craft
    // instead spends only its direct recipe, retaining any existing result body.
    internal static Dictionary<string, int>? ProjectAfterCraft(DataCatalog catalog, string targetId,
        IReadOnlyDictionary<string, int> inventory)
    {
        var ingredients = catalog.Unit(targetId).Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원").ToArray();
        if (ingredients.Any(pair => inventory.GetValueOrDefault(pair.Key) < pair.Value)) return null;
        var after = inventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var ingredient in ingredients) after[ingredient.Key] -= ingredient.Value;
        after[targetId] = checked(after.GetValueOrDefault(targetId) + 1);
        return after;
    }

    public static bool Allows(DataCatalog catalog, string targetId, IReadOnlyDictionary<string, int> inventory,
        int round, string? confirmedNavigation, int armorTarget = 100, bool queenConversionConfirmed = false,
        BulletGuidePlan? plan = null)
        => BlockReason(catalog, targetId, inventory, round, confirmedNavigation, armorTarget, queenConversionConfirmed, plan) is null;

    public static string? BlockReason(DataCatalog catalog, string targetId, IReadOnlyDictionary<string, int> inventory,
        int round, string? confirmedNavigation, int armorTarget = 100, bool queenConversionConfirmed = false,
        BulletGuidePlan? plan = null)
    {
        if (round <= 0) return "현재 라운드 미확인 · 재료를 사용하는 조합은 보류하세요.";
        if (targetId == BulletGuidePolicy.GoalId && round < 50) return "불릿은 50라에 조합합니다. 재료 전설은 그때까지 유지하세요.";
        var target = catalog.Unit(targetId);
        var queenException = targetId == "rawcode:IC0h" && queenConversionConfirmed;
        var after = ProjectAfterCraft(catalog, targetId, inventory);
        if (after is null) return "이 단계에 필요한 재료 유닛을 먼저 조합해야 합니다.";
        var firstLegendRecipe = FirstLegendRecipe(catalog, targetId, inventory, plan);
        int Commons(IReadOnlyDictionary<string, int> units) => units.Where(pair =>
            catalog.Unit(pair.Key).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains)).Sum(pair => pair.Value);
        if (round >= 50 && Commons(after) < Math.Min(20, Commons(inventory)))
            return "불릿 강화에 남겨둘 흔함이 줄어듭니다. 여분의 흔함을 먼저 확보하세요.";
        int CapabilityCount(IReadOnlyDictionary<string, int> units, string[] codes) => units.Where(pair => pair.Value > 0 &&
            catalog.Unit(pair.Key).Rawcodes.Any(codes.Contains)).Sum(pair => pair.Value);
        if (CapabilityCount(after, BulletGuidePolicy.BossCodes) < Math.Min(2, CapabilityCount(inventory, BulletGuidePolicy.BossCodes)))
            return "보스 담당 유닛이 줄어듭니다. 대신 보스를 잡을 유닛을 먼저 확보하세요.";
        bool BossException(IReadOnlyDictionary<string, int> units) => CapabilityCount(units, ["U30h"]) > 0 &&
            CapabilityCount(units, ["F30h", "H50h"]) > 0;
        if (BossException(inventory) && !BossException(after) && CapabilityCount(after, BulletGuidePolicy.BossCodes) < 2)
            return "레드포스와 함께 보스를 맡을 유닛이 사라집니다. 보스 담당을 먼저 보완하세요.";
        if (!queenException && round < 50 && CapabilityCount(after, BulletGuidePolicy.AirCodes) < Math.Min(2, CapabilityCount(inventory, BulletGuidePolicy.AirCodes)))
            return "스토리를 맡을 공중 유닛이 줄어듭니다. 다른 공중 유닛을 먼저 확보하세요.";
        var support = new BulletGuideSupportPolicy(catalog);
        // The source explicitly trades one King for Queen after the confirmed condition.
        // Remove only that King from the comparison; no other support may be sacrificed.
        var baseline = inventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (queenException && baseline.GetValueOrDefault("rawcode:HA0h") > 0) baseline["rawcode:HA0h"]--;
        var beforeSupport = support.Evaluate(baseline, confirmedNavigation, GoroseiMode.None, includeAuxiliaryAuras: false);
        var afterSupport = support.Evaluate(after, confirmedNavigation, GoroseiMode.None, includeAuxiliaryAuras: false);
        // Source-retained rare stun is a roster role, not a claim about uptime or seconds.
        // A spare rare or an already verified source stun formation permits promotion.
        string[] rareStunCodes = ["C20h", "V10h", "E20h"];
        var requiredBulletComponent = target.Rawcodes.Any(BulletGuidePolicy.ComponentCodes.Contains);
        // Source operation lines 49-54 explicitly permit early Brulee preparation.
        // The catalog's actual Brulee recipe spends rare Usopp (C20h).
        // This is only a recipe-rare exception, not a replacement stun or an aura bypass.
        // Preparation eligibility (story, legends, mobility, mission units) stays in GuidePolicy.
        var earlyBruleeRecipe = target.Id == "rawcode:S80h" && round < 50 &&
            inventory.GetValueOrDefault(BulletGuidePolicy.GoalId) == 0 &&
            inventory.GetValueOrDefault(target.Id) == 0 &&
            target.Recipe.ContainsKey("rawcode:C20h");
        if (!firstLegendRecipe && !requiredBulletComponent && !earlyBruleeRecipe && CapabilityCount(inventory, rareStunCodes) > 0 && CapabilityCount(after, rareStunCodes) == 0 &&
            !afterSupport.StunPairReady) return "남겨둘 희귀 스턴 유닛이 사라집니다. 다른 스턴 유닛을 먼저 확보하세요.";
        // Auxiliary rare/special armor, slow, attack and speed do not veto progression,
        // including later legends and their intermediates. Primary control roles still do.
        // Only the two registered source targets are accepted; callers cannot lower the safety floor.
        var requiredArmor = armorTarget == 120 ? 120 : 100;
        return firstLegendRecipe || afterSupport.ArmorPotential >= Math.Min(requiredArmor, beforeSupport.ArmorPotential) &&
            afterSupport.SlowPotential >= Math.Min(82, beforeSupport.SlowPotential) &&
            (!beforeSupport.StunPairReady || afterSupport.StunPairReady)
            ? null : "조합하면 현재 필요한 방깎·이감·스턴이 줄어듭니다. 소모할 지원 유닛을 대신할 유닛을 먼저 확보하세요.";
    }
}
