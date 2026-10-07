namespace OrandOverlay;

// A displayed action is not an input event. Only a matched inventory receipt completes it.
internal sealed class ObservedCraftSession(DataCatalog catalog)
{
    private long _generation = -1;
    private long _recognition = -1;
    private string _plan = "";
    private string? _root;
    private readonly Dictionary<string, int> _completed = new(StringComparer.Ordinal);
    private CoachFrame? _baseline;
    private CoachDecision? _offer;
    private CoachDecision? _acceptedDecision;
    private CoachFrame? _rewardReceipt;

    public CoachDecision Update(CoachFrame frame, CoachDecision decision)
    {
        if (!frame.IsCurrent || frame.Paused || !frame.GuideVisible) return decision;
        var root = frame.GuidePlan?.TargetUnitId ?? frame.Recommendations.FirstOrDefault()?.Route.GoalUnitId ?? frame.GoalId;
        var plan = $"{frame.Mode}:{frame.GuideNumber}:{frame.GuidePlan?.Stage}:{root}:{frame.GoalId}:" +
            string.Join(",", frame.SelectedGoalIds);
        var rootReceipt = _baseline is { } prior && _offer?.TargetUnitId == _root && _root is not null &&
            frame.Inventory.GetValueOrDefault(_root) > prior.Inventory.GetValueOrDefault(_root) &&
            frame.Mode == prior.Mode && frame.GoalId == prior.GoalId && frame.GuideNumber == prior.GuideNumber &&
            frame.SelectedGoalIds.SequenceEqual(prior.SelectedGoalIds);
        if (_generation != frame.MatchGeneration || _plan != plan && !rootReceipt || frame.Outcome is "clear" or "fail" || frame.Round <= 0)
        {
            _completed.Clear();
            _baseline = null;
            _offer = null;
            _acceptedDecision = null;
            _rewardReceipt = null;
            _recognition = -1;
            _generation = frame.MatchGeneration;
            _plan = plan;
            _root = root;
        }
        if (frame.Round <= 0 || frame.Outcome is "clear" or "fail")
            return decision;
        var revision = frame.RecognitionRevision > 0 ? frame.RecognitionRevision : frame.Revision;
        var canPresentProgress = decision.Kind is CoachActionKind.Craft or CoachActionKind.Gather ||
            decision.Kind == CoachActionKind.Waiting && decision.TargetUnitId is null && !decision.IsUrgent;
        if (revision <= _recognition)
            return _acceptedDecision?.CraftProgress is not null && canPresentProgress
                ? _acceptedDecision : decision;
        _recognition = revision;

        if (_baseline is { } before && _offer?.TargetUnitId is { } unitId)
        {
            var ingredients = catalog.Unit(unitId).Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원").ToArray();
            var rewardId = TopGradePolicy.BaseTier(catalog.Unit(unitId).Tier) switch
            {
                "안흔함" => "e017", "특별함" => "e016", "희귀함" => "e019", _ => null
            };
            if (rewardId is not null && decision.Kind == CoachActionKind.Reward && decision.RewardWispId == rewardId)
                _rewardReceipt ??= frame;
            if (_rewardReceipt is { } reward && rewardId is not null)
            {
                var received = Math.Min(frame.Inventory.GetValueOrDefault(unitId) - before.Inventory.GetValueOrDefault(unitId),
                    reward.RewardWisps.GetValueOrDefault(rewardId) - frame.RewardWisps.GetValueOrDefault(rewardId));
                if (received > 0)
                {
                    before = before with { Inventory = before.Inventory.SetItem(unitId, before.Inventory.GetValueOrDefault(unitId) + received) };
                    _baseline = before;
                    _rewardReceipt = frame;
                }
            }
            var competingOutput = false;
            foreach (var other in frame.Inventory.Keys.Select(catalog.Unit).Where(unit => unit.Id != unitId &&
                         !unit.Recipe.ContainsKey(unitId) && unit.Recipe.Keys.Any(id => ingredients.Any(pair => pair.Key == id))))
            {
                var otherGain = frame.Inventory.GetValueOrDefault(other.Id) - before.Inventory.GetValueOrDefault(other.Id);
                if (otherGain <= 0) continue;
                var recipe = other.Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원").ToArray();
                var receipts = Math.Max(0, Math.Min(otherGain, recipe.Min(pair =>
                    (before.Inventory.GetValueOrDefault(pair.Key) - frame.Inventory.GetValueOrDefault(pair.Key)) / pair.Value)));
                competingOutput |= receipts < otherGain;
                if (receipts == 0) continue;
                var settled = before.Inventory.SetItem(other.Id, before.Inventory.GetValueOrDefault(other.Id) + receipts);
                foreach (var pair in recipe)
                    settled = settled.SetItem(pair.Key, settled.GetValueOrDefault(pair.Key) - pair.Value * receipts);
                before = before with { Inventory = settled };
                _baseline = before;
            }
            // An output can already have entered a parent before the next accepted pool scan.
            // Settle observed parent receipts first, so their other inputs cannot pay for this craft.
            foreach (var parent in frame.Inventory.Keys.Select(catalog.Unit).Where(unit => unit.Recipe.ContainsKey(unitId)))
            {
                var parentGain = frame.Inventory.GetValueOrDefault(parent.Id) - before.Inventory.GetValueOrDefault(parent.Id);
                if (parentGain <= 0 || parent.Recipe.Where(pair => pair.Key != unitId && catalog.Unit(pair.Key).Tier != "자원")
                    .Any(pair => before.Inventory.GetValueOrDefault(pair.Key) - frame.Inventory.GetValueOrDefault(pair.Key) < pair.Value * parentGain))
                    continue;
                var settled = before.Inventory.SetItem(parent.Id, frame.Inventory.GetValueOrDefault(parent.Id));
                foreach (var ingredient in parent.Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원"))
                    settled = settled.SetItem(ingredient.Key, settled.GetValueOrDefault(ingredient.Key) - ingredient.Value * parentGain);
                before = before with { Inventory = settled };
                _baseline = before;
            }
            var gain = frame.Inventory.GetValueOrDefault(unitId) - before.Inventory.GetValueOrDefault(unitId);
            var consumed = ingredients.Length == 0 ? 0 : ingredients.Min(pair =>
                (before.Inventory.GetValueOrDefault(pair.Key) - frame.Inventory.GetValueOrDefault(pair.Key)) / pair.Value);
            var matched = Math.Max(0, Math.Min(gain, consumed));
            if (matched > 0)
            {
                _completed[unitId] = _completed.GetValueOrDefault(unitId) + matched;
                // Retain any unmatched half of a multi-output receipt against a virtual settled baseline.
                var inventory = before.Inventory.SetItem(unitId, before.Inventory.GetValueOrDefault(unitId) + matched);
                foreach (var ingredient in ingredients)
                    inventory = inventory.SetItem(ingredient.Key, inventory.GetValueOrDefault(ingredient.Key) - matched * ingredient.Value);
                before = before with { Inventory = inventory };
                _baseline = before;
                gain -= matched;
            }
            var partial = gain > 0 || ingredients.Any(pair =>
                frame.Inventory.GetValueOrDefault(pair.Key) < before.Inventory.GetValueOrDefault(pair.Key));
            if (partial)
            {
                var progress = _offer.CraftProgress! with { CompletedCount = _completed.GetValueOrDefault(unitId), AwaitingRecognition = true };
                var waiting = _offer with
                {
                    Kind = CoachActionKind.Waiting, Id = "craft-recognition:" + unitId,
                    Title = $"{catalog.Unit(unitId).Name} 조합 결과 확인 중",
                    Controls = $"완료 {progress.CompletedCount}/{progress.RequiredCount} · 남은 조합 {progress.RemainingCount}개\n조합을 다시 입력하지 말고 결과 인식을 기다리세요.",
                    Reason = "재료 또는 결과 유닛의 변화만 확인되어 조합 완료를 아직 확정하지 않습니다.",
                    Confirmation = "재료 감소와 결과 유닛이 모두 확인되면 다음 조합을 안내합니다.",
                    CraftRecipe = null, CraftProgress = progress
                };
                // Recognition and safety decisions retain priority over a pending craft.
                _acceptedDecision = canPresentProgress
                    ? waiting : decision;
                return _acceptedDecision;
            }
            if (!competingOutput || matched > 0 || decision.TargetUnitId != unitId)
            {
                _baseline = null;
                _offer = null;
                _rewardReceipt = null;
            }
            if (_plan != plan)
            {
                _completed.Clear();
                _plan = plan;
                _root = root;
            }
        }

        if (decision.Kind == CoachActionKind.Craft && decision.TargetUnitId is { } target)
        {
            var recommendation = frame.Recommendations.FirstOrDefault(item => item.Route.GoalUnitId == root);
            var remaining = recommendation?.RemainingCraftSteps.Where(step => step.UnitId == target).Sum(step => step.MissingCount) ?? 0;
            if (remaining == 0) remaining = 1;
            var completed = _completed.GetValueOrDefault(target);
            var progress = new ObservedCraftProgress(target, checked(completed + remaining), completed, false);
            decision = decision with
            {
                CraftProgress = progress,
                Title = $"{RecommendationPresentation.CoachUnitName(catalog.Unit(target).Name, catalog.Unit(target).Tier)} {progress.RequiredCount}개 만들기",
                Controls = $"완료 {completed}/{progress.RequiredCount} · 남은 조합 {remaining}개\n{decision.Controls}"
            };
            _baseline ??= frame;
            _offer = decision;
        }
        _acceptedDecision = decision;
        return decision;
    }
}
