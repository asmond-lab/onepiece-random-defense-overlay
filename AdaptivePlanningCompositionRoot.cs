using System.Collections.Immutable;

namespace OrandOverlay;

public sealed class AdaptivePlanningCompositionRoot
{
    private readonly AdaptivePlanningCoordinatorInputFactory _inputFactory;
    private readonly AdaptivePlanningCoordinator _coordinator;

    public AdaptivePlanningCompositionRoot(string dataDirectory)
        : this(dataDirectory, new AdaptivePlanningCoordinator()) { }

    internal AdaptivePlanningCompositionRoot(string dataDirectory,
        AdaptivePlanningCoordinator coordinator)
    {
        _inputFactory = new AdaptivePlanningCoordinatorInputFactory(dataDirectory);
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public long MatchGeneration => _coordinator.MatchGeneration;
    public AdaptiveBuildState State => _coordinator.State;
    public ManualLatches ManualLatches => _coordinator.ManualLatches;
    public AdaptivePlanningApplied? LastApplied => _coordinator.LastApplied;

    public AdaptivePlanningRefreshInput CreateInput(AdaptivePlanningInputSource source)
    {
        var input = _inputFactory.Create(source);
        if (!source.AdditionalValues.Any(value => !value.IsKnown)) return input;
        return new AdaptivePlanningRefreshInput(input.CanonicalInput, input.BuildSnapshot,
            input.NavigationRequest with { InputState = NavigationScoringInputState.Unknown });
    }

    public AdaptivePlanningWork? TryBegin(AdaptivePlanningRefreshInput input) =>
        _coordinator.TryBegin(input);

    public static AdaptivePlanningComputed Evaluate(AdaptivePlanningWork work) =>
        AdaptivePlanningCoordinator.Evaluate(work);

    public void ScheduleApply(AdaptivePlanningComputed computed, Action<Action> schedule,
        Action<AdaptivePlanningApplied> mutate) =>
        _coordinator.ScheduleApply(computed, schedule, mutate);

    public void LatchManualGoalOverride() => _coordinator.LatchManualGoalOverride();
    public void LatchManualNavigationOverride() =>
        _coordinator.LatchManualNavigationOverride();
    public void NoteProgrammaticSelection() => _coordinator.NoteProgrammaticSelection();
    public void ConfirmReset(long matchGeneration) =>
        _coordinator.ConfirmReset(matchGeneration);
}

public readonly record struct AdaptivePlanningReadMetric
{
    public long Bytes { get; }
    public long Calls { get; }
    public TimeSpan Elapsed { get; }

    public AdaptivePlanningReadMetric(long bytes, long calls, TimeSpan elapsed)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (calls < 0) throw new ArgumentOutOfRangeException(nameof(calls));
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        Bytes = bytes;
        Calls = calls;
        Elapsed = elapsed;
    }
}

public sealed record AdaptivePlanningReadCounters(
    AdaptivePlanningReadMetric UnitTraversal,
    AdaptivePlanningReadMetric MapState,
    AdaptivePlanningReadMetric SideChannel)
{
    public static AdaptivePlanningReadCounters Empty { get; } = new(default, default, default);
}

public sealed record AdaptivePlanningPerformanceSample(
    AdaptivePlanningReadCounters Reads,
    TimeSpan RecognitionElapsed,
    TimeSpan SettleElapsed,
    TimeSpan ScoringElapsed,
    TimeSpan TotalElapsed)
{
    public static TimeSpan TotalBudget => TimeSpan.FromMilliseconds(350);
    public bool IsWithinBudget => TotalElapsed <= TotalBudget &&
                                  Reads.MapState.Bytes <= 4L * 1024 * 1024;
}

public sealed record AdaptivePlanningReplayFrame(
    string InputFingerprint,
    string ProfileHash,
    string DataHash,
    ImmutableArray<string> OptionIds,
    AdaptivePlanningApplied Applied,
    PlannerEvidenceView Presentation,
    AdaptivePlanningPerformanceSample Performance);

public sealed class AdaptivePlanningReplayHost
{
    private readonly AdaptivePlanningCompositionRoot _composition;

    public AdaptivePlanningReplayHost(string dataDirectory) =>
        _composition = new AdaptivePlanningCompositionRoot(dataDirectory);

    public AdaptiveBuildState State => _composition.State;
    public ManualLatches ManualLatches => _composition.ManualLatches;
    public AdaptivePlanningApplied? LastApplied => _composition.LastApplied;

    public AdaptivePlanningReplayFrame? Apply(AdaptivePlanningInputSource source) =>
        Apply(source, AdaptivePlanningReadCounters.Empty, TimeSpan.Zero);

    public AdaptivePlanningReplayFrame? Apply(AdaptivePlanningInputSource source,
        AdaptivePlanningReadCounters reads, TimeSpan recognitionElapsed)
    {
        AdaptivePlanningReplayFrame? frame = null;
        TryApply(source, action => action(), value => frame = value,
            reads, recognitionElapsed);
        return frame;
    }

    public bool TryApply(AdaptivePlanningInputSource source, Action<Action> schedule,
        Action<AdaptivePlanningReplayFrame> applied) => TryApply(source, schedule, applied,
        AdaptivePlanningReadCounters.Empty, TimeSpan.Zero);

    private bool TryApply(AdaptivePlanningInputSource source, Action<Action> schedule,
        Action<AdaptivePlanningReplayFrame> applied, AdaptivePlanningReadCounters reads,
        TimeSpan recognitionElapsed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(reads);
        if (recognitionElapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(recognitionElapsed));
        var input = _composition.CreateInput(source);
        var work = _composition.TryBegin(input);
        if (work is null) return false;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var computed = AdaptivePlanningCompositionRoot.Evaluate(work);
        var scoringElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        var settleElapsed = LatestBackgroundWorkCoordinator.DefaultSettleDelay;
        var performance = new AdaptivePlanningPerformanceSample(reads,
            recognitionElapsed, settleElapsed, scoringElapsed,
            recognitionElapsed + settleElapsed + scoringElapsed);
        _composition.ScheduleApply(computed, schedule, value => applied(new(
            input.Fingerprint, input.CanonicalInput.ProfileHash,
            input.CanonicalInput.DataHash,
            input.NavigationRequest.Options.Select(option => option.OptionId)
                .ToImmutableArray(), value,
            PlannerEvidenceProjector.Project(source.Round, value, false, null),
            performance)));
        return true;
    }

    public void LatchManualGoalOverride() => _composition.LatchManualGoalOverride();
    public void LatchManualNavigationOverride() =>
        _composition.LatchManualNavigationOverride();
    public void ConfirmReset(long matchGeneration) =>
        _composition.ConfirmReset(matchGeneration);
}
