using System.Collections.Immutable;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>Pure, consent-gated session fragments. Never reads paths, HTTP or coach prose.</summary>
public sealed class GameplaySessionRecorder(Func<bool> permission, GameplayTelemetryMetadata metadata,
    DataCatalog catalog, TimeProvider? timeProvider = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly List<GameplayTelemetryEvent> _events = [];
    private readonly Queue<GameplayTelemetryPacket> _packets = new();
    private GameplayTelemetryObservation? _last;
    private readonly List<GameplayTelemetryObservation> _receiptBaselines = [];
    private const int ReceiptHistoryLimit = 8;
    private ImmutableDictionary<string, int> _lastGood = ImmutableDictionary<string, int>.Empty;
    private string? _matchId;
    private string _packetId = Guid.NewGuid().ToString("N");
    private bool _capturePaused;
    private bool _hadCaptureGap;
    private long _pausedObservationCount;
    public bool CapturePaused
    {
        get { lock (_gate) return _capturePaused; }
        set { lock (_gate) _capturePaused = value; }
    }
    public long PausedObservationCount { get { lock (_gate) return _pausedObservationCount; } }
    private long _generation = -1, _revision = -1, _sequence, _started;
    private int _chunk;
    private bool _terminal;
    private string? _lastRecommendation;

    public void Observe(GameplayTelemetryObservation observation)
    {
        lock (_gate)
        {
            if (!permission()) { Forget(); return; }
            if (!observation.IsCurrent || observation.Round is < 1 or > 65 || observation.RecognitionRevision < 0) return;
            if (_generation > observation.MatchGeneration) return;
            if (_matchId is not null && observation.MatchGeneration != _generation) ResetCore();
            // Revisions cannot roll back within a generation. Reset() explicitly fences a new fragment.
            if (_matchId is not null && observation.RecognitionRevision <= _revision) return;
            if (_terminal) return;
            if (_capturePaused) { _pausedObservationCount++; _hadCaptureGap = _last is not null; return; }
            // An explicitly skipped native observation must not masquerade as a complete
            // learnable match. Keep the old fragment, then restart at the newly observed state.
            if (_hadCaptureGap) ResetCore();
            var current = Sanitize(observation);
            if (_last is { } previous && previous.Difficulty != current.Difficulty) ResetCore();
            if (_matchId is null)
            {
                _matchId = Guid.NewGuid().ToString("N");
                _packetId = Guid.NewGuid().ToString("N");
                _generation = current.MatchGeneration;
                _started = _time.GetTimestamp();
            }
            var before = _last;
            _revision = current.RecognitionRevision;
            _last = current;
            if (current.Inventory.Any(p => p.Value > 0)) _lastGood = current.Inventory;
            if (before is not null && SameObservation(before, current)) return;
            Add(NewEvent("observation", "native-observed") with
            {
                Inventory = current.Inventory, RewardWisps = current.RewardWisps,
                Resources = current.Resources, GambleFailures = current.GambleFailures, GambleCounters = current.GambleCounters
            });
            // Only inventory/wisp transitions consume correlation history. Passive gold or
            // round updates must not erase an output-first/consumption-first action baseline.
            if (_receiptBaselines.Count == 0 || !SameReceiptState(_receiptBaselines[^1], current))
            {
                for (var index = _receiptBaselines.Count - 1; index >= 0; index--)
                {
                    if (!MatchReceipt(_receiptBaselines[index], current)) continue;
                    _receiptBaselines.Clear(); // Already-attributed deltas cannot be reused.
                    break;
                }
                _receiptBaselines.Add(current);
                if (_receiptBaselines.Count > ReceiptHistoryLimit) _receiptBaselines.RemoveAt(0);
            }
        }
    }

    public void Recommend(CoachFrame frame, CoachDecision decision)
    {
        lock (_gate)
        {
            if (!permission()) { Forget(); return; }
            if (_capturePaused || _last is null || _terminal || !frame.IsCurrent || frame.Paused || !frame.GuideVisible ||
                frame.MatchGeneration != _generation || frame.RecognitionRevision != _revision || !Enum.IsDefined(decision.Kind)) return;
            var goals = Goals(frame.Mode == PlayMode.Guide && frame.GuideNumber == 1 ? [BulletGuidePolicy.GoalId] :
                frame.SelectedGoalIds.Length > 0 ? frame.SelectedGoalIds : frame.GoalId is { } goal ? [goal] : []);
            var selection = decision.SelectionBatch?.Items.Where(x => SafeCatalogId(x.UnitId) && x.RemainingCount >= 0)
                .GroupBy(x => x.UnitId).ToImmutableDictionary(g => g.Key, g => g.Max(x => x.RemainingCount));
            var e = NewEvent("recommendation", "recommendation") with
            {
                Mode = frame.Mode.ToString(), GuideNumber = frame.GuideNumber,
                Action = decision.Kind.ToString(), TargetUnitId = decision.TargetUnitId is { } target && SafeCatalogId(target) ? target : null,
                GoalUnitIds = goals, Selection = selection
            };
            var fingerprint = JsonSerializer.Serialize(new { e.Action, e.TargetUnitId, e.GoalUnitIds, e.Selection, e.Round, e.CompletedStory, e.Mode, e.GuideNumber });
            if (fingerprint == _lastRecommendation) return;
            _lastRecommendation = fingerprint;
            Add(e);
        }
    }

    public void Complete(string outcome, string outcomeSource)
    {
        lock (_gate)
        {
            if (!permission()) { Forget(); return; }
            if (_hadCaptureGap) CompleteCore("interrupted", "unknown");
            else CompleteCore(outcome, outcomeSource);
        }
    }
    private void CompleteCore(string outcome, string source)
    {
        if (_last is null || _terminal || outcome is not ("clear" or "fail" or "interrupted")) return;
        if (source is not ("mapSettlement" or "clearRound" or "unitWipe" or "appExit" or "unknown")) source = "unknown";
        Add(NewEvent("outcome", source == "mapSettlement" ? "native-observed" : "observation-only") with
        {
            Outcome = outcome, OutcomeSource = source, Inventory = _lastGood, GoalUnitIds = _last.GoalUnitIds
        });
        _terminal = true;
        Seal();
    }
    public void Reset()
    {
        lock (_gate)
        {
            if (!permission()) { Forget(); return; }
            ResetCore();
        }
    }
    private void ResetCore()
    {
        CompleteCore("interrupted", "unknown");
        ClearSession();
    }
    public ImmutableArray<GameplayTelemetryPacket> DrainPackets() => Checkpoint(seal: true).SealedPackets;

    public GameplayTelemetryCheckpoint Checkpoint(bool seal = false)
    {
        lock (_gate)
        {
            if (!permission()) { Forget(); return new([], null); }
            if (seal) Seal();
            var sealedPackets = _packets.ToImmutableArray();
            _packets.Clear();
            var draft = _events.Count > 0 ? Packet(_events.ToImmutableArray()) : null;
            return new(sealedPackets, draft);
        }
    }
    private GameplayTelemetryEvent NewEvent(string kind, string evidence) => new()
    {
        Sequence = _sequence + 1, RecognitionRevision = _revision,
        ElapsedMs = Math.Max(0, (long)_time.GetElapsedTime(_started).TotalMilliseconds),
        Round = _last!.Round, CompletedStory = _last.CompletedStory, Mode = _last.Mode.ToString(),
        GuideNumber = _last.GuideNumber, Kind = kind, Evidence = evidence
    };
    private void Add(GameplayTelemetryEvent e)
    {
        // Split on BOTH count and serialized bytes; immutable packets never change on retry.
        if (_events.Count > 0 && (_events.Count >= 64 || JsonSerializer.SerializeToUtf8Bytes(Packet(_events.Append(e).ToImmutableArray()), GameplayTelemetryWire.JsonOptions).Length > GameplayTelemetryWire.MaxBytes)) Seal();
        _events.Add(e with { Sequence = ++_sequence });
        if (_events.Count == 64) Seal();
    }
    private GameplayTelemetryPacket Packet(ImmutableArray<GameplayTelemetryEvent> events) => new()
    {
        PacketId = _packetId, MatchId = _matchId!, ChunkIndex = _chunk,
        AppVersion = metadata.AppVersion, MapVersion = metadata.MapVersion, MapScriptSha256 = metadata.MapScriptSha256,
        ProfileVersion = metadata.ProfileVersion, Difficulty = _last!.Difficulty, Events = events
    };
    private void Seal()
    {
        if (_events.Count == 0) return;
        var packet = Packet(_events.ToImmutableArray());
        GameplayTelemetryWire.Serialize(packet);
        _packets.Enqueue(packet);
        _events.Clear();
        _chunk++;
        _packetId = Guid.NewGuid().ToString("N");
    }
    private void Forget() { _packets.Clear(); ClearSession(); }
    private void ClearSession()
    {
        _events.Clear(); _last = null; _receiptBaselines.Clear(); _matchId = null;
        _lastGood = ImmutableDictionary<string, int>.Empty;
        _revision = -1; _sequence = 0; _chunk = 0; _terminal = false; _lastRecommendation = null; _hadCaptureGap = false;
    }
    private bool SafeCatalogId(string id) => GameplayTelemetryWire.IsUnitId(id) &&
        (catalog.UnitsById.ContainsKey(id) || id.StartsWith("rawcode:", StringComparison.Ordinal) &&
            catalog.RawcodeCatalog.ContainsKey(id[8..]));
    private static bool IsNativeRawcode(string id) => id.StartsWith("rawcode:", StringComparison.Ordinal) &&
        id.Length == 12 && id.AsSpan(8).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789") < 0;
    private ImmutableArray<string> Goals(IEnumerable<string> ids) => ids.Where(SafeCatalogId).Distinct().Take(8).ToImmutableArray();
    private GameplayTelemetryObservation Sanitize(GameplayTelemetryObservation o) => o with
    {
        // A verified native four-byte unit code is a fact even if the catalog has not learned it yet.
        // It must not be promoted to a goal/recipe or widened to arbitrary user-provided identifiers.
        Inventory = o.Inventory.Where(p => (SafeCatalogId(p.Key) || IsNativeRawcode(p.Key)) && p.Value >= 0).Take(512).ToImmutableDictionary(),
        RewardWisps = o.RewardWisps.Where(p => GameplayTelemetryWire.IsWisp(p.Key) && p.Value >= 0).ToImmutableDictionary(),
        Resources = o.Resources.Where(p => GameplayTelemetryWire.IsResource(p.Key) && p.Value >= 0).ToImmutableDictionary(),
        GoalUnitIds = Goals(o.GoalUnitIds), CompletedStory = Math.Clamp(o.CompletedStory, 0, 14),
        GuideNumber = Math.Clamp(o.GuideNumber, 0, 99), Mode = Enum.IsDefined(o.Mode) ? o.Mode : PlayMode.Normal,
        Difficulty = MatchOutcomeDetector.IsKnownDifficulty(o.Difficulty) ? o.Difficulty : "unknown",
        GambleFailures = o.GambleFailures >= 0 ? o.GambleFailures : null,
        GambleCounters = o.GambleCounters?.Where(p => GameplayTelemetryWire.IsGambleCounterType(p.Key) && p.Value.IsValid).ToImmutableDictionary()
    };
    private static bool Equal<T>(ImmutableDictionary<string, T> a, ImmutableDictionary<string, T> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && EqualityComparer<T>.Default.Equals(p.Value, v));
    private static bool SameReceiptState(GameplayTelemetryObservation a, GameplayTelemetryObservation b) =>
        Equal(a.Inventory, b.Inventory) && Equal(a.RewardWisps, b.RewardWisps);
    private static bool SameObservation(GameplayTelemetryObservation a, GameplayTelemetryObservation b) =>
        a.Round == b.Round && a.CompletedStory == b.CompletedStory && a.Mode == b.Mode && a.GuideNumber == b.GuideNumber &&
        a.Difficulty == b.Difficulty && a.GambleFailures == b.GambleFailures && Equal(a.Inventory, b.Inventory) &&
        Equal(a.RewardWisps, b.RewardWisps) && Equal(a.Resources, b.Resources) && a.GoalUnitIds.SequenceEqual(b.GoalUnitIds) &&
        (a.GambleCounters is null ? b.GambleCounters is null : b.GambleCounters is not null && Equal(a.GambleCounters, b.GambleCounters));

    // Whole-inventory accounting only. Mixed actions are emitted only when the
    // decomposition is unique and all changes are explained within the search budget.
    private bool MatchReceipt(GameplayTelemetryObservation before, GameplayTelemetryObservation after)
    {
        var result = GameplayReceiptMatcher.Match(before, after, catalog);
        if (!result.IsMatched) return false;
        foreach (var craft in result.Crafts)
            Add(NewEvent("craft", "inventory-matched") with
            {
                UnitId = craft.UnitId, Count = craft.Count, Consumed = craft.Consumed
            });
        foreach (var selection in result.Selections)
            Add(NewEvent("selection", "inventory-matched") with
            {
                WispId = selection.WispId, Count = selection.Count, Outputs = selection.Outputs
            });
        return true;
    }
}
