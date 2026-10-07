namespace OrandOverlay;

public partial class MainWindow
{
    private Task? _bulletLearningRefresh;
    private string? _bulletLearningRequestedDifficulty;
    private DateTimeOffset _bulletLearningNextRefresh;

    // The parent-owned Plan call consumes this bound snapshot, never _liveStats.
    private BulletGuideLearningBridge? CurrentBulletGuideLearning =>
        MapDatasetRuntimePolicy.AllowsLegacyRuntime(_catalog.MapVersion) &&
        CurrentPlayMode == PlayMode.Guide && _settings.GuideNumber == 1 && !_gameplayClosing &&
        _gameplayStatsRefresh is { } service
            ? BulletGuideLearningBridge.FromSnapshot(
                service.GetBulletSnapshot(RouteQuestCatalog.MapScriptSha256, _matchDifficulty),
                RouteQuestCatalog.MapScriptSha256, _matchDifficulty) : null;

    private void RequestBulletLearning()
    {
        if (!MapDatasetRuntimePolicy.AllowsLegacyRuntime(_catalog.MapVersion) ||
            CurrentPlayMode != PlayMode.Guide || _settings.GuideNumber != 1 || _gameplayClosing ||
            _gameplayStatsRefresh is not { } service) return;
        var hash = RouteQuestCatalog.MapScriptSha256;
        var difficulty = _matchDifficulty;
        if (!LiveStats.IsCohort(hash, difficulty)) return;
        if (_bulletLearningRefresh is not { IsCompleted: false } &&
            (_bulletLearningRequestedDifficulty != difficulty || DateTimeOffset.UtcNow >= _bulletLearningNextRefresh))
        {
            _bulletLearningRequestedDifficulty = difficulty;
            _bulletLearningNextRefresh = DateTimeOffset.UtcNow.AddMinutes(5);
            _bulletLearningRefresh = RefreshBulletLearningAsync(service, hash, difficulty);
        }
    }

    private async Task RefreshBulletLearningAsync(GameplayStatsRefreshService service, string hash, string difficulty)
    {
        try { await service.RefreshBulletAsync(hash, difficulty, _gameplayLifetime.Token); }
        catch (OperationCanceledException) when (_gameplayLifetime.IsCancellationRequested) { return; }
        if (_gameplayClosing || CurrentPlayMode != PlayMode.Guide || _settings.GuideNumber != 1 ||
            difficulty != _matchDifficulty || hash != RouteQuestCatalog.MapScriptSha256) return;
        if (service.GetBulletSnapshot(hash, difficulty).MatchesCohort(hash, difficulty, LiveStats.BulletProfile))
            RefreshAll();
    }
}
