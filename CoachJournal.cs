using System.Text.Json;

namespace OrandOverlay;

/// <summary>Local-only observations; never connected to the anonymous telemetry uploader.</summary>
public sealed class CoachJournal
{
    private readonly string? directory;
    private readonly List<string>? _memory;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private CoachFrame? _lastCurrent;
    private CoachDecision? _lastDecision;
    private long _generation = -1;
    private long _revision = -1;
    private long _sequence;
    private string? _signature;

    public CoachJournal(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = directory;
    }

    private CoachJournal(List<string> memory) => _memory = memory;

    public static CoachJournal Memory() => new(new List<string>());

    public bool IsPersistent => directory is not null;
    public bool HasLatest => _memory is { Count: > 0 } || LatestPath is not null;
    public string? LatestPath { get; private set; }
    public string? LastError { get; private set; }

    internal IEnumerable<string> ReadbackLines() => _memory is not null
        ? _memory
        : LatestPath is null ? [] : File.ReadLines(LatestPath);

    public async Task FlushAsync()
    {
        await _writer.WaitAsync().ConfigureAwait(false);
        _writer.Release();
    }

    public async Task<bool> RecordAsync(CoachFrame frame, CoachDecision decision)
    {
        await _writer.WaitAsync().ConfigureAwait(false);
        try
        {
            if (frame.MatchGeneration < _generation ||
                frame.MatchGeneration == _generation && frame.Revision < _revision) return false;
            if (frame.MatchGeneration != _generation)
            {
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                    LatestPath = Path.Combine(directory,
                        $"match-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
                }
                else
                {
                    _memory!.Clear();
                    LatestPath = null;
                }
                _generation = frame.MatchGeneration;
                _revision = -1;
                _sequence = 0;
                _lastCurrent = null;
                _lastDecision = null;
                _signature = null;
            }
            var signature = JsonSerializer.Serialize(new
            {
                frame.Round, frame.CompletedStoryStage, frame.IsCurrent, frame.Difficulty,
                frame.Outcome, frame.ConfirmedNavigation, frame.GoalId, frame.Paused, frame.GuideVisible,
                Inventory = frame.Inventory.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                Signals = frame.Signals.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                decision.Id, decision.Kind
            });
            _revision = frame.Revision;
            if (signature == _signature) return false;
            var delta = frame.IsCurrent && _lastCurrent is { } previous
                ? frame.Inventory.Keys.Union(previous.Inventory.Keys).Order(StringComparer.Ordinal)
                    .Select(id => new { UnitId = id, Change = frame.Inventory.GetValueOrDefault(id) -
                        previous.Inventory.GetValueOrDefault(id) })
                    .Where(item => item.Change != 0).ToArray()
                : [];
            var acquiredTarget = frame.IsCurrent && frame.GuideVisible && _lastCurrent is { GuideVisible: true } last &&
                                 _lastDecision?.TargetUnitId is { } target &&
                                 frame.Inventory.GetValueOrDefault(target) > last.Inventory.GetValueOrDefault(target)
                ? target : null;
            var row = JsonSerializer.Serialize(new
            {
                SchemaVersion = 1, Sequence = ++_sequence, RecordedAtUtc = DateTimeOffset.UtcNow,
                frame.MatchGeneration, frame.Revision, frame.Round, frame.CompletedStoryStage,
                frame.IsCurrent, frame.Difficulty, frame.Outcome, frame.ConfirmedNavigation, frame.GoalId,
                frame.GuideVisible,
                frame.Inventory, frame.Signals, Decision = decision,
                ObservedInventoryDelta = delta, AcquiredRecommendedUnit = acquiredTarget,
                Evidence = acquiredTarget is null ? "observation" : "inventory-increase-not-input-confirmation"
            });
            if (_memory is not null) _memory.Add(row);
            else await File.AppendAllTextAsync(LatestPath!, row + Environment.NewLine).ConfigureAwait(false);
            _revision = frame.Revision;
            _signature = signature;
            if (frame.IsCurrent)
            {
                _lastCurrent = frame;
                _lastDecision = frame.GuideVisible ? decision : null;
            }
            LastError = null;
            return true;
        }
        catch (IOException exception)
        {
            LastError = exception.Message;
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            LastError = exception.Message;
            return false;
        }
        finally
        {
            _writer.Release();
        }
    }
}
