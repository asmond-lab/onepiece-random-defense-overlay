using System.Collections.Immutable;

namespace OrandOverlay;

public sealed class BeginnerCoachPlanner(DataCatalog catalog)
{
    public CoachDecision Decide(CoachFrame frame, RecipeConditionContext? conditionContext = null)
    {
        frame = frame with { ConfirmedNavigation = frame.NativeNavigation.Resolve(frame.ConfirmedNavigation) };
        conditionContext = RecipeConditionEvaluator.BindToFrame(conditionContext,
            frame.MatchGeneration, frame.RecognitionRevision, frame.IsCurrent);
        var registeredGuide = frame.Mode == PlayMode.Guide && frame.GuideNumber == 1 && frame.GuidePlan is not null;
        if (registeredGuide) frame = frame with { GoalId = BulletGuidePolicy.GoalId,
            SuggestedNavigation = BulletGuidePolicy.NavigationId };
        if (registeredGuide) frame = frame with { ShipReservations = ShipReservationPolicy.Evaluate(frame, catalog) };
        var goal = frame.GoalId is { } goalId ? catalog.Unit(goalId) : null;
        var milestoneRound = frame.CompletedStoryStage < 13
            ? frame.Difficulty == "악몽" ? (int?)35 : null : frame.ClearRound;
        var milestone = !frame.HasKnownDifficulty ? "난이도 확인 후 다음 관문 안내" : frame.CompletedStoryStage < 13
            ? $"스토리 13단계까지 진행 · 현재 {frame.CompletedStoryStage}/13" +
              (frame.Difficulty == "악몽" ? $" · 35라까지 {Math.Max(0, 35 - frame.Round)}라 남음" : "")
            : frame.Round < frame.ClearRound
                ? $"{frame.ClearRound}라 클리어 구간 전 지원 수치와 주력 조합 확인"
                : "클리어 판정 확인 · 라인과 보스 대응 유지";
        var lead = frame.Recommendations.FirstOrDefault();
        var step = frame.CraftSteps.FirstOrDefault();
        // Reward types are observed map signals, not navigation emergency-summon candidates.
        var firstLegend = registeredGuide && frame.GuidePlan!.Stage == BulletGuideStage.FirstLegend;
        var waitingForReward = firstLegend
            ? frame.GuidePlan!.AwaitingRewardHand || !frame.GuidePlan.FirstLegendRewardHandObserved && frame.Round < 10
            : frame.Story?.Action is StorySequenceAction.WaitForStoryReward or StorySequenceAction.PushStoryForRareReward;
        var rewardWispId = firstLegend
            ? FirstLegendRewardGate.RewardIds.FirstOrDefault(id => id != "e018" && frame.RewardWisps.GetValueOrDefault(id) > 0)
            : frame.Story?.Action switch
        {
            // FirstRare describes the build surface, not absence of already received rewards.
            // Keep the common reward consumer; the quest branch still prefers a safe ready craft.
            StorySequenceAction.FindFirstRare => new[] { "e016", "e017", "e019" }
                .FirstOrDefault(id => frame.RewardWisps.GetValueOrDefault(id) > 0),
            StorySequenceAction.SpendStoryWisps => new[] { "e016", "e017" }
                .FirstOrDefault(id => frame.RewardWisps.GetValueOrDefault(id) > 0),
            StorySequenceAction.WaitForStoryReward or StorySequenceAction.PushStoryForRareReward => null,
            _ => frame.RewardWisps.GetValueOrDefault("e019") > 0 ? "e019" : null
        };
        var rewardBlocked = waitingForReward || rewardWispId is not null;
        var rewardWispName = rewardWispId switch
            { "e016" => "특별위습", "e017" => "안흔위습", "e018" => "흔함선택위습",
                "e0IX" => "랜덤위습", "e01A" => "초월위습", _ => "희귀위습" };
        if (frame.Outcome is "clear" or "fail")
            return Make(CoachActionKind.Finished, "outcome:" + frame.Outcome,
                frame.Outcome == "clear" ? "클리어 확인" : "이번 판 종료",
                "이번 판 기록에서 마지막 패와 추천 변경을 확인하세요.",
                $"{frame.Round}라 · 스토리 {frame.CompletedStoryStage}단계까지 확인했습니다.",
                "게임 종료를 확인했습니다. 실패 원인은 알 수 없습니다.") with
                { Milestone = "이번 판 기록을 확인하고 다음 판 준비", MilestoneRound = null, PreservedMaterials = "" };
        if (frame.Paused)
            return Make(CoachActionKind.Waiting, "paused", "자동 안내 일시정지",
                "안내 재개를 누르면 현재 패로 다시 계산합니다.",
                "패는 계속 확인하지만 새 행동은 안내하지 않습니다.", "재개 버튼을 누르세요.");
        if (frame.Round == 0 && frame.Inventory.Count == 0)
            return Make(CoachActionKind.Waiting, "start", frame.HasKnownDifficulty
                    ? $"{frame.DifficultyLabel} 게임을 시작하세요" : "게임에서 난이도를 선택하고 시작하세요",
                "워크래프트에서 맵을 시작하고 첫 패를 받으세요.",
                "첫 희귀함과 스토리 보상을 확인한 뒤 목표를 자동으로 정합니다.",
                "패가 인식되면 안내가 자동으로 시작됩니다.");
        if (!frame.IsCurrent)
            return Make(CoachActionKind.Recognition, "recognition", "패 인식을 다시 확인하고 있어요",
                "판매·분해·조합을 잠시 멈추고 게임 화면을 유지하세요.",
                "이전 패만 보고 조합이나 판매를 권하지 않습니다.",
                "현재 패가 다시 확인되면 같은 목표로 안내합니다.");
        if (frame.Inventory.Count == 0)
            return Make(CoachActionKind.Waiting, "empty-hand", "현재 패와 종료 상태를 확인하고 있어요",
                "새 조합은 잠시 보류하세요.", "보유 패가 없어 이전 조합 안내를 재사용하지 않습니다.",
                "새 패 또는 종료 결과가 확인되면 화면을 갱신합니다.");
        if (frame.Round <= 0)
            return Make(CoachActionKind.Recognition, "round-unknown", "현재 라운드를 확인하고 있어요",
                "조합·판매·분해·위습 사용을 잠시 보류하세요.",
                "패가 보여도 라운드가 확인되지 않으면 초반이나 50라 전으로 판단하지 않습니다.",
                "현재 판의 라운드가 확인되면 안내를 다시 계산합니다.") with
                { Milestone = "현재 라운드 확인", MilestoneRound = null, OperationGuide = "" };
        if (frame.NativeNavigation.Status == NativeNavigationStatus.Conflict)
            return Make(CoachActionKind.Waiting, "navigation-conflict", "항법을 정확히 읽지 못했습니다",
                "게임에서 선택한 항법을 확인해 주세요. 확인될 때까지 재료를 사용하지 마세요.",
                "게임에서 읽은 정보가 서로 달라 항법을 확정할 수 없습니다.",
                "항법을 다시 확인하면 안내를 갱신합니다.");
        if (frame.Difficulty == "악몽" &&
            frame.Signals.GetValueOrDefault("line-count") is { } lineCount &&
            lineCount >= (frame.Round <= 40 ? 70 : 50))
            return Make(CoachActionKind.Maintain, "line-survival", "라인 처리부터 유지하세요",
                "주력과 스턴 유닛의 라인 공격을 확인하고 재료 정리는 멈추세요.",
                $"확인된 라인 수 {lineCount}가 현재 제한에 도달했습니다.",
                "라인 수가 줄어드는지 다시 확인합니다.", true);

        if (registeredGuide && frame.GuidePlan!.PendingSelectionOutputs > 0 &&
            BulletGuideSelectionPolicy.Decide(frame, catalog) is { } pendingSelection)
            return pendingSelection;

        // T000 precedes unreceived story rewards. Final Craft still enforces recipe ownership and combat safety.
        if (registeredGuide && frame.HasKnownDifficulty && frame.Round is > 0 and < 8 &&
            frame.GuidePlan!.Stage == BulletGuideStage.FastUniqueRare)
        {
            if (frame.CommittedCraftUnitId is { } previous &&
                !BulletGuideReservations.IsRecipeStep(catalog, frame.GuidePlan.TargetUnitId, previous))
                frame = frame with { CommittedCraftUnitId = null };
            var questStep = frame.CraftSteps.FirstOrDefault(action =>
                BulletGuideReservations.IsRecipeStep(catalog, frame.GuidePlan.TargetUnitId, action.TargetUnitId));
            if (questStep is not null) return Craft(questStep, false);
            if (rewardWispId is not null) return ReceivedReward();
            return BulletGuideSelectionPolicy.Decide(frame, catalog) ?? BulletGuideAdvice.Gather(frame, catalog);
        }

        // The engine has already reserved materials and assessed the actual post-craft field.
        if (!rewardBlocked && goal is not null && step is not null &&
            lead?.CurrentCraft is { Priority: <= 1, LosesRequiredCombat: false })
            return Craft(step, true);
        if (!registeredGuide && frame.Round is >= 21 and <= 23 && frame.ConfirmedNavigation is null)
        {
            var option = frame.SuggestedNavigation;
            return Make(CoachActionKind.Navigation, "navigation:" + (option ?? "unknown"),
                option is null ? "항법 후보를 확인하세요" : $"{NavigationProfiles.Find(option).Name} 항법 선택",
                option is null ? "게임에서 고를 수 있는 항법을 확인해 주세요. 추천이 아직 준비되지 않았습니다."
                    : $"게임의 항법 창에서 {NavigationProfiles.Find(option).Name} 선택 → 아래에서 선택 완료 확인",
                "항법 선택 구간입니다. 추천 표시만으로 게임 안에서 선택된 것은 아닙니다.",
                "게임에서 선택한 뒤 확인 버튼을 누르세요.", true) with
            {
                NavigationOptionId = option, RequiresUserConfirmation = option is not null
            };
        }
        if (!registeredGuide && frame.Round >= 24 && frame.ConfirmedNavigation is null)
            return Make(CoachActionKind.Navigation, "navigation-confirm",
                "게임에 표시된 항법을 확인하세요",
                "아래 목록에서 실제 게임에 표시된 항법을 고른 뒤 확인하세요.",
                "항법 선택 시간이 지났지만 게임에서 선택된 항법은 아직 확인되지 않았습니다. 임의로 결정하지 않습니다.",
                "사용자 확인 후 해당 항법의 제한으로 조합을 다시 계산합니다.", true) with
            { RequiresUserConfirmation = true, RequiresNavigationChoice = true };
        if (frame.Mode == PlayMode.Guide && !registeredGuide)
            return Make(CoachActionKind.Waiting, "guide-unregistered", "대깨 공략 등록 대기",
                "사용할 공략을 전달하면 목표·진행 순서·예외 규칙을 반영합니다.",
                "아직 안내할 공략이 없어 임의로 조합을 권하지 않습니다.",
                "공략이 준비되면 안내를 시작합니다.") with
            { GoalLabel = "대깨 공략 미등록", Milestone = "사용자 공략 확인", MilestoneRound = null };
        if (frame.Difficulty == "악몽" && frame.Round >= 30 && frame.CompletedStoryStage < 13)
            return Story(true);
        if (rewardWispId is not null) return ReceivedReward();
        if (waitingForReward)
        {
            var advice = Story(false);
            return firstLegend && frame.GuidePlan!.AwaitingRewardHand ? advice with
            {
                Title = "사용한 위습의 결과를 기다리세요",
                Controls = "새로 나온 유닛이 확인될 때까지 조합을 잠시 멈추세요.",
                Reason = "위습은 줄었지만 해당 보상 유닛을 아직 확인하지 못했습니다.",
                Confirmation = "보상 유닛이 패에 반영되면 조합 안내를 다시 계산합니다."
            } : advice;
        }
        if (goal is null && frame.Round >= 20)
            return Make(CoachActionKind.Waiting, "goal-evidence",
                "자동 목표를 아직 정하지 못했어요",
                "희귀·전설 유닛과 클리어 기록을 확인해 주세요.\n직접 목표를 고르려면 직접 설정 모드를 선택하세요.",
                "현재 패와 클리어 기록만으로 목표를 정하기 어려워 임의로 조합을 권하지 않습니다.",
                frame.HasKnownDifficulty
                    ? $"{frame.DifficultyLabel}에서 1상위로 클리어한 기록과 현재 패가 확인되면 다시 추천합니다."
                    : "난이도가 확인되면 그 난이도의 클리어 기록을 참고해 목표를 추천합니다.");
        if (!rewardBlocked && step is not null) return Craft(step, false);

        var ownedGoal = goal is not null && frame.Inventory.GetValueOrDefault(goal.Id) > 0;
        if (frame.Difficulty is "신" or "악몽" && frame.GreenBloodAvailable && goal is not null)
        {
            foreach (var advice in frame.GreenBlood.Where(item => string.IsNullOrWhiteSpace(item.Warning)))
            {
                var recipient = catalog.Unit(advice.UnitId);
                UnitDefinition? host = recipient;
                if (advice.Seraphim)
                {
                    var strategy = lead?.CurrentCraft?.Strategy ?? GoalStrategyCalculator.StrategyProfileFor(goal);
                    if (strategy is null) continue;
                    var assessment = new CurrentCraftPolicy(catalog.Unit, goal, frame.Inventory, strategy.Value,
                        frame.Round, frame.CompletedStoryStage, new RecipeCompletionCalculator(catalog.Unit), conditionContext)
                        .Assess(recipient);
                    if (!assessment.MaterialsReady || assessment.LosesRequiredCombat || assessment.GoalMaterialLoss > 0)
                        continue;
                    host = recipient.Recipe.Keys.Select(catalog.Unit).FirstOrDefault(unit =>
                        TopGradePolicy.BaseTier(unit.Tier) is "전설" or "히든");
                }
                else if (frame.Inventory.GetValueOrDefault(recipient.Id) <= 0 || ConsumesGoal(recipient.Id))
                    continue;
                if (host is null || TopGradePolicy.BaseTier(host.Tier) is not ("전설" or "히든") &&
                    !(registeredGuide && host.Id == "rawcode:U30h")) continue;
                var markers = frame.CombatObservations.Where(unit =>
                    unit.Kind is CombatUnitKind.LocalUnit or CombatUnitKind.Bullet &&
                    host.Rawcodes.Contains(new string(unit.Rawcode.Reverse().ToArray())))
                    .Select(unit => unit.BulletAbilities).OfType<BulletAbilityObservation>().ToArray();
                var observed = markers.Length == 1 ? markers[0] : null;
                var targetDetail = observed is null ? "대상 유닛과 능력 미확인" :
                    "게임에서 대상 능력을 읽었습니다. 실제 사용 여부와 현재 효과는 미확인";
                return Make(CoachActionKind.Item, "greenblood:" + recipient.Id, $"{host.Name} 그린블러드 대상 확인",
                    "사용은 보류하세요. 게임에서 내 유닛이 맞는지, 그린블러드 사용 버튼을 누를 수 있는지 직접 확인하세요.",
                    advice.Reason + " · 현재 가진 유닛 중 후보입니다. " + targetDetail,
                    "수동 확인은 게임에서 실제 사용을 확인한 것은 아닙니다. 능력을 다시 읽으면 안내를 갱신합니다.")
                    with { TargetUnitId = recipient.Id };
            }
        }
        if (registeredGuide) return BulletGuideAdvice.Hold(frame, catalog);

        if (ownedGoal && lead?.CombatReadiness is { IsReady: true } &&
            frame.Signals.GetValueOrDefault("upgrade-level") is { } level &&
            frame.Signals.GetValueOrDefault("upgrade-cost") is > 0 and var cost &&
            frame.Signals.GetValueOrDefault("gold") is { } gold && gold >= cost)
            return Make(CoachActionKind.Upgrade, $"upgrade:{goal!.Id}:{level}",
                $"{goal.Name} 강화 한 번 확인",
                $"{goal.Name} 선택 → 현재 표시된 강화 비용 {cost} 확인 → 강화 한 번",
                "지원 수치가 충족되고 현재 강화 비용을 감당할 수 있습니다. 전투력 보장은 아닙니다.",
                "강화 단계가 실제로 올랐는지 확인한 뒤 다음 강화를 안내합니다.") with { TargetUnitId = goal.Id };

        var dismantle = frame.Dismantles.FirstOrDefault(item => item.Dismantle &&
            frame.Inventory.GetValueOrDefault(item.UnitId) > 0 &&
            !GoalStrategyCalculator.StrategyMetricsFor(catalog.Unit(item.UnitId)).HasAny &&
            !ConsumesGoal(item.UnitId));
        if (dismantle is not null)
            return Make(CoachActionKind.Economy, "dismantle:" + dismantle.UnitId,
                $"{catalog.Unit(dismantle.UnitId).Name} 1개 분해",
                "해당 유닛 1개 선택 → 게임의 분해 기능 사용",
                dismantle.Reason + " · 목표와 지원 조합의 재료는 보존합니다.",
                "유닛 감소와 새 재료 확인 후 다시 계산합니다.") with { TargetUnitId = dismantle.UnitId };

        var disposable = frame.Rerolls.FirstOrDefault(item =>
            item.RerollCount > 0 && !GoalStrategyCalculator.StrategyMetricsFor(catalog.Unit(item.UnitId)).HasAny &&
            !ConsumesGoal(item.UnitId));
        if (disposable is not null &&
            (disposable.Sell || frame.Signals.GetValueOrDefault("reroll-cost") is > 0 and var rerollCost &&
                frame.Signals.GetValueOrDefault("lumber") is { } lumber && lumber >= rerollCost))
            return Make(CoachActionKind.Economy, $"economy:{disposable.UnitId}:{disposable.Sell}",
                $"{disposable.Name} 여유분 1개 {(disposable.Sell ? "판매" : "리롤")}",
                $"{disposable.Name} 1개 선택 → 게임의 {(disposable.Sell ? "판매" : "리롤")} 기능 사용",
                disposable.Reason + " · 현재 목표 재료와 보유 지원 수치는 보존합니다.",
                "패 변화 확인 후 다시 계산합니다. 여러 개를 연속 처리하지 마세요.")
                with { TargetUnitId = disposable.UnitId };

        if (lead is not null)
        {
            var unit = catalog.Unit(lead.Route.GoalUnitId);
            var missing = lead.RecipeProgress.MissingLeaves.Take(3)
                .Select(leaf => $"{leaf.Name} {leaf.MissingCount}개").ToArray();
            var instructions = missing.Length > 0
                ? "다음 재료 확보: " + string.Join(" · ", missing)
                : "최하위 유닛 재료는 확보됐습니다.\n실행할 조합은 아직 확인되지 않았습니다.\n판매·분해·조합은 보류하고 현재 패를 보존하세요.";
            return Make(missing.Length > 0 ? CoachActionKind.Gather : CoachActionKind.Waiting,
                (missing.Length > 0 ? "gather:" : "craft-hold:") + unit.Id,
                missing.Length > 0 ? $"{unit.Name} 재료를 모으세요" : $"{RecommendationPresentation.CoachUnitName(unit.Name, unit.Tier)} 조합 보류", instructions,
                lead.Warnings.FirstOrDefault() ?? (missing.Length > 0
                    ? "현재 목표를 유지하며 다음 조합에 필요한 재료를 모읍니다."
                    : "재료 완성률은 하위 조합을 포함한 수치입니다.\n안전한 즉시 조합을 뜻하지는 않습니다."),
                "새 패·자원·스토리 상태를 확인하세요.\n실행 가능한 단계가 계산되면\n재료와 게임 조작을 표시합니다.") with
            {
                TargetUnitId = unit.Id, MaterialCompletion = lead.RecipeProgress.CompletionRatio,
                RecipePreview = Preview(),
                Alternative = disposable is not null
                    ? "리롤 비용이 미확인입니다. 자원을 확인하기 전에는 패와 목재를 보존하세요."
                    : "지금 조합 가능한 안전한 대체 기물이 없습니다. 스토리 보상과 다음 패를 확인합니다."
            };
        }
        return Make(CoachActionKind.Maintain, "maintain", "현재 배치를 유지하고 다음 관문을 확인하세요",
            "주력과 스턴의 공격 범위를 확인하세요. 불필요한 판매·분해는 보류합니다.",
            "지금 실행할 조합을 확인하지 못했습니다.",
            "새 패·스토리·항법 상태가 확인되면 자동으로 갱신합니다.");

        CoachDecision ReceivedReward() => Make(CoachActionKind.Reward, "reward:" + rewardWispId,
            $"받은 {rewardWispName}을 한 번 사용하세요",
            $"게임에서 {rewardWispName} 선택 → 소환 한 번 → 실제로 나온 패 확인",
            $"현재 {rewardWispName} {frame.RewardWisps[rewardWispId!]}개 보유가 확인됐습니다.\n소환 결과는 미리 확정하지 않습니다.",
            "위습 감소와 새 패를 확인한 뒤 다음 조합을 계산합니다.") with { RewardWispId = rewardWispId };

        CoachDecision Story(bool urgent)
        {
            var attacker = frame.Inventory.Where(pair => pair.Value > 0).Select(pair => catalog.Unit(pair.Key))
                .Select(unit => (Unit: unit, Metrics: GoalStrategyCalculator.StrategyMetricsFor(unit)))
                .Where(item => item.Metrics.SingleDamage > 0 || item.Metrics.FinisherDamage > 0)
                .OrderBy(item => item.Metrics.Stun > 0)
                .ThenByDescending(item => item.Metrics.SingleDamage + item.Metrics.FinisherDamage)
                .ThenBy(item => item.Unit.Id, StringComparer.Ordinal).FirstOrDefault().Unit;
            return Make(CoachActionKind.Story, "story:" + (frame.CompletedStoryStage + 1),
                frame.Story?.CurrentStoryLabel ?? $"스토리 {frame.CompletedStoryStage + 1}단계 진행",
                attacker is null
                    ? "스토리 공격 담당이 확인되지 않았습니다. 전설 전력을 확인하고 라인 제어 유닛은 남기세요."
                    : $"{attacker.Name} 선택 → 현재 스토리 목표 공격. 라인을 지킬 스턴 유닛은 남기세요.",
                urgent ? "악몽은 35라 전 스토리 13단계 완료가 필요합니다."
                    : frame.Story is { } story ? "예정 보상 · " + story.ClearRewardSummary
                        : "스토리 보상이 다음 조합의 재료가 됩니다.",
                "스토리 완료 단계가 증가하면 다음 행동으로 넘어갑니다.", urgent);
        }

        CoachDecision Craft(AutoCombineStep action, bool urgent)
        {
            var unit = catalog.Unit(action.TargetUnitId);
            var conditions = RecipeConditionEvaluator.Evaluate(unit, conditionContext);
            if (!conditions.IsSatisfied || (unit.RecipeConditions is not null &&
                RecipeWildcards.AllocateDirect(unit, frame.Inventory, catalog.Unit) is null))
                return Make(CoachActionKind.Waiting, "recipe-conditions:" + unit.Id,
                    "니카 조합 보류", "조건 확인 전 재료를 보존하세요.",
                    conditions.IsSatisfied ? "실제 영웅 재료가 부족합니다." : conditions.Reason,
                    "현재 플레이어의 특성강화·토큰·목재와 실제 재료를 다시 확인합니다.") with { TargetUnitId = unit.Id };
            if (registeredGuide)
            {
                var available = BulletGuideReservations.Available(catalog, frame.Inventory,
                    frame.GuidePlan!.ProtectedUnitIds.Concat(frame.SelectedGoalIds)
                        .Concat(new[] { frame.CommittedCraftUnitId }.OfType<string>()
                            .Where(id => !BulletGuideReservations.IsRecipeStep(catalog, frame.GuidePlan.TargetUnitId, id))),
                    frame.GuidePlan.TargetUnitId);
                if (!BulletGuideReservations.IsRecipeStep(catalog, frame.GuidePlan.TargetUnitId, unit.Id) ||
                    unit.Recipe.Any(pair => catalog.Unit(pair.Key).Tier != "자원" &&
                        available.GetValueOrDefault(pair.Key) < pair.Value))
                    return Make(CoachActionKind.Waiting, "guide1:reservation:" + unit.Id,
                        $"{RecommendationPresentation.CoachUnitName(unit.Name, unit.Tier)} 조합 보류", "지원 조합에 필요한 패와 불릿 재료를 남기세요.",
                        "이 조합을 하면 다른 목표에 필요한 재료가 부족해집니다.",
                        "실제 여유분이 생기면 다시 계산합니다.") with { TargetUnitId = unit.Id };
            }
            if (registeredGuide && (unit.Id == "rawcode:IC0h" && frame.GuidePlan?.QueenConversionConfirmed != true ||
                !BulletGuideCraftSafety.Allows(catalog, unit.Id, frame.Inventory,
                    frame.Round, frame.ConfirmedNavigation, frame.GuidePlan?.Support?.ArmorTarget ?? 100,
                    frame.GuidePlan?.QueenConversionConfirmed == true, frame.GuidePlan)))
                return Make(CoachActionKind.Waiting, "guide1:craft-safety:" + unit.Id,
                    $"{RecommendationPresentation.CoachUnitName(unit.Name, unit.Tier)} 조합 보류", "현재 지원 오라·스턴·흔함 예비분을 보존하세요.",
                    "공략 순서 또는 조합 후 남는 전력을 아직 충족하지 못했습니다.",
                    "대체 지원과 새 재료가 확인되면 다시 계산합니다.") with { TargetUnitId = unit.Id };
            if (registeredGuide && TopGradePolicy.IsTopGrade(unit.Tier))
            {
                var actual = frame.ConfirmedNavigation is { } navigation ? NavigationProfiles.Find(navigation) : null;
                var after = BulletGuideCraftSafety.ProjectAfterCraft(catalog, unit.Id, frame.Inventory)!;
                var afterTopCount = after.Where(pair => TopGradePolicy.IsTopGrade(catalog.Unit(pair.Key).Tier)).Sum(pair => pair.Value);
                if (actual is null || afterTopCount > actual.TopUnitLimit)
                    return Make(CoachActionKind.Waiting, "guide1:actual-navigation-limit:" + unit.Id,
                        $"{RecommendationPresentation.CoachUnitName(unit.Name, unit.Tier)} 조합 보류",
                        "바운티헌터 계획은 유지합니다. 실제 항법을 확인하고 허용된 상위 수 안에서만 조합하세요.",
                        actual is null ? "게임에서 선택된 항법은 아직 확인되지 않았습니다. 앱에서 고른 계획만으로 조합하지 마세요."
                            : $"실제 {actual.Name} 상위 제한 {actual.TopUnitLimit}기와 조합 후 {afterTopCount}기가 충돌합니다.",
                        "실제 선택과 현재 패를 새로 확인한 뒤 다시 계산합니다.") with { TargetUnitId = unit.Id };
            }
            var controls = action.Commands.Count > 0
                ? $"{action.TriggerName} 선택 → 채팅에 {action.Commands[0]} 입력"
                : $"{action.TriggerName} 선택 → {action.Key} 키";
            var resourceCosts = unit.Recipe.Select(pair => (Unit: catalog.Unit(pair.Key), pair.Value))
                .Where(pair => pair.Unit.Tier == "자원").ToArray();
            var missingResource = resourceCosts.FirstOrDefault(pair =>
                ResourceName(pair.Unit) is not { } name ||
                (frame.Signals.GetValueOrDefault(name) is not { } available || available < pair.Value));
            if (missingResource.Unit is not null)
                return Make(CoachActionKind.Economy, "resource:" + unit.Id,
                    $"{missingResource.Unit.Name} {missingResource.Value}개 확인 후 조합",
                    registeredGuide ? "게임에서 자원이 확인될 때까지 조합하지 마세요. 현재 패와 목재를 남겨두세요."
                        : $"게임 자원 표시를 확인하세요. 부족하면 저축, 충분하면 {controls}",
                    "유닛 재료 완성과 조합 자원 확보는 다릅니다. 미확인 잔량을 추측하지 않습니다.",
                    $"{action.TargetName} 보유가 확인되면 다음 행동으로 넘어갑니다.", urgent) with
                { TargetUnitId = unit.Id, MaterialCompletion = 1, CraftRecipe = Recipe(unit.Id, 1), RecipePreview = Preview() };
            return Make(CoachActionKind.Craft, "craft:" + unit.Id, $"{RecommendationPresentation.CoachUnitName(unit.Name, unit.Tier)} 조합",
                controls, lead?.Warnings.FirstOrDefault() ??
                    (urgent ? "현재 생존·스토리 부족을 먼저 보완합니다." : "현재 패로 가능한 다음 조합입니다."),
                $"{action.TargetName} 보유가 확인되면 다음 행동으로 넘어갑니다.", urgent) with
            { TargetUnitId = unit.Id, MaterialCompletion = 1, CraftRecipe = Recipe(unit.Id, 1), RecipePreview = Preview() };
        }

        // A recipe is a preview, not an AutoCombineStep. Never infer execution permission or keys here.
        IReadOnlyList<RecipeCraftStep> Preview() => lead?.RemainingCraftSteps
            .Where(item => item.MissingCount > 0)
            .Select(item => Recipe(item.UnitId, item.MissingCount)).ToArray() ?? [];

        RecipeCraftStep Recipe(string unitId, int count)
        {
            var unit = catalog.Unit(unitId);
            return new RecipeCraftStep
            {
                Conditions = RecipeConditionEvaluator.Evaluate(unit, conditionContext),
                UnitId = unit.Id, Name = unit.Name, Tier = unit.Tier, RequiredCount = count,
                Ingredients = unit.Recipe.Select(pair => new RecipeCraftIngredient
                {
                    UnitId = pair.Key, Name = catalog.Unit(pair.Key).Name, Tier = catalog.Unit(pair.Key).Tier,
                    RequiredCount = checked(pair.Value * count)
                }).ToList()
            };
        }

        bool ConsumesGoal(string unitId)
        {
            if (goal is null || frame.Inventory.GetValueOrDefault(unitId) <= 0) return true;
            var calculator = new RecipeCompletionCalculator(catalog.Unit);
            var required = new[] { goal.Id }.Concat(frame.SelectedGoalIds)
                .Concat(frame.Recommendations.Select(item => item.Route.GoalUnitId))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var before = calculator.Calculate(required, frame.Inventory);
            var after = calculator.Calculate(required, frame.Inventory.SetItem(unitId, frame.Inventory[unitId] - 1));
            return after.OwnedLeafCount < before.OwnedLeafCount;
        }

        CoachDecision Make(CoachActionKind kind, string id, string title, string controls,
            string reason, string confirmation, bool urgent = false)
        {
            var deferred = rewardBlocked && kind is CoachActionKind.Story or CoachActionKind.Reward or CoachActionKind.Navigation;
            if (deferred)
            {
                reason = (waitingForReward
                    ? "조합 보류: 예정된 스토리 보상이 필요한 재료와 다음 조합을 바꿀 수 있습니다."
                    : "조합 보류: 보유한 보상 위습의 실제 결과를 먼저 반영해야 합니다.") + "\n" + reason;
                var resume = waitingForReward
                    ? "조합 재개 조건: 스토리 보상을 받은 뒤 해당 위습을 사용하고, 실제 결과가 새 정상 패에 반영되어 보상 대기·사용 단계가 끝나면 다시 계산합니다."
                    : "조합 재개 조건: 해당 보상 위습의 사용 결과가 새 정상 패에 반영되고 보상 대기·사용 단계가 끝나면 다시 계산합니다.";
                confirmation = kind == CoachActionKind.Navigation ? confirmation + "\n" + resume : resume;
            }
            var preserved = goal is null || kind == CoachActionKind.Finished
                ? ImmutableDictionary<string, long>.Empty
                : new RecipeCompletionCalculator(catalog.Unit).CalculateAllocation(
                    new[] { goal.Id, frame.CommittedCraftUnitId, lead?.Route.GoalUnitId }.OfType<string>()
                        .Concat(frame.SelectedGoalIds).Distinct(StringComparer.OrdinalIgnoreCase), frame.Inventory)
                    .ConsumedByUnitId.ToImmutableDictionary();
            return new(kind, id, title, controls, reason, confirmation, milestone)
            {
                CraftDeferredForReward = deferred,
                // Optional preparation only; never replace the action or infer a confirmed selection.
                ShowBountyHunterPreparationTip = frame.Round == 19 && frame.IsCurrent &&
                    frame.ConfirmedNavigation is null && frame.SuggestedNavigation == "PathOfKings.BountyHunter" &&
                    !urgent && !rewardBlocked && lead?.CurrentCraft is not { Priority: <= 1 } &&
                    lead?.CurrentCraft is not { LosesRequiredCombat: true } &&
                    kind is CoachActionKind.Craft or CoachActionKind.Gather or CoachActionKind.Maintain or
                        CoachActionKind.Economy or CoachActionKind.Upgrade or CoachActionKind.Item,
                RecipePreview = deferred ? Preview() : [],
                GoalLabel = frame.SelectedGoalIds.Length > 0
                    ? string.Join(" + ", frame.SelectedGoalIds.Select(id => catalog.Unit(id).Name))
                    : goal is null ? "스토리 보상 확인 후 목표 자동 결정" : goal.Name,
                IsUrgent = urgent,
                MilestoneRound = milestoneRound,
                Constraint = frame.NavigationConstraint,
                PreservedMaterialCounts = preserved,
                PreservedMaterials = goal is null ? "첫 희귀함과 스토리 재료는 보존하세요."
                    : string.Join(" · ", preserved.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{catalog.Unit(pair.Key).Name} {pair.Value}개")),
                UnknownSignals = string.Join(" · ", new[] { ("gold", "골드"), ("lumber", "목재"),
                    ("boss-hp", "보스 체력"), ("line-count", "라인 수") }
                    .Where(pair => frame.Signals.GetValueOrDefault(pair.Item1) is null).Select(pair => pair.Item2)),
                OperationGuide = registeredGuide ? BulletGuideAdvice.Operation(frame)
                    : goal is null ? "" : OperationGuide(goal, frame),
                Milestone = registeredGuide ? BulletGuideAdvice.Stage(frame.GuidePlan!) : milestone
            };
        }
    }

    private static string? ResourceName(UnitDefinition unit) =>
        unit.Rawcodes.FirstOrDefault() switch
        {
            "GOLD" => "gold", "LUMBER" => "lumber", "POINT" => "trait-points", _ => null
        };

    private string OperationGuide(UnitDefinition goal, CoachFrame frame)
    {
        var owned = frame.Inventory.Where(pair => pair.Value > 0).Select(pair => catalog.Unit(pair.Key)).ToArray();
        var boss = owned.FirstOrDefault(unit => GoalStrategyCalculator.StrategyMetricsFor(unit).BossControl > 0);
        var stun = owned.FirstOrDefault(unit => GoalStrategyCalculator.StrategyMetricsFor(unit).Stun > 0);
        var lines = new List<string>();
        if (boss is not null) lines.Add($"{boss.Name}: 보스 출현 시 공격 대상 확인");
        if (stun is not null) lines.Add($"{stun.Name}: 공격 범위가 라인에 닿도록 배치 확인");
        var source = goal.Description.Split('\n').Where(line =>
            line.Contains("스킬", StringComparison.Ordinal) || line.Contains("강화", StringComparison.Ordinal))
            .Take(2);
        lines.AddRange(source.Select(line => "유닛 설명: " + line));
        if (lines.Count > 0) lines.Add("배치·타깃·스킬 사용은 자동 확인하지 못합니다.");
        return string.Join("\n", lines);
    }
}
