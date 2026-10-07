namespace OrandOverlay;

/// <summary>Retains the recipe behind an actionable instruction, not every ranked support candidate.</summary>
internal sealed class IntermediateCraftCommitment(DataCatalog catalog)
{
    private string? _goalId;
    private long _generation = -1;
    private PlayMode _mode;
    public string? TargetUnitId { get; private set; }

    public string? TargetFor(string goalId, long generation, PlayMode mode,
        IReadOnlyList<InventoryEntry> inventory)
    {
        if (_goalId != goalId || _generation != generation ||
            _mode != mode && !(PlayModes.AutomaticGoals(_mode) && PlayModes.AutomaticGoals(mode)))
        {
            Reset();
            _goalId = goalId;
            _generation = generation;
            _mode = mode;
        }
        if (inventory.Any(entry => entry.Count > 0 && entry.UnitId == TargetUnitId))
            TargetUnitId = null;
        return TargetUnitId;
    }

    public void Record(CoachFrame frame, CoachDecision decision)
    {
        if (!frame.IsCurrent || frame.Paused || frame.GoalId is null || frame.Mode == PlayMode.Guide)
            return;
        TargetFor(frame.GoalId, frame.MatchGeneration, frame.Mode,
            frame.Inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray());
        if (TargetUnitId is not null || decision.CraftRecipe is null ||
            decision.Kind is not (CoachActionKind.Craft or CoachActionKind.Economy) ||
            frame.Recommendations.FirstOrDefault() is not { } lead ||
            TopGradePolicy.IsTopGrade(catalog.Unit(lead.Route.GoalUnitId).Tier) ||
            frame.Inventory.GetValueOrDefault(lead.Route.GoalUnitId) > 0)
            return;
        // Commit when execution is offered: waiting for the next scan would let ranking
        // replace the instruction while the user is already carrying it out.
        TargetUnitId = lead.Route.GoalUnitId;
    }

    public void Reset() => TargetUnitId = null;
}
