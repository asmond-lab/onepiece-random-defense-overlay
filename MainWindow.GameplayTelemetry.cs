using System.Collections.Immutable;
using System.Windows;

namespace OrandOverlay;

public partial class MainWindow
{
    private GameplayTelemetryClient? _gameplayTelemetry;
    private GameplayRecoveryJournal? _gameplayRecovery;
    private long _gameplayLastObservedGeneration = -1;

    // Parent execution factory supplies null for fixtures and a consent-bound client for production.
    private void InitializeGameplayTelemetry(GameplayTelemetryClient? client)
    {
        _gameplayTelemetry = client;
        _gameplayRecovery = client is null ? null : _execution.CreateGameplayRecoveryJournal(_catalog);
        _gameplayRecovery?.RecoverPendingWrites();
        client?.Start();
    }
    private void CaptureGameplayTelemetry(RecognitionResult result)
    {
        if (_gameplayTelemetry is not { } client) return;
        // Ready boundary observations have already reset the parent generation before this hook.
        if (result.ShouldReplaceInventory)
        {
            _gameplayLastObservedGeneration = _adaptivePlanning.MatchGeneration;
            _gameplayRecovery?.MarkRecognitionRecovered();
            var highGamble = result.MapSignals.RouteQuests.HighGamble;
            var resources = ImmutableDictionary<string, long>.Empty;
            if (result.PlayerResources is { } native)
                resources = resources.Add("gold", native.Gold).Add("lumber", native.Lumber).Add("trait-points", native.TraitPoints);
            var observation = new GameplayTelemetryObservation
            {
                MatchGeneration = _adaptivePlanning.MatchGeneration, RecognitionRevision = _recognitionRevision,
                // UI keeps a monotonic last-known round; telemetry must use this scan's native round only.
                Round = result.Diagnostics.MapState?.MaxRound ?? 0, CompletedStory = result.MapSignals.CompletedStoryStageOrdinal,
                Mode = CurrentPlayMode, GuideNumber = _settings.GuideNumber, Difficulty = _matchDifficulty,
                // Do not include editable/manual simulated inventory in an actual gameplay observation.
                Inventory = result.Entries.GroupBy(e => e.UnitId).ToImmutableDictionary(g => g.Key, g => g.Sum(e => e.Count)),
                RewardWisps = result.MapSignals.RewardWisps, Resources = resources,
                GambleFailures = highGamble.IsVerified ? highGamble.Failures : null,
                GambleCounters = result.GambleCounters is { } counters &&
                    counters.LocalSlot == result.VerifiedLocalPlayerSlot &&
                    counters.MapScriptSha256 == RouteQuestCatalog.MapScriptSha256
                    ? counters.Counters.ToImmutableDictionary(counter => counter.Kind switch
                    {
                        GambleCounterKind.Low => "low", GambleCounterKind.Middle => "middle",
                        GambleCounterKind.High => "high", GambleCounterKind.World => "world",
                        GambleCounterKind.Absalom => "absalom", GambleCounterKind.LumberWisp => "lumberWisp",
                        _ => throw new ArgumentOutOfRangeException(nameof(counter.Kind))
                    }, counter => new GameplayTelemetryGambleCounter(counter.Attempts, counter.Successes, counter.Failures))
                    : null,
                GoalUnitIds = CurrentPlayMode == PlayMode.Guide && _settings.GuideNumber == 1
                    ? [BulletGuidePolicy.GoalId] : CurrentPlayMode == PlayMode.Manual ? SelectedManualGoals() :
                    SelectedGoal is { } goal ? [goal.Id] : []
            };
            if (observation.Round is < 1 or > 65)
                _gameplayRecovery?.RecordObservation(observation, "round-unavailable");
            else if (client.IsBackpressured)
                _gameplayRecovery?.RecordObservation(observation, "storage-backpressure");
            var pausedBefore = client.Recorder.PausedObservationCount;
            client.Recorder.Observe(observation);
            // Backpressure can change on the transport thread between the check above and Observe.
            if (client.Recorder.PausedObservationCount > pausedBefore)
                _gameplayRecovery?.RecordObservation(observation, "storage-backpressure");
            client.NotifyObservation();
        }
        else if (_outcome.Outcome is not ("clear" or "fail")) RecordGameplayRecognitionGap(result.State);
        if (_outcome.Outcome is "clear" or "fail")
        {
            client.Recorder.Complete(_outcome.Outcome, _outcome.OutcomeSource);
            client.RequestFlush();
        }
        UpdateGameplayRecordingStatus();
    }

    private void RecordGameplayRecognitionGap(RecognitionState state)
    {
        if (_gameplayLastObservedGeneration != _adaptivePlanning.MatchGeneration) return;
        _gameplayRecovery?.RecordGap(_adaptivePlanning.MatchGeneration, _recognitionRevision,
            _lastRound is >= 1 and <= 65 ? _lastRound : null, state.ToString());
        UpdateGameplayRecordingStatus();
    }

    private void UpdateGameplayRecordingStatus()
    {
        if (TryShowLocalActivityRecordingWarning()) return;
        if (_gameplayTelemetry is not { } client) return;
        var messages = new List<string>();
        if (client.IsBackpressured) messages.Add("기록을 저장하지 못하고 기다리고 있어요. 저장 공간을 확인해 주세요");
        else if (client.LastError is not null) messages.Add("기록을 저장하거나 보내는 중 오류가 있었어요. 다시 확인해 주세요");
        if (client.ExpiredFileCount > 0) messages.Add($"보관 기간 30일이 지난 기록 {client.ExpiredFileCount}개");
        if (_gameplayRecovery?.IncompleteTemporaryCount > 0) messages.Add("앱이 갑자기 종료되어 일부 기록이 불완전해요");
        if (_gameplayRecovery?.LastError is not null) messages.Add("기록을 복구할 자료를 저장하지 못했어요. 일부 기록이 빠질 수 있어요");
        else if (_gameplayRecovery?.SavedCount > 0) messages.Add("확인하지 못한 게임 정보가 있어요. 이 컴퓨터에 남은 기록을 확인해 주세요");
        if (client.Recorder.PausedObservationCount > 0) messages.Add("저장이 늦어지는 동안 게임 정보가 들어왔어요. 남은 기록을 확인해 주세요");
        GameplayRecordingStatus.Text = string.Join(" · ", messages);
        GameplayRecordingStatus.Visibility = messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RecordGameplayRecommendation(CoachFrame frame, CoachDecision decision)
    {
        _gameplayTelemetry?.Recorder.Recommend(frame, decision);
        _gameplayTelemetry?.NotifyObservation();
    }
    private void ResetGameplayTelemetry()
    {
        _gameplayLastObservedGeneration = -1;
        _gameplayTelemetry?.Recorder.Reset();
        _gameplayTelemetry?.RequestFlush();
    }
    private async Task ShutdownGameplayTelemetryAsync()
    {
        if (_gameplayTelemetry is not { } client) return;
        await client.ShutdownAsync();
        var pending = client.UnpersistedPacketsAfterShutdown();
        if (pending.Count == 0 || _gameplayRecovery is not { } recovery) return;
        var saved = await Task.Run(() => pending.Count(packet => recovery.RecordPendingPacket(packet)));
        if (saved != pending.Count && recovery.LastError != "permission-denied" && _execution.RuntimeEnabled)
            MessageBox.Show(this,
                "저장 공간이 부족해 이번 판의 마지막 기록 일부를 저장하지 못했어요. 이전에 저장을 기다리던 기록은 남아 있어요. 저장 공간을 확인해 주세요.",
                "기록 저장 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
