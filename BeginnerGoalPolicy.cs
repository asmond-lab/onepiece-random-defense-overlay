namespace OrandOverlay;

public sealed class BeginnerGoalPolicy
{
    private readonly DataCatalog _catalog;
    private ClearBuildStats? _sourceStats;
    private readonly RecipeCompletionCalculator _recipes;
    private UnitDefinition? _committed;
    private bool _ownedCommitment;

    public BeginnerGoalPolicy(DataCatalog catalog, ClearBuildStats stats, string difficulty = "unknown")
    {
        _catalog = catalog;
        _recipes = new RecipeCompletionCalculator(catalog.Unit);
        UpdateContext(stats, difficulty);
    }

    public void UpdateContext(ClearBuildStats stats, string difficulty)
    {
        difficulty = MatchOutcomeDetector.IsKnownDifficulty(difficulty) ? difficulty : "unknown";
        if (ReferenceEquals(_sourceStats, stats) && Difficulty == difficulty) return;
        if (Difficulty != difficulty && !_ownedCommitment) Reset();
        _sourceStats = stats;
        Difficulty = difficulty;
        Statistics = difficulty == "unknown" ? ClearBuildStats.Empty : stats.ForDifficulty(difficulty);
        EligibleGoals = _catalog.AllUnits.Where(unit => TopGradePolicy.IsTopGrade(unit.Tier) &&
                unit.Recipe.Count > 0 && GoalStrategyCalculator.StrategyProfileFor(unit) is
                    { StunTarget: > 0, SlowTarget: > 0 })
            .Where(unit => BuildVariants.For(unit).Count == 0)
            .Where(unit => Statistics.GoalProfile(unit.Rawcodes, TopScope.SoloTop) is
                { Scope: TopScope.SoloTop, SampleCount: >= ClearBuildStats.MinimumGoalSamples })
            .DistinctBy(unit => unit.Id).OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray();
    }

    public string Difficulty { get; private set; } = "unknown";
    public ClearBuildStats Statistics { get; private set; } = ClearBuildStats.Empty;
    public IReadOnlyList<UnitDefinition> EligibleGoals { get; private set; } = [];
    public string? CommittedGoalId => _committed?.Id;
    public IReadOnlyList<string> RouteGoalIds => _committed is { } goal
        ? [goal.Id] : EligibleGoals.Select(unit => unit.Id).ToArray();
    public int Samples(UnitDefinition unit) =>
        Statistics.GoalProfile(unit.Rawcodes, TopScope.SoloTop)?.SampleCount ?? 0;

    public UnitDefinition? Select(IReadOnlyList<InventoryEntry> inventory)
    {
        if (_ownedCommitment) return _committed;
        var counts = inventory.Where(entry => entry.Count > 0)
            .GroupBy(entry => _catalog.Unit(entry.UnitId).Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        // Existing tops are facts, not new build recommendations. Do not replace one
        // merely because the player changed explanation/planning mode.
        var owned = counts.Keys.Select(_catalog.Unit).Where(unit => TopGradePolicy.IsTopGrade(unit.Tier))
            .OrderByDescending(unit => unit.Id == _committed?.Id)
            .ThenBy(unit => unit.Id, StringComparer.Ordinal).FirstOrDefault();
        if (owned is not null)
        {
            _committed = owned;
            _ownedCommitment = true;
        }
        if (_committed is not null) return _committed;
        if (!counts.Keys.Any(id => TopGradePolicy.BaseTier(_catalog.Unit(id).Tier) is
                "희귀함" or "전설" or "초월" or "불멸" or "영원" or "제한됨")) return null;
        _committed = EligibleGoals.Select(unit => (Unit: unit,
                Progress: _recipes.Calculate([unit.Id], counts)))
            .OrderByDescending(item => counts.GetValueOrDefault(item.Unit.Id) > 0)
            .ThenByDescending(item => item.Progress.CompletionRatio)
            .ThenBy(item => item.Progress.MissingLeaves.Sum(leaf => leaf.MissingCount))
            .ThenByDescending(item => Samples(item.Unit))
            .ThenBy(item => item.Unit.Id, StringComparer.Ordinal)
            .Select(item => item.Unit).FirstOrDefault();
        return _committed;
    }

    public void Reset()
    {
        _committed = null;
        _ownedCommitment = false;
    }
}
