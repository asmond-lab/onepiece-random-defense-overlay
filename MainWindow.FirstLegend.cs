using System.Collections.Immutable;

namespace OrandOverlay;

public partial class MainWindow
{
    private BulletGuidePlan? PrepareFirstLegend(BulletGuidePlan? plan, IReadOnlyList<InventoryEntry> inventory)
    {
        if (plan is null) return null;
        RequestBulletLearning();
        return _coachSession.FirstLegend.Prepare(plan, new CoachFrame
        {
            Mode = CurrentPlayMode, GuideNumber = _settings.GuideNumber, GuidePlan = plan,
            MatchGeneration = _adaptivePlanning.MatchGeneration, RecognitionRevision = _recognitionRevision,
            Revision = _coachRevision, Round = _lastRound, CompletedStoryStage = _mapSignals.CompletedStoryStageOrdinal,
            IsCurrent = _coachCurrent && !_automaticStale && !_automaticDisconnected,
            Paused = _coachPaused, Outcome = _outcome.Outcome, ConfirmedNavigation = EffectiveNavigation,
            Inventory = inventory.ToImmutableDictionary(entry => entry.UnitId, entry => entry.Count),
            RewardWisps = _mapSignals.RewardWisps
        });
    }
}
