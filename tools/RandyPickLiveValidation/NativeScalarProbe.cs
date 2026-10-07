using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrandOverlay;

// Tool-only evidence acquisition. Never sets layout proof or changes the production result/cache.
internal static class NativeScalarProbe
{
    internal enum IdentifierMismatchKind { UnknownSourceIdentifier, DuplicateSourceIdentifier }
    internal sealed record SourceIdentifierMismatch(
        [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        IdentifierMismatchKind Kind, string NormalizedNameSha256, int IdentifierLength,
        uint RuntimeTypeTag, uint DeclaredTypeTag, int EarlierExpectedMatches, bool BracketVerified = false)
    {
        public string VerificationScope => BracketVerified
            ? "Name/metadata prefix and closing context brackets only; not source acceptance, freshness or layout proof"
            : "Unverified mismatch observation; name/metadata/context/deadline brackets not completed";
    }
    // NFC UTF-8, uppercase hexadecimal SHA-256. No trimming/case-folding or lookup normalization.
    internal static string NormalizedIdentifierSha256(string name) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.Normalize(NormalizationForm.FormC))));
    internal static bool IdentifierHashMatches(string knownReferenceName, string capturedHash) =>
        capturedHash is { Length: 64 } && capturedHash.All(Uri.IsHexDigit) &&
        string.Equals(NormalizedIdentifierSha256(knownReferenceName), capturedHash, StringComparison.OrdinalIgnoreCase);
    internal static IdentifierMismatchKind? ClassifyIdentifier(bool inExpectedSource, bool alreadyMatched) =>
        alreadyMatched ? IdentifierMismatchKind.DuplicateSourceIdentifier :
        !inExpectedSource ? IdentifierMismatchKind.UnknownSourceIdentifier : null;
    internal static SourceIdentifierMismatch DescribeMismatch(IdentifierMismatchKind kind, string name, byte[] metadata, int earlierMatches)
    {
        Need(metadata.Length == 32 && earlierMatches >= 0 && earlierMatches < 3936, "Mismatch descriptor inputs rejected");
        return new(kind, NormalizedIdentifierSha256(name), name.Length,
            BitConverter.ToUInt32(metadata, 24), BitConverter.ToUInt32(metadata, 28), earlierMatches);
    }
    internal static SourceIdentifierMismatch VerifyMismatch(SourceIdentifierMismatch initial, Action completeBrackets)
    {
        completeBrackets();
        return initial with { BracketVerified = true };
    }
    internal enum Outcome { IndependentAgreement, LimitedPreRound, BlockedComparison, FailedRead }
    internal sealed record Result(Outcome Outcome, string Stage, string? FailureReason,
        object? Evidence = null, DateTimeOffset? ProbeStartedAt = null, DateTimeOffset? ProbeCompletedAt = null,
        double ProbeElapsedMilliseconds = 0, long PayloadRequestedReadBytes = 0, int PayloadReadCalls = 0,
        BoundReadSession.BudgetSnapshot? SharedBudget = null, SourceIdentifierMismatch? Mismatch = null)
    {
        public string Status => Outcome == NativeScalarProbe.Outcome.FailedRead ? "Blocked" : Outcome.ToString();
        public bool ReadCompleted => Outcome != NativeScalarProbe.Outcome.FailedRead;
        public bool ExperimentalLayoutVerified => false;
        public bool GameplayReady => false;
        public bool CanCoach => false;
        public string FreshnessAuthority => "Outer ScanStartedAt/ScanAgeMilliseconds only; probe clock cannot renew freshness";
    }
    internal static Result Failed(Exception error, string stage, BoundReadSession.BudgetSnapshot? budget = null) =>
        new(Outcome.FailedRead, stage, error is Blocked ? error.Message : error.GetType().Name, SharedBudget: budget);
    // Shared by real core and synthetic ordering/failure tests. No callback runs before both rechecks.
    internal static void RecheckAndClose(Action journal, Action cache, Action close, Action deadline, CancellationToken token)
    {
        journal(); cache(); close(); token.ThrowIfCancellationRequested(); deadline();
    }
    private const string SourceHash = "6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C";
    private const int MaximumBytes = 2 * 1024 * 1024;
    private sealed class Blocked(string reason) : Exception(reason) { }
    private static void Need(bool ok, string reason) { if (!ok) throw new Blocked(reason); }
    private static T Property<T>(object obj, string name) =>
        (T)(obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(obj) ?? throw new Blocked("Cache schema unavailable"));
    private static object Cache(WarcraftMemoryRecognitionService reader)
    {
        var growth = typeof(WarcraftMemoryRecognitionService).GetField("_diagnosticGrowthReader", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(reader) ?? throw new Blocked("Growth cache unavailable");
        return growth.GetType().GetField("cached", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(growth)
            ?? throw new Blocked("Validated growth cache absent");
    }

    private static object Cache(Warcraft300GrowthReader reader) =>
        typeof(Warcraft300GrowthReader).GetField("cached", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(reader)
        ?? throw new Blocked("Completed standalone growth cache absent");

    // Legacy ownership fence stays intact; no standalone caller can manufacture Ready.
    internal static object Run(WarcraftMemoryRecognitionService reader, RecognitionResult main,
        ExpectedReadTarget target, Action identityGuard, CancellationToken token = default)
    {
        try
        {
            Need(main.State == RecognitionState.Ready, "Main result not Ready");
            identityGuard();
            using var process = Process.GetProcessById(target.ProcessId);
            var liveModule = process.MainModule ?? throw new Blocked("Module unavailable");
            target.EnsureMatches(process.Id, process.StartTime.ToUniversalTime().Ticks);
            Need(!process.HasExited && liveModule.FileVersionInfo.FileVersion == Warcraft300Diagnostic.Version &&
                liveModule.ModuleName == "Warcraft III.exe" && (ulong)liveModule.BaseAddress.ToInt64() == Property<ulong>(Property<object>(Cache(reader), "Context"), "Module"), "Current module pin mismatch");
            using var memory = ReadOnlyProcessMemory.Open(target.ProcessId);
            byte[] Guarded(ulong a, int n)
            {
                ulong cursor = a;
                foreach (var r in memory.ReadableModuleRegions(a, n, token)) { Need(r.BaseAddress == cursor, "Not already readable"); cursor = checked(cursor + r.Size); }
                Need(cursor == checked(a + (ulong)n), "Not already readable");
                return memory.ReadAvailable(a, n);
            }
            void Identity() { identityGuard(); Need(!process.HasExited, "Target exited"); target.EnsureMatches(process.Id, process.StartTime.ToUniversalTime().Ticks); }
            return RunCore(() => Cache(reader), Guarded, Identity, Identity, () => { }, () => null, token);
        }
        catch (Exception e) { return Failed(e, "legacy-ownership"); }
    }
    internal static Result Run(Warcraft300GrowthReader reader, Warcraft300GrowthObservation completed,
        BoundReadSession binding, Action closingCheck, CancellationToken token, Warcraft300DeclaredScope? declaredScope = null)
    {
        try
        {
            var cache = Cache(reader); var c = Property<object>(cache, "Context");
            Need(Property<ulong>(cache, "Aggregate") == completed.OwnerAggregate && Property<ulong>(cache, "Table") == completed.DataTable &&
                Property<ulong>(cache, "Qr") == completed.QrNode && Property<string>(c, "Session") == binding.SessionKey &&
                completed.SessionKey == binding.SessionKey && Property<ulong>(c, "Module") == binding.Module &&
                Property<Warcraft300Diagnostic.View>(c, "View") == completed.CurrentView && Property<ulong>(c, "World") == completed.World,
                "Standalone completed-discovery ownership mismatch");
            var opening = completed.GlobalsSnapshot;
            if (declaredScope is not null || opening is not null)
            {
                Need(declaredScope is not null && opening is not null && ReferenceEquals(reader.CompletedSnapshot, opening) && ReferenceEquals(opening.Scope, declaredScope) &&
                    opening.Invocation != Guid.Empty && opening.Scope.Fingerprint == PreludeDeclarationCatalog.UnionFingerprint &&
                    opening.Session == binding.SessionKey && opening.Module == binding.Module && opening.World == completed.World && opening.View == completed.CurrentView &&
                    Equals(opening.Context, c) && opening.Owner == completed.OwnerAggregate && opening.Table == completed.DataTable && opening.Qr == completed.QrNode &&
                    Stopwatch.GetElapsedTime(opening.StartedTimestamp) < TimeSpan.FromSeconds(3), "Stale/foreign scan snapshot");
            }
            return RunCore(() => Cache(reader), binding.Read, binding.Revalidate, closingCheck, binding.CheckProbeDeadline, () => binding.SharedProbeBudget, token,
                opening, () => ReferenceEquals(reader.CompletedSnapshot, opening));
        }
        catch (Exception e) { return Failed(e, "standalone-ownership", binding.SharedProbeBudget); }
    }
    private static Result RunCore(Func<object> currentCache, Func<ulong, int, byte[]> nativeRead,
        Action identityGuard, Action closingCheck, Action sharedDeadline, Func<BoundReadSession.BudgetSnapshot?> sharedBudget, CancellationToken token,
        Warcraft300GlobalsSnapshot? opening = null, Func<bool>? sameInvocation = null)
    {
        var watch = Stopwatch.StartNew(); var started = DateTimeOffset.UtcNow;
        long bytes = 0; int calls = 0; string stage = "preflight";
        SourceIdentifierMismatch? mismatch = null;
        try
        {
            void Check() { token.ThrowIfCancellationRequested(); Need(watch.Elapsed < TimeSpan.FromSeconds(5), "Five-second probe budget"); sharedDeadline(); }
            Check(); var cache = currentCache(); var context = Property<object>(cache, "Context");
            var module = Property<ulong>(context, "Module");
            var expectedView = Property<Warcraft300Diagnostic.View>(context, "View");
            var world = Property<ulong>(context, "World"); var ui = Property<ulong>(context, "Ui");
            var instance = Property<ulong>(context, "Instance"); var script = Property<ulong>(context, "Script");
            var manager = Property<ulong>(context, "Manager"); var registry = Property<ulong>(context, "Registry");
            var aggregate = Property<ulong>(cache, "Aggregate"); var table = Property<ulong>(cache, "Table");
            var qr = Property<ulong>(cache, "Qr");
            var ownerHeader = opening?.OwnerHeaderCopy() ?? (byte[])Property<byte[]>(cache, "OwnerHeader").Clone();
            var tableHeader = opening?.TableHeaderCopy() ?? (byte[])Property<byte[]>(cache, "TableHeader").Clone();
            var expected = Map2320GrowthSource.LoadBundled().Globals;
            Need(expected.Count == 3936 && expected["pb"] == 4 && expected["Eb"] == 4 && expected["qg"] == 7, "Source declarations missing");
            var fingerprint = string.Join(";", expected.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => p.Key + ":" + p.Value.ToString(CultureInfo.InvariantCulture)));
            Need(fingerprint == Property<string>(context, "ExpectedGlobalsFingerprint"), "Cache/source fingerprint mismatch");
            identityGuard(); Check();
            var journal = new List<(ulong Address, byte[] Data)>(); bool record = true; int contextReads = 0;
            byte[] R(ulong a, int n)
            {
                Check(); Need(n > 0 && a >= 0x10000 && a <= 0x7FFFFFFFFFFF && (ulong)(n - 1) <= 0x7FFFFFFFFFFF - a, "Read range rejected");
                Need(n <= MaximumBytes - bytes && calls < 100000, "Probe read budget"); bytes += n; calls++;
                var data = nativeRead(a, n); Check(); Need(data.Length == n, "Incomplete guarded read");
                if (record) journal.Add((a, (byte[])data.Clone()));
                return data;
            }
            ulong Q(ulong a) => BitConverter.ToUInt64(R(a, 8));
            uint D(ulong a) => BitConverter.ToUInt32(R(a, 4));
            void Typed(ulong a, ulong rva) => Need(Q(a) == module + rva, "Pinned vtable mismatch");
            void Same(ulong a, byte[] b) => Need(b.AsSpan().SequenceEqual(R(a, b.Length)), "Changed metadata/context/payload");
            string Text(ulong a, int maximum)
            {
                var data = new List<byte>();
                for (var i = 0; i < maximum; i++)
                { var b = R(checked(a + (ulong)i), 1)[0]; if (b == 0) return new UTF8Encoding(false, true).GetString(data.ToArray()); data.Add(b); }
                throw new Blocked("Unterminated UTF8 text");
            }
            void CloseObservedInputs()
            {
                stage = "complete-before-after-revalidation";
                RecheckAndClose(() =>
                {
                    record = false;
                    // Replay only bytes already observed, including the current node's metadata/name and terminator.
                    foreach (var read in journal) Same(read.Address, read.Data);
                    if (opening is not null)
                    {
                        opening.RecheckAll(R, token); // One exhaustive scope replay. Exact original names+NUL, no new pointers/baseline.
                        for (int i = 0; i < contextReads; i++) Same(journal[i].Address, journal[i].Data);
                    }
                }, () =>
                {
                    stage = "closing-cache";
                    Need(opening is null || sameInvocation?.Invoke() == true, "Snapshot invocation replaced");
                    Need(ReferenceEquals(currentCache(), cache) && Equals(Property<object>(currentCache(), "Context"), context), "Cached context replaced");
                }, () =>
                {
                    stage = "closing-world-and-binding";
                    closingCheck(); // Same one closing world/full-binding callback and original shared deadline.
                }, Check, token);
            }
            stage = "cached-context-revalidation";
            Need(Warcraft300Diagnostic.ReadView(R, module) == expectedView && expectedView.Slot <= 3, "Current view mismatch");
            Need(Q(module + 0x2F5EF00) == ui && Q(module + 0x2F85360) == ui, "Dual UI mismatch");
            Typed(ui, 0x275ED08); Typed(world, 0x2764A20); Need(Q(world + 0x40) == ui, "World UI mismatch");
            Need(Q(expectedView.Root + 0x25D0) == instance && Q(expectedView.Root + 0x25E0) == script &&
                Q(expectedView.Root + 0x2620) == manager && Q(module + 0x2F807F0) == registry, "VM/registry context mismatch");
            Same(instance, Convert.FromHexString(Property<string>(context, "InstanceHeader")));
            Same(script, Convert.FromHexString(Property<string>(context, "ScriptHeader")));
            Typed(instance, 0x27ECBC0); Typed(script, 0x27ECC40);
            Same(aggregate, ownerHeader); Same(table, tableHeader);
            Need(ownerHeader.Length == 24 && tableHeader.Length == 72 && BitConverter.ToUInt64(ownerHeader) == instance &&
                BitConverter.ToUInt64(ownerHeader, 8) == module + 0x13F3070 && BitConverter.ToUInt64(ownerHeader, 16) == table &&
                BitConverter.ToUInt64(tableHeader) == module + 0x2809B78 && BitConverter.ToUInt32(tableHeader, 8) == 24, "Owner/table layout mismatch");
            contextReads = journal.Count;
            stage = "complete-source-globals";
            var nodes = new Dictionary<string, ulong>(StringComparer.Ordinal); var seen = new HashSet<ulong>();
            if (opening is not null)
            {
                Need(opening.Nodes.Count == 6196 && opening.Scope.Declarations.Count == 6196, "Incomplete declared scope snapshot");
                foreach (var node in opening.Nodes) Need(nodes.TryAdd(node.Name, node.Address), "Duplicate snapshot name");
                foreach (var name in new[] { "pb", "Eb", "qg", "QR" })
                {
                    var node = opening.Nodes.Single(n => n.Name == name);
                    opening.Scope.ValidateConsumed(name, node.RuntimeTag, node.DeclaredTag);
                    Need(node.DeclaredTag == expected[name], "Selected declared type mismatch");
                    node.Recheck(R); // Before any selected payload, using opening identifying bytes.
                }
            }
            else
            {
            var current = BitConverter.ToUInt64(tableHeader, 24); var previous = table + 16;
            while (current != table + 17)
            {
                Check(); Need(nodes.Count < 3936 && (current & 7) == 0 && seen.Add(current), "Globals count/cycle/alignment");
                var meta = R(checked(current + 24), 32);
                Need(BitConverter.ToUInt64(meta) == previous, "Globals backlink mismatch");
                var name = Text(BitConverter.ToUInt64(meta, 16), 256);
                bool inExpected = expected.TryGetValue(name, out var tag);
                var mismatchKind = ClassifyIdentifier(inExpected, nodes.ContainsKey(name));
                if (mismatchKind is { } kind)
                {
                    mismatch = DescribeMismatch(kind, name, meta, nodes.Count); // Immutable, unverified, no raw identifier retained.
                    var verifiedMismatch = VerifyMismatch(mismatch, CloseObservedInputs);
                    stage = "complete-source-globals";
                    Check();
                    var rejected = new Result(Outcome.FailedRead, stage, kind.ToString(),
                        ProbeStartedAt: started, ProbeCompletedAt: DateTimeOffset.UtcNow, ProbeElapsedMilliseconds: watch.Elapsed.TotalMilliseconds,
                        PayloadRequestedReadBytes: bytes, PayloadReadCalls: calls, SharedBudget: sharedBudget(), Mismatch: verifiedMismatch);
                    Check(); return rejected; // No next-node scan, scalar payload read, retry or second full pipeline.
                }
                Need(nodes.TryAdd(name, current), "DuplicateSourceIdentifier");
                Need(BitConverter.ToUInt32(meta, 24) == tag && BitConverter.ToUInt32(meta, 28) == tag, "Source global type mismatch");
                previous = current + 24; current = BitConverter.ToUInt64(meta, 8);
            }
            Need(nodes.Count == 3936 && previous == BitConverter.ToUInt64(tableHeader, 16) && nodes.GetValueOrDefault("QR") == qr,
                "Incomplete globals tail/source/QR");
            }
            stage = "scalar-and-qg-payload-hypotheses";
            var pbNode = R(nodes["pb"], 64); var ebNode = R(nodes["Eb"], 64); var qgNode = R(nodes["qg"], 64);
            var pb = BitConverter.ToInt32(pbNode, 56); var eb = BitConverter.ToInt32(ebNode, 56);
            var jassHandle = BitConverter.ToUInt32(qgNode, 56);
            Need(jassHandle > 0x100000, "qg JASS handle unavailable");
            var jassLimit = D(manager + 0x290); var jassTable = Q(manager + 0x298); var jassIndex = jassHandle - 0x100000;
            Need(jassLimit > 0 && jassLimit <= 1048576 && jassIndex < jassLimit, "qg JASS handle bounds");
            var entry = R(checked(jassTable + 24UL * jassIndex), 24);
            Need(BitConverter.ToUInt32(entry) > 0, "qg JASS reference absent");
            var dialog = BitConverter.ToUInt64(entry, 8); Typed(dialog, 0x2730B08);
            uint Resolve(ulong handle, ulong obj, ulong vtable)
            {
                uint index = (uint)handle & 0x7FFFFFFF; bool alternate = ((uint)handle & 0x80000000) != 0;
                var count = D(registry + (alternate ? 0x68UL : 0x30UL));
                var slots = Q(registry + (alternate ? 0x50UL : 0x18UL));
                Need(count > 0 && count <= 262144 && index < count, "Native allocation bounds");
                var slot = R(checked(slots + index * 16UL), 16);
                Need(BitConverter.ToUInt32(slot) == 0xFFFFFFFE, "Native allocation marker");
                var recordAddress = BitConverter.ToUInt64(slot, 8);
                var typeId = D(checked(recordAddress + 0x18)); // Discover, never assume CUnit's ID.
                Need(D(checked(recordAddress + 0x24)) == (uint)(handle >> 32) && Q(checked(recordAddress + 0x30)) == 0 &&
                    Q(checked(recordAddress + 0x90)) == obj && Q(checked(obj + 0x18)) == handle, "Native serial/state/backlink mismatch");
                Typed(obj, vtable); return typeId;
            }
            stage = "qg-owned-native-title";
            var dialogTypeId = Resolve(Q(dialog + 0x18), dialog, 0x2730B08);
            var dialogUi = Q(dialog + 0x58); Typed(dialogUi, 0x2768190);
            Need(Q(dialogUi + 0x40) == ui && Text(Q(dialogUi + 0x280), 32) == "TimerDialog", "Dialog UI ownership/name");
            var timerHandle = Q(dialog + 0x60); var timerObject = Q(dialogUi + 0x2D8);
            var timerTypeId = Resolve(timerHandle, timerObject, 0x26E04E0);
            var titleFrame = Q(dialogUi + 0x298); Typed(titleFrame, 0x228F5D0);
            Need(Q(titleFrame + 0x40) == dialogUi && Text(Q(titleFrame + 0x280), 32) == "TimerDialogTitle", "Title frame ownership/name");
            var titleAddress = Q(titleFrame + 0x4C8);
            Need(Q(titleFrame + 0x4D0) == titleAddress, "Title aliases mismatch");
            var title = Text(titleAddress, 96); var titleNumber = WarcraftCurrentRoundReader.ParseTitle(title);
            stage = "complete-before-after-revalidation";
            CloseObservedInputs();
            bool pair = pb >= 0 && pb <= 65 && eb == pb + 1;
            bool positive = pair && pb > 0 && titleNumber == pb;
            bool pre = pair && pb == 0 && title == "|cffFF0000 1라운드 시작까지|r";
            var evidence = new {
                DeclaredSchemaAgreement = opening is not null,
                UnknownRuntimeValues = opening?.Nodes.Where(n => n.RuntimeTag != n.DeclaredTag).Select(n => new {
                    NormalizedNameSha256 = NormalizedIdentifierSha256(n.Name), n.RuntimeTag, n.DeclaredTag,
                    RuntimeValueStatus = "UnknownRuntimeValue; not consumed by probe" }).ToArray(),
                GlobalsCount = nodes.Count, RawScalarPb = pb, RawScalarEb = eb,
                RawPbPayloadHex = Convert.ToHexString(pbNode.AsSpan(56, 4)), RawEbPayloadHex = Convert.ToHexString(ebNode.AsSpan(56, 4)),
                IndependentTitleValue = titleNumber, PreRoundTitleRecognized = pre,
                SourcePairRelation = pair, NativeOwnedTitleMatchesScalar = positive,
                DiscoveredDialogRecordTypeId = $"0x{dialogTypeId:X8}", DiscoveredTimerRecordTypeId = $"0x{timerTypeId:X8}",
                DialogVtableRva = "0x2730B08", TimerVtableRva = "0x26E04E0",
                TypeIdsAreObservedNotPreviouslyPinned = true, ExperimentalLayoutVerified = false,
                ProofStatus = positive ? "Independent single-value sample; no immutable-sentinel or full-round approval" : "Limited or mismatched sample; no scalar layout approval",
                GameplayReady = false, CanCoach = false, MainResultUnmodified = true, NoRuntimeMapOrLifeClaim = true
            };
            Check();
            var result = new Result(positive ? Outcome.IndependentAgreement : pre ? Outcome.LimitedPreRound : Outcome.BlockedComparison,
                "complete", positive || pre ? null : "Stable scalar/title comparison did not agree", evidence, started, DateTimeOffset.UtcNow,
                watch.Elapsed.TotalMilliseconds, bytes, calls, sharedBudget());
            Check(); return result; // Final original lease-clock check after all closing work and report construction.
        }
        catch (Exception error)
        {
            return new Result(Outcome.FailedRead, stage, error is Blocked ? error.Message : error.GetType().Name,
                ProbeStartedAt: started, ProbeCompletedAt: DateTimeOffset.UtcNow, ProbeElapsedMilliseconds: watch.Elapsed.TotalMilliseconds,
                PayloadRequestedReadBytes: bytes, PayloadReadCalls: calls, SharedBudget: sharedBudget(), Mismatch: mismatch);
        }
    }
}
