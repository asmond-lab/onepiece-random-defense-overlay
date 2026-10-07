using System.ComponentModel;
using System.Windows.Threading;

namespace OrandOverlay;

public partial class MainWindow
{
    private GameplayStatsRefreshService? _gameplayStatsRefresh;
    private readonly DispatcherTimer _gameplayStatsTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly CancellationTokenSource _gameplayLifetime = new();
    private string? _gameplayRequestedDifficulty;
    private bool _gameplayClosing;
    private bool _gameplayCloseReady;

    private void InitializeGameplayServices()
    {
        InitializeObservedTelemetry();
        InitializeGameplayTelemetry(_execution.CreateGameplayTelemetry(new GameplayTelemetryMetadata(
            UpdateService.CurrentBuildVersion, "2.314", RouteQuestCatalog.MapScriptSha256,
            "2.0.4.23745"), _catalog));
        if (MapDatasetRuntimePolicy.AllowsLegacyRuntime(_catalog.MapVersion))
        {
            _gameplayStatsRefresh = _execution.CreateGameplayStatsRefreshService();
            ConfigureGameplayEngine(_engine, _matchDifficulty);
            _gameplayStatsTimer.Tick += async (_, _) => await RefreshGameplayStatsAsync();
            _gameplayStatsTimer.Start();
        }
        else
        {
            // Preserve historical queue files, but do not label new offline observations as 2.314.
            _telemetry.SetEnabled(false, deletePending: false);
        }
        Closing += Gameplay_OnClosing;
        Closed += (_, _) => _gameplayLifetime.Dispose();
    }

    private void ConfigureGameplayEngine(RecommendationEngine engine, string difficulty)
    {
        if (!MapDatasetRuntimePolicy.AllowsLegacyRuntime(_catalog.MapVersion)) return;
        engine.SetGameplayCohort(RouteQuestCatalog.MapScriptSha256, difficulty);
        if (_gameplayStatsRefresh is { } service)
            engine.SetLiveStats(service.GetSnapshot(RouteQuestCatalog.MapScriptSha256, difficulty));
    }

    private void RequestGameplayStatsForCurrentDifficulty()
    {
        if (_gameplayStatsRefresh is null || _gameplayRequestedDifficulty == _matchDifficulty) return;
        _gameplayRequestedDifficulty = _matchDifficulty;
        ConfigureGameplayEngine(_engine, _matchDifficulty);
        _ = RefreshGameplayStatsAsync();
    }

    private async Task RefreshGameplayStatsAsync()
    {
        if (_gameplayStatsRefresh is not { } service || _gameplayClosing) return;
        RequestBulletLearning();
        var difficulty = _matchDifficulty;
        var hash = RouteQuestCatalog.MapScriptSha256;
        if (!LiveStats.IsCohort(hash, difficulty)) return;
        try { await service.RefreshAsync(hash, difficulty, _engine, _gameplayLifetime.Token); }
        catch (OperationCanceledException) when (_gameplayLifetime.IsCancellationRequested) { return; }
        if (_gameplayClosing || difficulty != _matchDifficulty) return;
        var snapshot = service.GetSnapshot(hash, difficulty);
        if (!snapshot.MatchesCohort(hash, difficulty) || ReferenceEquals(snapshot, _liveStats)) return;
        _liveStats = snapshot;
        ConfigureGameplayEngine(_engine, difficulty);
        RefreshAll("현재 난이도의 플레이 통계를 반영했습니다.");
    }

    private async void Gameplay_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_gameplayCloseReady) return;
        e.Cancel = true;
        if (_gameplayClosing) return;
        _gameplayClosing = true;
        _timer.Stop();
        _gameplayStatsTimer.Stop();
        _scanCancellation?.Cancel();
        try
        {
            await _gameplayLifetime.CancelAsync();
            await StopObservedTelemetryAsync();
            await ShutdownGameplayTelemetryAsync();
            await StopActivityRecordingAsync();
        }
        finally
        {
            _gameplayCloseReady = true;
            Close();
        }
    }
}
