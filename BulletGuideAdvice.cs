namespace OrandOverlay;

/// <summary>Independent verified native population, not app-common inventory or a planned count.</summary>
public sealed record BulletTrait2322Eligibility(BulletUpgradeObservationContext Context,
    int EligibleOwnedLiveUndeadCount, bool SelectorsObserved, bool VerifiedNativePopulation)
{
    public bool CanChoose(BulletUpgradeAbilityObservation? abilities, BulletUpgradeAbility choice,
        BulletUpgradeObservationContext current) =>
        current.MapVersion == BulletMapVersion.V2322 && Context.MapVersion == current.MapVersion &&
        string.Equals(Context.MapScriptSha256, BulletMechanics.Script2322, StringComparison.OrdinalIgnoreCase) &&
        Context.Owner == current.Owner && Context.Owner <= 3 && Context.EngineHandle != 0 &&
        Context.EngineHandle == current.EngineHandle && !string.IsNullOrWhiteSpace(Context.SessionId) &&
        Context.SessionId == current.SessionId && Context.MaximumAge > TimeSpan.Zero &&
        current.MaximumAge > TimeSpan.Zero && current.NowUtc >= Context.NowUtc &&
        current.NowUtc - Context.NowUtc <= current.MaximumAge && VerifiedNativePopulation &&
        SelectorsObserved && EligibleOwnedLiveUndeadCount >= 5 &&
        abilities?.IsValid(current) == true && abilities.Level(choice) == 3;
}

/// <summary>Source advice accompanies observed decisions; it is not an observation itself.</summary>
public static class BulletGuideAdvice
{
    public static string UpgradeEffects(BulletGuideRuntimeState state)
    {
        if (!state.IsCurrent) return state.Detail;
        var baseline = $"강화 등급 기준 기본값 · 방깎 {state.BaselineArmorReduction?.ToString() ?? "미확인"} / 이감 {state.BulletSlow?.ToString() ?? "미확인"}";
        if (state.MapVersion is not (BulletMapVersion.V2320 or BulletMapVersion.V2322))
            return baseline + " · 특성 미확인 · 실제 대상 적용은 별도";
        var traits = new[] { (BulletUpgradeAbility.Attack, "공격"), (BulletUpgradeAbility.Speed, "공속"), (BulletUpgradeAbility.Armor, "방깎") }
            .Select(item => item.Item2 + ": " + (state.Trait(item.Item1) switch
            {
                BulletTraitState.ConfirmedSelected => "선택한 특성 확인 · 게임에 표시된 값 " +
                    (item.Item1 == BulletUpgradeAbility.Armor ? "-50" : "50%") +
                    (state.MapVersion == BulletMapVersion.V2320 ? " (게임 설명에는 48로 표시됨)" : " (선택에 필요한 유닛과 실제 대상 효과는 아직 확인되지 않음)"),
                BulletTraitState.Baseline => "기본 능력 확인 · 특성 미선택",
                _ => "특성을 확인하지 못해 40/50을 실제 효과로 표시하지 않습니다"
            }));
        return baseline + " (계획 기준, 실제 특성값 아님) · " + string.Join(" · ", traits) +
            (state.MapVersion == BulletMapVersion.V2322
                ? " · 2.322 특성 획득에는 특성 포인트 4가 필요합니다. 선택에는 해당 능력 정확히 3레벨과 조건에 맞는 내 살아 있는 유닛 5기(무작위 소모)가 필요합니다. 현재 보유한 유닛 중 어느 5기가 해당하는지 확인할 수 없어 선택 추천을 보류합니다. 선택 후 실제 효과를 확인하세요. 게임 설명의 2/1/2초만으로 범위 기절이 적용됐다고 판단하지 않습니다. 실제 피해량은 확인되지 않았습니다"
                : " · 실제 대상 효과는 확인이 필요합니다. 관련 능력의 최대 레벨은 3이며 실제 피해량을 측정한 값은 아닙니다");
    }

    public static string Stage(BulletGuidePlan plan) => (plan.Stage switch
    {
        BulletGuideStage.RoundUnknown => "현재 라운드 확인 전 · 재료 사용 보류",
        BulletGuideStage.FastUniqueRare => "패스트유니크 · 8라 시작 전 희귀 조합 우선 · 완료 미확인 (계획) · 보상 선택위습 1개는 받은 뒤 반영",
        BulletGuideStage.FirstLegend => plan.Round >= 12
            ? "첫 전설 · 12라 목표 경과 · 재료 보존 후 가능한 첫 전설 확보 (패배 판정 아님)"
            : "첫 전설 · 12라 전 목표",
        BulletGuideStage.EarlyStunSupport => "불릿 전 지원 · 준비된 스턴 조합부터 보완 (깎·이감 목표는 유지)",
        BulletGuideStage.QueenConversion => "킹 → 퀸 전환 · 사용자 조건 확인 · 조합 후 지원 재점검",
        BulletGuideStage.OpeningRoleRecovery => "초반 역할 보완 · 깎패 추가보다 이감·스턴·보잡",
        BulletGuideStage.SecondLegend => "두 번째 전설 · 이감/보잡 기반",
        BulletGuideStage.BruleePreparation => "브륄레 준비 · 변환은 펑크해저드 이후 별도 확인",
        BulletGuideStage.DestructionRace => "파괴왕 · 준비된 네 번째 전설 조합",
        BulletGuideStage.AirFoundation => $"공중·블링크 기반 {plan.AirCount}/2 · 스토리 12 전",
        BulletGuideStage.FourthLegendReward => "네 번째 전설 · 펑크해저드 보상 확인",
        BulletGuideStage.BulletMaterials => $"불릿 재료 전설 {plan.ComponentCount}/3 · 50라 시작 전 3기 준비 · 조합은 50라부터",
        BulletGuideStage.BossSupport => $"보잡 {plan.BossCount}/2 · 레드포스 우선",
        BulletGuideStage.ControlSupport => (plan.BossException ? "레포+모건/카르가라 예외" : "2보잡") +
            $" → 오라깎 {plan.Support?.ArmorTarget ?? 100} / 이감 82 → 스턴 1~1.5",
        BulletGuideStage.HoldRound50 => "재료 전설 유지 · 불릿 조합은 50라",
        BulletGuideStage.CraftBullet => "50라 불릿 조합 · 실제 보유 확인",
        _ => "불릿 보유 · 지원 보완과 강화"
    }) + (plan.PursueDestructionKing ? "\n파괴왕 · 30라 시작 전 드레스로자 11단계 파괴" : "");

    public static string Operation(CoachFrame frame)
    {
        if (frame.Round <= 0) return "현재 라운드를 확인할 때까지 조합·판매·위습 사용을 보류하세요.";
        var plan = frame.GuidePlan!;
        var lines = new List<string>
        {
            "이 안내는 솔로 악몽 공략을 바탕으로 하며, 1상위 조합 안내와는 다릅니다.",
            $"현재 {frame.DifficultyLabel} · 공중/블링크 {plan.AirCount}/2 · 보잡 {plan.BossCount}/2" +
                (plan.BossException ? " · 레포+모건/카르가라 원문 예외" : ""),
            "추천 항법: 바운티헌터 · 게임에서 선택했는지는 별도로 확인해야 합니다. " +
                (frame.ConfirmedNavigation is null ? "실제 선택 미확인 · 선택 구간에 게임에서 바운티헌터를 선택하고 실제 항법을 확인하세요. 계획 진행은 유지하되 미확인 제한을 추정하지 않습니다."
                    : frame.ConfirmedNavigation == plan.PlannedNavigation ? "실제 바운티헌터 확인."
                    : $"게임에서 선택한 {NavigationProfiles.Find(frame.ConfirmedNavigation).Name}은 공략 권장 항법과 다릅니다. 선택한 항법의 상위 수·자원 제한을 따릅니다.")
        };
        lines.Add("패스트유니크 · " + (plan.FastUnique switch
        {
            FastUniqueState.CurrentRareOwned => "현재 희귀 보유 · 조합 완료 미확인",
            FastUniqueState.Expired => "기한 만료 · 성공 여부 미확인",
            FastUniqueState.CompletedVerified => "이전에 조합 성공을 확인했습니다 · 지금 보유하지 않아도 완료 상태를 유지합니다",
            FastUniqueState.RarePreviouslyObserved => "기한 전에 희귀 보유를 확인함 · 조합으로 얻었는지 보상으로 받았는지는 미확인 · 첫 희귀를 다시 만들도록 지시하지 않음",
            FastUniqueState.TerminalOutcomeUnknown => "미션이 끝났지만 성공했는지 기한이 지났는지는 확인되지 않았습니다",
            _ => "완료 미확인 · 게임에서 성공 여부를 읽을 수 없음"
        }) + " · 현재 희귀 보유는 보상/획득일 수 있으며 미션 성공 증거가 아닙니다.");
        if (plan.Stage == BulletGuideStage.FastUniqueRare)
            lines.Add($"목표 흔함 총 부족 {plan.RareCommonDeficit}개 · 선택 불가 재료 부족 {plan.RareOtherDeficit}개 · 현재 선택위습 {plan.SelectionWisps}개 (미지급 보상 제외). " +
                "선택위습은 기본 보존합니다. 현재 보유량으로 안전한 목표 조합을 완성할 수 있을 때만 필요한 전체 선택 목록을 안내합니다. 기한 내 완성 보장은 아닙니다.");
        if (plan.ProtectedUnitIds.Any(id => id is "rawcode:B30h" or "rawcode:MC0h" or "rawcode:O30h"))
            lines.Add("흰수염·히바리·봉쿠레 지원 조합에 쓸 재료를 남겨 둡니다. 마르코 보조딜보다 지원을 우선합니다. 재료를 남겨 둔 것일 뿐, 조합 완성이나 효과 적용을 확인한 것은 아닙니다.");
        if (frame.Inventory.GetValueOrDefault("rawcode:HA0h") > 0)
            lines.Add(plan.QueenInput switch
            {
                QueenConversionInput.UserConfirmedMissionsComplete => "퀸 전환: 킹으로 페문 완료를 사용자 확인했습니다. 게임에서 완료를 읽은 것은 아닙니다.",
                QueenConversionInput.UserConfirmedStoryTooSlow => "퀸 조기 전환 예외: 스토리가 너무 느리다는 사용자 확인입니다. 페문 완료로 간주하지 않습니다.",
                QueenConversionInput.KeepKing => "킹 유지: 사용자 선택. 퀸 전환 조건을 자동으로 추정하지 않습니다.",
                _ => "퀸 전환 조건 미확인: 킹으로 페문 완료 또는 스토리가 너무 느린 예외를 직접 확인하세요. 라운드·스토리 단계만으로 판단하지 않습니다."
            });
        if (plan.ActiveHighGambleQuest)
            lines.Add(HighGamblePresentation.Describe(plan.HighGamble));
        lines.Add(frame.PlanningGorosei.Describe());
        lines.Add(frame.Round is > 0 and < 50
            ? "현재 전투 효과: 50라 전에는 아직 적용되지 않습니다. 목표 수치를 현재 수치에 더하지 않습니다."
            : "현재 전투 효과: " + (frame.CurrentGoroseiEffect == GoroseiMode.None ? "아직 확인하지 못했습니다" :
                GoroseiEffects.Options.First(option => option.Mode == frame.CurrentGoroseiEffect).Name) +
                " · " + frame.Gorosei.EffectEvidence);
        if (plan.PursueDestructionKing)
            lines.Add("파괴왕 미션 활성 확인 · 30라 시작 전 드레스로자(스토리 11)를 파괴하세요. 모든 플레이어가 공유하며 목재 2·아이템 발굴 1회는 완료 후 실제 수령을 확인합니다.");
        if (!plan.OwnedBullet)
        {
            var missingComponents = new[] { (Id: "rawcode:U20h", Name: "검은수염"),
                    (Id: "rawcode:930h", Name: "시키"), (Id: "rawcode:V20h", Name: "스모커") }
                .Where(item => frame.Inventory.GetValueOrDefault(item.Id) <= 0).Select(item => item.Name).ToArray();
            lines.Add(missingComponents.Length == 0 ? "불릿 재료 3기 보유 확인 · 50라 전에는 합치지 마세요." :
                "50라 시작 전 준비할 재료 전설: " + string.Join(" · ", missingComponents) +
                $" · 현재 {frame.Round}라. 선택위습 {frame.RewardWisps.GetValueOrDefault("e018")}개는 실제 보유량입니다. " +
                "현재 패와 자원으로 가능한 조합부터 안내하며, 없는 재료의 획득이나 기한 내 완성을 보장하지 않습니다.");
            lines.Add($"지금까지 확인한 전설 최소 {plan.KnownLegendLowerBound}기 · 종류별로 가장 많이 보유했던 수를 합쳤습니다. 획득 순서는 미확인입니다. 확인하지 못한 전설이나 이미 소모한 같은 유닛은 세지 않습니다.");
            lines.Add("첫 전설 이후에는 이감·스턴·보잡을 확보하고 깎패만 2개 이상 올리지 마세요.");
            if (BulletGuideMirrorPolicy.HasMissionException(frame.Inventory))
                lines.Add("미션 특수 유닛 보유 예외: 브륄레를 무리하게 소모하지 않고 시키·레드포스 경로를 먼저 봅니다. 미션 완료를 뜻하지는 않습니다.");
            lines.Add("공중 2기는 스토리 12 전에. 시키+레드포스 또는 시키+킹을 선호합니다.");
            lines.Add("펑크해저드 완료 후 네 번째 전설과 초월위습·브륄레 경로를 확인합니다.");
            lines.Add("검은수염·스모커·시키는 50라까지 전설로 사용합니다. 미완성 불릿의 스킬 수치를 현재 전력에 더하지 않습니다.");
        }
        else
        {
            lines.Add("불릿은 라인, 보잡 2기+남은 전설은 보스/광폭화. 불릿·스턴·보잡 그룹을 따로 관리하세요.");
            lines.Add("라운드몹 생성 위치에서 약 400 거리. 방깎이 묻은 체력 높은 몹을 공격하고 갓 나온 몹은 피합니다.");
            lines.Add("원문 강화: 방깎 → 공속 → 공격력. 방깎 15강에 이감 +20, 30강에 기본 방깎 +40. 선택 특성은 별도 관측이며 관측 전에는 적용하지 않습니다.");
            lines.Add("50라 방깎 30 / 공속 1~15, 60라 방깎·공속 풀강 목표.");
            lines.Add("공격력 강화량·독약·해루석 사용은 사용자가 직접 판단합니다. 자동 조건 판단에 포함하지 않습니다.");
        }
        lines.Add("희귀·특별함의 작은 방깎·이감·공증·공속은 주요 조합을 마친 뒤 남는 패로 보완합니다. 그 효과만을 위해 전설·불릿 재료 조합을 막거나 보조 유닛부터 만들지 않습니다. 필요한 이감·스턴·보잡 역할과 조합 재료는 별도로 확인합니다.");
        lines.Add("50라 흔함 최소 20개를 남기는 것이 원문 목표입니다. 중복 효과의 희귀+특별은 맵 중첩 규칙과 목표 재료부터 확인합니다.");
        lines.Add("공략의 일반 안내: 공방 여부 미확인 · 선택 난이도와 참여 형태는 다릅니다. 공방의 페문·스토리 도움 예외를 자동 적용하지 않습니다. 첫 전설이 불릿패/이감/보잡(샹크스·히바리, 킬러 제외)이면 두 번째까지 깎패 하나를 허용하며, 첫 두 전설이 이감/보잡이면 세 번째 공중을 권합니다. 획득 순서와 시저 이동은 미확인입니다.");
        lines.Add("공략의 지원 조합 안내: 퀸+봉쿠레/이완, 바톨/시키, 드래곤+이완/봉쿠레, 이완+봉쿠레. 스턴패가 안 나오면 퀸+흰수염 공폭·속공 컨트롤 대안이나 능력·대상·쿨다운 미확인으로 사용 지시는 보류합니다. 이완/봉쿠레/퀸의 흐름에는 후지전설·아오키지히든 대안, 새턴은 초파희귀+짤깎 또는 드래곤 포함 스턴. 누수·필요 조건 미확인으로 해당 대안을 현재 패의 확정 목표로 만들지 않습니다.");
        lines.Add("사용자가 직접 판단할 공략 참고사항: 시작깎높음은 초파희귀+방깎/공속30, 공격력1~15에서 멈추고 위습; 시작깎낮음은 공격력30 후 위습. 공격투자보다 해루+독약+출항, 보잡이 빠르면 공격15+독약+출항. 독약은 암보가 좀 쌓이고 사용한다는 원문입니다. 60라 빅맘 속도가 빡빡하면 강화 중단·도움소 위습으로 65라 해루석 준비, 빠르면 출항+독약 마나를 남기고 강화한다는 방향이며 현재 위험/예산 판단이 아닙니다.");
        lines.Add("공략 참고 수치: 기본 공격 피해 60만 이상일 때 추가 고정 피해, 59라를 제외하고 방깎 211 초과, 시작 방깎 160 이상과 레드포스가 있으면 공격 강화 1~15라는 안내입니다. 현재 피해량이나 적 방어력, 실제 적용 효과를 측정한 값이 아니므로 이 수치만으로 강화를 권하지 않습니다.");
        lines.Add("공략 작성자의 항법 평가: 일석이조 비선호, 최대출력 미시도는 작성자의 경험이며 선택 불가 판정이 아닙니다. 하급도박 클리어와 고도1회실패 퀘스트 예외 역시 현재 지출 가능 보장이 아닙니다.");
        lines.Add("공략의 추천 궁합: 크래커 공속/공격력, 센고쿠 이감/공증, 초파 공속, 사보 이감. 이 목록은 조합 궁합 설명입니다. 현재 보유와 능력 존재와 실제 대상 효과는 서로 다르며, 효과가 켜지는 조건·범위·지속 여부를 모르면 현재 수치에 더하지 않습니다.");
        var abilityObservation = BulletAbilityPresentation.Describe(frame);
        if (abilityObservation.Length > 0) lines.Add(abilityObservation);
        var blackMaria = BulletGuideBlackMariaPolicy.Summary(frame);
        if (blackMaria.Length > 0) lines.Add(blackMaria);
        var nasjuro = NasjuroWispAdvicePolicy.Evaluate(frame);
        if (nasjuro.Guidance.Length > 0) lines.Add(nasjuro.Guidance + "\n" + nasjuro.UnknownSignals);
        return string.Join("\n", lines);
    }

    public static CoachDecision Hold(CoachFrame frame, DataCatalog catalog)
    {
        // Deadline material selection precedes optional sales, conversions and upgrades.
        if (frame.GuidePlan is { Stage: BulletGuideStage.BulletMaterials } &&
            BulletGuideSelectionPolicy.Decide(frame, catalog) is { } componentSelection)
            return componentSelection;
        if (BulletGuideBlackMariaPolicy.Decide(frame) is { } blackMaria) return blackMaria;
        var roots = frame.SelectedGoalIds.Concat(frame.GuidePlan!.ProtectedUnitIds)
            .Concat(new[] { frame.GoalId, frame.GuidePlan?.TargetUnitId, frame.CommittedCraftUnitId }
                .OfType<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var remaining = new RecipeCompletionCalculator(catalog.Unit)
            .CalculateAllocation(roots, frame.Inventory).RemainingInventory;
        foreach (var (special, stronger) in new[]
                 { ("Y00h", "D20h"), ("A10h", "H20h"), ("F10h", "V20h") })
        {
            var specialId = "rawcode:" + special;
            if (remaining.GetValueOrDefault(specialId) <= 0 ||
                remaining.GetValueOrDefault("rawcode:" + stronger) <= 0 ||
                frame.GuidePlan!.ProtectedUnitIds.Contains(specialId))
                continue;
            var name = catalog.Unit(specialId).Name;
            return new(CoachActionKind.Economy, "guide1:sell-special:" + specialId,
                $"{name} 중복 여유분 1개 판매",
                $"{name} 1개 선택 → 게임의 ‘판매 :: 특별’ 사용",
                "상위 기물과 효과가 겹치며 현재 목표·진행 중 조합에 배정되지 않은 여유분입니다.",
                "기물 감소와 실제 보상을 확인한 뒤 다시 계산합니다. 목재는 확률 보상입니다.",
                Stage(frame.GuidePlan))
            {
                TargetUnitId = specialId, GoalLabel = "공략 1 · 더글라스 불릿",
                OperationGuide = Operation(frame)
            };
        }
        if (BulletGuideAncientShipPolicy.Decide(frame, catalog) is { } ancientShip) return ancientShip;
        if (BulletGuideUncommonSalePolicy.Decide(frame, catalog) is { } sale) return sale;
        if (BulletGuideMirrorPolicy.Decide(frame) is { } mirror) return mirror;
        if (NasjuroWispAdvicePolicy.Decide(frame, catalog) is { } nasjuro) return nasjuro;
        if (BulletGuideRayleighPolicy.Decide(frame) is { } rayleigh) return rayleigh;
        if (BulletGuideSelectionPolicy.Decide(frame, catalog) is { } selection) return selection;
        if (BulletGuideExchangePolicy.Decide(frame) is { } exchange) return exchange;
        if (BulletGuideUpgradePolicy.Decide(frame, catalog) is { } upgrade) return upgrade;
        if (BulletGuideTargetPolicy.Decide(frame) is { } lineTarget) return lineTarget;
        // Duplicate rares invite consideration; they do not block observed actions.
        if (BulletGuideBlackMariaPolicy.Consider(frame, catalog) is { } consideration) return consideration;
        return Gather(frame, catalog);
    }

    internal static CoachDecision Gather(CoachFrame frame, DataCatalog catalog)
    {
        var plan = frame.GuidePlan!;
        var lead = frame.Recommendations.FirstOrDefault();
        var target = plan.TargetUnitId is { } id ? catalog.Unit(id) : null;
        var waitingStory = plan.Stage == BulletGuideStage.FourthLegendReward;
        if (!waitingStory && target is not null && lead?.RecipeProgress.CompletionRatio >= 1)
        {
            foreach (var stepId in lead.RemainingCraftSteps.Where(step => step.MissingCount > 0)
                         .Select(step => step.UnitId).Append(target.Id).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (BulletGuideCraftSafety.ProjectAfterCraft(catalog, stepId, frame.Inventory) is null) continue;
                var reason = BulletGuideCraftSafety.BlockReason(catalog, stepId, frame.Inventory, frame.Round,
                    frame.ConfirmedNavigation, plan.Support?.ArmorTarget ?? 100, plan.QueenConversionConfirmed, plan);
                if (reason is null) continue;
                var step = catalog.Unit(stepId);
                return new(CoachActionKind.Waiting, "guide1:craft-blocked:" + stepId,
                    $"{RecommendationPresentation.CoachUnitName(target.Name, target.Tier)} 조합 전 지원 보완",
                    $"{RecommendationPresentation.CoachUnitName(step.Name, step.Tier)} 조합이 막혀 있습니다. 필요한 지원 유닛을 먼저 확보하세요.",
                    reason, "지원 유닛을 추가로 확보하면 조합을 다시 안내합니다.", Stage(plan))
                {
                    TargetUnitId = target.Id, GoalLabel = "공략 1 · 더글라스 불릿",
                    MaterialCompletion = lead.RecipeProgress.CompletionRatio,
                    OperationGuide = Operation(frame), PreservedMaterials = reason
                };
            }
        }
        var missing = lead?.RecipeProgress.MissingLeaves.Take(plan.Stage == BulletGuideStage.BulletMaterials ? int.MaxValue : 4)
            .Select(item => $"{RecommendationPresentation.CoachUnitName(catalog.Unit(item.UnitId).Name, catalog.Unit(item.UnitId).Tier)} {item.MissingCount}개").ToArray() ?? [];
        var kind = waitingStory ? CoachActionKind.Story : missing.Length > 0 ? CoachActionKind.Gather : CoachActionKind.Waiting;
        return new(kind, "guide1:" + plan.Stage + ":" + target?.Id,
            waitingStory ? "펑크해저드 완료와 보상을 확인하세요" : target is not null
                ? $"{RecommendationPresentation.CoachUnitName(target.Name, target.Tier)} {(missing.Length > 0 ? "재료 확보" : "조합 보류")}" : Stage(plan),
            waitingStory ? "스토리 담당 전설로 진행하고 라인·스턴 유닛은 남기세요. 받지 않은 초월위습은 사용하지 않습니다."
                : missing.Length > 0 ? "다음 재료: " + string.Join(" · ", missing)
                    : "현재 패를 보존하세요. 실행 가능한 조합·강화 상태가 확인되면 다음 행동을 표시합니다.",
            "사용자 공략의 현재 단계와 실제 보유 패를 함께 반영했습니다.",
            "새 정상 패·스토리·자원 관측으로 다시 계산합니다.", Stage(plan))
        {
            TargetUnitId = target?.Id, GoalLabel = "공략 1 · 더글라스 불릿",
            MaterialCompletion = lead?.RecipeProgress.CompletionRatio,
            OperationGuide = Operation(frame), UnknownSignals = "자원·강화·아이템·타깃은 해당 관측이 필요합니다.",
            PreservedMaterials = "불릿 재료 전설과 현재 이감·스턴·보잡을 보존하세요."
        };
    }

    public static string SupportSummary(CoachFrame frame)
    {
        if (!frame.IsCurrent || !frame.HasKnownDifficulty) return "";
        if (frame.GuidePlan?.Support is not { } support) return BulletGuideBlackMariaPolicy.Summary(frame);
        var summary = $"조합 후 보유 조합 기준 · 방깎 {support.ArmorPotential}/{support.ArmorTarget} · 이감 {support.SlowPotential}/82\n" +
            $"원문 스턴 조합: {(support.StunPairReady ? "보유 확인" : "추가 확보")} · 실제 효과가 닿는 범위나 유지 시간과는 다릅니다.";
        if (frame.GuidePlan.OwnedBullet)
        {
            summary += frame.GuideRuntime.IsCurrent
                ? $"\n불릿 강화 등급 · 방깎 {Tier(frame.GuideRuntime.ArmorTier)} / 공속 {Tier(frame.GuideRuntime.SpeedTier)} / 공격 {Tier(frame.GuideRuntime.AttackTier)}\n" +
                  UpgradeEffects(frame.GuideRuntime)
                : "\n" + frame.GuideRuntime.Detail;
            if (frame.GuideRuntime is { IsCurrent: true, ExactCounts: { } counts })
                summary += $"\n확인된 강화 횟수 · 방깎 {counts.Armor}/30 · 공속 {counts.Speed}/30 · 공격 {counts.Attack}/30";
            var upgradeMilestone = BulletGuideUpgradePolicy.MilestoneSummary(frame);
            if (upgradeMilestone.Length > 0) summary += "\n" + upgradeMilestone;
            if (BulletGuidePlacementPolicy.Summary(frame) is { } placement)
                summary += "\n" + placement;
        }
        var blackMaria = BulletGuideBlackMariaPolicy.Summary(frame);
        if (blackMaria.Length > 0) summary += "\n" + blackMaria;
        return summary;
        static string Tier(int? tier) => tier switch { 1 => "기본", 2 => "유니크", 3 => "레전더리", _ => "미확인" };
    }
}
