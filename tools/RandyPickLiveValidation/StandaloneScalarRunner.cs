using System.Diagnostics;
using System.IO;
using OrandOverlay;

internal static class StandaloneScalarRunner
{
    internal const int DiscoveryBlockBytes = Warcraft300GrowthReader.LargeDiscoveryBlockBytes;
    internal enum FailureKind { None, DiscoveryTimeCap, DiscoveryByteCap, MetadataByteCap, Cancelled, ProtocolRejected }
    internal static FailureKind ClassifyFailure(Exception error) => error switch
    {
        OperationCanceledException => FailureKind.Cancelled,
        InvalidDataException => error.Message switch
        {
            "Incomplete discovery: time cap." => FailureKind.DiscoveryTimeCap,
            "Incomplete discovery: byte cap." => FailureKind.DiscoveryByteCap,
            "Incomplete discovery: region exceeds remaining byte cap." => FailureKind.DiscoveryByteCap,
            "Diagnostic metadata budget exceeded." => FailureKind.MetadataByteCap,
            _ => FailureKind.ProtocolRejected
        },
        _ => FailureKind.ProtocolRejected
    };
    // A finally capture preserves failure/expiry metrics using only frozen numeric counters.
    internal static T CaptureDiscovery<T>(Action begin, Func<T> discover,
        Func<BoundReadSession.DiscoveryMetricsSnapshot?> end, Action<BoundReadSession.DiscoveryMetricsSnapshot?> capture)
    {
        begin();
        try { return discover(); }
        finally { capture(end()); }
    }
    internal sealed record Report(string Status, bool DiscoveryComplete, bool Fresh, bool HistoricalOnly,
        DateTimeOffset ScanStartedAt, DateTimeOffset CompletedAt, double ScanAgeMilliseconds, NativeScalarProbe.Result? Evidence,
        string? Failure, string? FailureStage, BoundReadSession.BudgetSnapshot? SharedBudget,
        [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        FailureKind FailureCode = FailureKind.None, BoundReadSession.DiscoveryMetricsSnapshot? DiscoveryMetrics = null,
        Warcraft300SourceScopeAudit? SourceScopeAudit = null, Warcraft300ObservedRoundReader.Observation? ReferenceRound = null)
    {
        public bool ExperimentalLayoutVerified => false;
        public bool GameplayReady => false;
        public bool CanCoach => false;
        public bool Verified => false;
        public bool RuntimeWholeImageEquivalence => false;
        public bool ProbeReadCompleted => Evidence?.ReadCompleted == true;
        public int? DiagnosticCurrentValue => null;
        public string CopySha256 => Warcraft300Diagnostic.Hash;
        public string FreshnessAuthority => "Original ScanStartedAt and monotonic ScanAgeMilliseconds, not the probe clock";
        public string BindingScope => "Pinned copy SHA; complete normalized MAIN headers and raw resource section equality, not runtime whole-image equality";
    }
    internal static Warcraft300SourceScopeAudit AcceptSourceScopeAudit(Warcraft300SourceScopeAudit? audit)
    {
        if (audit is null || audit.TotalCount is < 3936 or > 20000 || audit.ExpectedSourceDeclarationCount != 3936 ||
            audit.MatchedExpectedMapDeclarationCount != 3936 || audit.Entries.Count(e => e.IsExpectedMapDeclaration) != 3936)
            throw new InvalidDataException("Source-scope audit must contain all 3936 expected map declarations.");
        return audit;
    }
    internal static bool IsFresh(TimeSpan age) => age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(3);
    internal static NativeScalarProbe.Result AfterCompleteDiscovery<T>(Func<T> discover, Func<T, NativeScalarProbe.Result> probe) => probe(discover());
    internal static Report Finish(DateTimeOffset started, DateTimeOffset completed, TimeSpan elapsed,
        bool discoveryComplete, NativeScalarProbe.Result? evidence, string? failure, string? failureStage = null,
        BoundReadSession.BudgetSnapshot? sharedBudget = null, BoundReadSession.DiscoveryMetricsSnapshot? discoveryMetrics = null,
        FailureKind failureCode = FailureKind.None, Warcraft300SourceScopeAudit? sourceScopeAudit = null,
        Warcraft300ObservedRoundReader.Observation? referenceRound = null)
    {
        bool failed = failure is not null || evidence is null || evidence.Outcome == NativeScalarProbe.Outcome.FailedRead;
        var reason = failure ?? evidence?.FailureReason ?? (evidence is null ? "No completed probe outcome" : null);
        var status = failed ? "Blocked" : evidence!.Outcome == NativeScalarProbe.Outcome.BlockedComparison ? "BlockedComparison" :
            !IsFresh(elapsed) ? "Expired" : "UnknownLayout";
        var publishedAudit = discoveryComplete && sourceScopeAudit is not null ? AcceptSourceScopeAudit(sourceScopeAudit) : null;
        return new(status, discoveryComplete, IsFresh(elapsed), !IsFresh(elapsed), started, completed, elapsed.TotalMilliseconds,
            evidence, reason, failureStage ?? (reason is not null ? evidence?.Stage : null), sharedBudget ?? evidence?.SharedBudget,
            failed && failureCode == FailureKind.None ? FailureKind.ProtocolRejected : failureCode, discoveryMetrics,
            publishedAudit, referenceRound);
    }
    // Called INSIDE the core after journal/cache checks. Exactly one final full binding in this callback.
    internal static void CloseEvidence(Action world, Action binding, Action originalProbeDeadline, CancellationToken token)
    {
        world(); binding(); token.ThrowIfCancellationRequested(); originalProbeDeadline();
    }
    internal static Report Read(BoundReadSession binding, CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow; var watch = Stopwatch.StartNew(); bool complete = false;
        NativeScalarProbe.Result? evidence = null; var stage = "discovery-preamble";
        BoundReadSession.DiscoveryMetricsSnapshot? discoveryMetrics = null;
        Warcraft300SourceScopeAudit? pendingScopeAudit = null, sourceScopeAudit = null;
        Warcraft300ObservedRoundReader.Observation? referenceRound = null;
        try
        {
            binding.Revalidate();
            var source = Map2320GrowthSource.LoadBundled();
            var declaredScope = PreludeDeclarationCatalog.Load(source.Globals); // Pin exact policy BEFORE this Growth call.
            var worldBefore = Warcraft300WorldLocator.Read(binding.Read, binding.Module, token);
            var view = Warcraft300Diagnostic.ReadView(binding.Read, binding.Module);
            var growthReader = new Warcraft300GrowthReader();
            stage = "complete-growth-discovery";
            evidence = AfterCompleteDiscovery(() => CaptureDiscovery(binding.BeginDiscoveryMetrics,
                () => growthReader.Read(binding.Read, binding.Regions, binding.Module,
                    view, worldBefore.World, binding.SessionKey, source.Globals, token,
                    discoveryBlockBytes: DiscoveryBlockBytes, sourceScopeObserver: audit => pendingScopeAudit = audit, declaredScope: declaredScope),
                binding.CompleteDiscoveryMetrics, snapshot => discoveryMetrics = snapshot), completed =>
            {
                complete = true;
                // Publish only after Growth.Read returned successfully, never directly from the observer.
                sourceScopeAudit = AcceptSourceScopeAudit(pendingScopeAudit);
                binding.Revalidate();
                if (Warcraft300WorldLocator.Read(binding.Read, binding.Module, token) != worldBefore)
                    throw new InvalidDataException("World locator changed after complete discovery");
                stage = "bounded-scalar-probe";
                binding.BeginProbeBudget(); // Original shared budget: never reset, enlarged, or exempted below.
                referenceRound = Warcraft300ObservedRoundReader.Read(binding.Read, completed, binding.Module,
                    boundedRead => new(binding.SessionKey, binding.Module,
                        Warcraft300Diagnostic.ReadView(boundedRead, binding.Module),
                        Warcraft300WorldLocator.Read(boundedRead, binding.Module, token).World), token);
                return NativeScalarProbe.Run(growthReader, completed, binding, () => CloseEvidence(() =>
                {
                    if (Warcraft300WorldLocator.Read(binding.Read, binding.Module, token) != worldBefore)
                        throw new InvalidDataException("World locator changed after scalar probe");
                }, binding.Revalidate, binding.CheckProbeDeadline, token), token, declaredScope);
                // No tail native reads here: the core already closed world + binding + deadline inside its outcome boundary.
            });
            return Finish(started, DateTimeOffset.UtcNow, watch.Elapsed, complete, evidence, null,
                sharedBudget: binding.SharedProbeBudget, discoveryMetrics: discoveryMetrics, sourceScopeAudit: sourceScopeAudit, referenceRound: referenceRound);
        }
        catch (Exception e)
        {
            return Finish(started, DateTimeOffset.UtcNow, watch.Elapsed, complete, evidence, e.GetType().Name, stage, binding.SharedProbeBudget,
                discoveryMetrics, ClassifyFailure(e), sourceScopeAudit, referenceRound);
        }
    }
}
