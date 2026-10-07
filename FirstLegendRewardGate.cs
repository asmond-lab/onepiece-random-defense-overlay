using System.Collections.Immutable;

namespace OrandOverlay;

/// <summary>Observed opening rewards and recipe execution, scoped to one match.</summary>
internal sealed class FirstLegendRewardGate(DataCatalog catalog)
{
    internal static readonly string[] RewardIds = ["e016", "e017", "e019", "e0IX", "e018", "e01A"];
    private CoachFrame? _previous;
    private CoachFrame? _offeredFrame;
    private string? _offeredStep;
    private SelectionWispBatch? _selectionOffer;
    private SelectionWispBatch? _selectionBatch;
    private BulletGuideStage _selectionStage;
    private readonly Dictionary<string, int> _pendingOutputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _earlyOutputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _manualSelectionOutputs = new(StringComparer.Ordinal);
    private string? _outputOffer;
    private SelectionWispBatch? _earlySelectionOffer;
    private readonly HashSet<string> _checkpointRewards = new(StringComparer.Ordinal);
    private bool _checkpointOutputObserved;
    private bool _checkpointHandObserved;
    private long _generation = -1;
    public string? CommittedLegendId { get; private set; }

    public BulletGuidePlan Prepare(BulletGuidePlan plan, CoachFrame observation)
    {
        if (observation.MatchGeneration < _generation) return plan;
        if (observation.MatchGeneration != _generation)
        {
            Reset();
            _generation = observation.MatchGeneration;
        }
        var accepted = observation.IsCurrent && !observation.Paused && observation.Outcome is not ("clear" or "fail") &&
            (_previous is null || observation.RecognitionRevision > _previous.RecognitionRevision);
        if (accepted)
        {
            Observe(observation);
            _previous = observation;
        }
        if (_selectionBatch is { } batch)
        {
            if (accepted && (plan.Stage != _selectionStage || observation.Inventory.GetValueOrDefault(batch.TargetUnitId) > 0))
                _selectionBatch = null;
            else plan = plan with { TargetUnitId = batch.TargetUnitId };
        }
        plan = plan with
        {
            SelectionBatch = _selectionBatch,
            AwaitingRewardHand = _pendingOutputs.Count > 0,
            PendingSelectionOutputs = _pendingOutputs.GetValueOrDefault("e018")
        };
        if (plan.Stage != BulletGuideStage.FirstLegend)
        {
            CommittedLegendId = null;
            _offeredFrame = null;
            _offeredStep = null;
            return plan;
        }
        if (CommittedLegendId is { } root)
        {
            var committedPlan = plan with { TargetUnitId = root };
            var ready = catalog.Unit(root).Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원")
                .All(pair => observation.Inventory.GetValueOrDefault(pair.Key) >= pair.Value);
            if (ready && !BulletGuideCraftSafety.Allows(catalog, root, observation.Inventory,
                    observation.Round, observation.ConfirmedNavigation, plan.Support?.ArmorTarget ?? 100,
                    plan.QueenConversionConfirmed, committedPlan))
                CommittedLegendId = null;
            else plan = committedPlan;
        }
        return plan with
        {
            FirstLegendRewardHandObserved = _checkpointHandObserved
        };
    }

    private void Observe(CoachFrame frame)
    {
        if (_previous is { } previous) ObserveRewardOutputs(previous, frame);

        if (_offeredFrame is { } offered && _offeredStep is { } step &&
            frame.Inventory.GetValueOrDefault(step) > offered.Inventory.GetValueOrDefault(step) &&
            catalog.Unit(step).Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원").All(pair =>
                frame.Inventory.GetValueOrDefault(pair.Key) <= offered.Inventory.GetValueOrDefault(pair.Key) - pair.Value))
        {
            CommittedLegendId = offered.GuidePlan!.TargetUnitId;
            _offeredFrame = null;
            _offeredStep = null;
        }
        if (CommittedLegendId is { } root && _previous is { } baseline)
        {
            var recipes = new RecipeCompletionCalculator(catalog.Unit);
            if (frame.Inventory.GetValueOrDefault(root) > 0 ||
                !HasPartialCraft(frame) && recipes.Calculate([root], frame.Inventory).OwnedLeafCount <
                recipes.Calculate([root], baseline.Inventory).OwnedLeafCount)
                CommittedLegendId = null;
        }
    }

    private void ObserveRewardOutputs(CoachFrame previous, CoachFrame current)
    {
        foreach (var id in RewardIds)
        {
            // Unsupported outputs remain unknown; they create neither checkpoint evidence nor impossible debt.
            if (id is not ("e016" or "e017" or "e018" or "e019")) continue;
            var consumed = previous.RewardWisps.GetValueOrDefault(id) - current.RewardWisps.GetValueOrDefault(id);
            if (id == "e018" && consumed < 0) _manualSelectionOutputs.Clear();
            // A new grant is a different receipt episode, not permission to spend old gains.
            if (consumed < 0 && _earlyOutputs.Keys.Any(unitId => OutputReward(unitId) == id))
                ClearEarlyOutputs();
            if (consumed <= 0) continue;
            if (id == "e018" && _selectionBatch is null)
                _selectionBatch = _earlySelectionOffer ?? _selectionOffer;
            _pendingOutputs[id] = checked(_pendingOutputs.GetValueOrDefault(id) + consumed);
            // The held wisp must itself have been observed after the completed-stage checkpoint.
            if (previous.CompletedStoryStage >= 4 && id is "e016" or "e017")
                _checkpointRewards.Add(id);
        }

        // Bodies can enter the unit pool before their spent wisp leaves it. Only gains
        // observed during the offered receipt may bridge that ordering, once per body.
        foreach (var (unitId, credit) in _earlyOutputs.ToArray())
        {
            var lost = Math.Max(0, previous.Inventory.GetValueOrDefault(unitId) - current.Inventory.GetValueOrDefault(unitId));
            var available = Math.Max(0, credit - lost);
            var remaining = available - MatchOutput(unitId, OutputReward(unitId), available);
            if (remaining == 0) _earlyOutputs.Remove(unitId);
            else _earlyOutputs[unitId] = remaining;
        }
        if (_earlyOutputs.Count == 0) _earlySelectionOffer = null;

        // Manual selections have no offered identities. Apply the same common-pool
        // matching as wisp-first receipts, but never lend these gains to an offered batch.
        if (_selectionBatch is not null) _manualSelectionOutputs.Clear();
        foreach (var (unitId, credit) in _manualSelectionOutputs.ToArray())
        {
            var lost = Math.Max(0, previous.Inventory.GetValueOrDefault(unitId) - current.Inventory.GetValueOrDefault(unitId));
            var available = Math.Max(0, credit - lost);
            var remaining = available - MatchOutput(unitId, "e018", available);
            if (remaining == 0) _manualSelectionOutputs.Remove(unitId);
            else _manualSelectionOutputs[unitId] = remaining;
        }

        foreach (var (unitId, count) in current.Inventory)
        {
            var gain = count - previous.Inventory.GetValueOrDefault(unitId);
            if (gain <= 0) continue;
            var reward = OutputReward(unitId);
            gain -= MatchOutput(unitId, reward, gain);
            if (gain > 0 && reward == "e018" && _outputOffer is null &&
                _selectionBatch is null && _earlySelectionOffer is null &&
                previous.RewardWisps.GetValueOrDefault("e018") > 0)
            {
                var credit = Math.Min(gain, Math.Max(0, current.RewardWisps.GetValueOrDefault("e018") -
                    _manualSelectionOutputs.Values.Sum()));
                if (credit > 0) _manualSelectionOutputs[unitId] = _manualSelectionOutputs.GetValueOrDefault(unitId) + credit;
            }
            if (gain <= 0 || reward is null || reward != _outputOffer ||
                previous.RewardWisps.GetValueOrDefault(reward) <= 0) continue;
            gain = Math.Min(gain, Math.Max(0, current.RewardWisps.GetValueOrDefault(reward) - _earlyOutputs.Values.Sum()));
            if (reward == "e018")
            {
                var selected = _selectionOffer?.Items.FirstOrDefault(item => item.UnitId == unitId);
                gain = Math.Min(gain, selected?.RemainingCount ?? 0);
                if (gain > 0) _earlySelectionOffer ??= _selectionOffer;
            }
            if (gain > 0) _earlyOutputs[unitId] = _earlyOutputs.GetValueOrDefault(unitId) + gain;
        }
        if (!_pendingOutputs.Any(pair => pair.Key != "e018") && current.CompletedStoryStage >= 4)
            _checkpointHandObserved |= _checkpointOutputObserved;
    }

    private string? OutputReward(string unitId)
    {
        var unit = catalog.Unit(unitId);
        return TopGradePolicy.BaseTier(unit.Tier) switch
        {
            "특별함" => "e016", "안흔함" => "e017", "희귀함" => "e019",
            "흔함" when unit.Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains) => "e018",
            _ => null
        };
    }

    private int MatchOutput(string unitId, string? reward, int gain)
    {
        if (reward is null || !_pendingOutputs.TryGetValue(reward, out var pending)) return 0;
        var matched = Math.Min(gain, pending);
        if (reward == "e018" && _selectionBatch is { } batch)
        {
            matched = Math.Min(matched, batch.Items.FirstOrDefault(item => item.UnitId == unitId)?.RemainingCount ?? 0);
            _selectionBatch = batch with { Items = batch.Items.Select(row => row.UnitId == unitId
                ? row with { RemainingCount = row.RemainingCount - matched } : row).ToImmutableArray() };
        }
        _checkpointOutputObserved |= _checkpointRewards.Contains(reward) && matched > 0;
        if (matched == pending) _pendingOutputs.Remove(reward);
        else _pendingOutputs[reward] = pending - matched;
        return matched;
    }

    private void ClearEarlyOutputs()
    {
        _earlyOutputs.Clear();
        _earlySelectionOffer = null;
    }

    public void Record(CoachFrame frame, CoachDecision decision)
    {
        if (frame.Mode != PlayMode.Guide || frame.GuideNumber != 1)
        {
            Reset();
            return;
        }
        if (frame.Round <= 0)
        {
            _manualSelectionOutputs.Clear();
            _selectionOffer = null;
            _outputOffer = null;
            ClearEarlyOutputs();
            _offeredFrame = null;
            _offeredStep = null;
            return;
        }
        if (frame.IsCurrent && !frame.Paused && frame.GuideVisible && frame.Outcome is not ("clear" or "fail") &&
            (_previous is null || frame.RecognitionRevision >= _previous.RecognitionRevision))
        {
            var outputOffer = decision.Kind == CoachActionKind.Reward ? decision.RewardWispId : null;
            if (outputOffer is not null) _manualSelectionOutputs.Clear();
            // A completed selected hand can offer its craft before the spent wisps disappear.
            var selectedCraft = (decision.Kind == CoachActionKind.Craft || decision.CraftProgress?.AwaitingRecognition == true) &&
                _earlySelectionOffer is not null;
            if (!selectedCraft && (outputOffer != _outputOffer || outputOffer is null) ||
                outputOffer == "e018" && _earlySelectionOffer is { } early &&
                decision.SelectionBatch?.TargetUnitId != early.TargetUnitId)
                ClearEarlyOutputs();
            _outputOffer = outputOffer;
            _selectionOffer = decision.Kind == CoachActionKind.Reward && decision.RewardWispId == "e018"
                ? decision.SelectionBatch : null;
            if (_selectionBatch is not null && _selectionOffer is not null) _selectionBatch = _selectionOffer;
            if (_selectionOffer is not null) _selectionStage = frame.GuidePlan!.Stage;
        }
        if (frame.IsCurrent && !frame.Paused && frame.GuideVisible &&
            frame.GuidePlan?.Stage == BulletGuideStage.FirstLegend && decision.Kind == CoachActionKind.Craft)
        {
            if (_offeredStep != decision.TargetUnitId) _offeredFrame = frame;
            _offeredStep = decision.TargetUnitId;
        }
        else if (decision.CraftProgress?.AwaitingRecognition != true)
        {
            _offeredFrame = null;
            _offeredStep = null;
        }
    }

    private bool HasPartialCraft(CoachFrame frame) => _offeredFrame is { } offered && _offeredStep is { } step &&
        (frame.Inventory.GetValueOrDefault(step) > offered.Inventory.GetValueOrDefault(step) ||
         catalog.Unit(step).Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원").Any(pair =>
             frame.Inventory.GetValueOrDefault(pair.Key) < offered.Inventory.GetValueOrDefault(pair.Key)));

    private void Reset()
    {
        _previous = null;
        _offeredFrame = null;
        _offeredStep = null;
        _selectionOffer = null;
        _selectionBatch = null;
        _pendingOutputs.Clear();
        _manualSelectionOutputs.Clear();
        _outputOffer = null;
        ClearEarlyOutputs();
        _checkpointRewards.Clear();
        _checkpointOutputObserved = false;
        _checkpointHandObserved = false;
        CommittedLegendId = null;
    }
}
