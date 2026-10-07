using System.ComponentModel;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OrandOverlay;

// Direct Warcraft reader. Profiles remain fail-closed until an exact executable hash and
// all layout offsets have been independently verified for that build.
public sealed partial class WarcraftMemoryRecognitionService : IInventoryRecognizer, IModernBasicInventorySource
{
    // 2.0.4 CUnit: rawcode 0x178와 같은 베이스 구조의 보존된 플레이어 색상.
    // SetUnitOwner(..., false) 뒤에도 원 소유 플레이어 색상이 남아 멀티 성장형을 구분한다.
    internal const int UnitPlayerColorOffset = 0x16C;
    private readonly RawcodeUnitMap _unitMap;
    private readonly DataCatalog _catalog;
    internal Map2321ActivityRules? ActivityRules { get; set; }
    private readonly string _selectedMapVersion;
    private readonly RuntimeMapIdentitySession _mapIdentitySession = new();
    private readonly MapSignalRecognitionProfile? _mapSignalProfile;
    private readonly MapSignalSnapshotTracker? _mapSignalTracker;
    private readonly string? _mapSignalProfileError;
    private readonly GrowthUnitPointerTracker _growthPointers = new();
    private readonly Warcraft300GrowthReader _diagnosticGrowthReader = new();
    private readonly object _diagnosticGrowthGate = new();
    private Map2320GrowthSource? _diagnosticGrowthSource;
    private readonly string _diagnosticObservationSalt = Guid.NewGuid().ToString("N");
    private string? _diagnosticObservationContext;
    private long _diagnosticObservationEpoch;
    private long _diagnosticObservationRevision;
    private long _growthCacheProcessStarted = long.MinValue;
    private int _savedGrowthRevision = -1;
    private readonly string GrowthPointerCachePath;
    private readonly MemoryProfileRepository _profiles;
    private readonly object _cacheGate = new();
    internal const int MapStateBackgroundBudgetBytes = 4 * 1024 * 1024;
    private readonly IncrementalMapStateScanner _mapStateScanner =
        new(MapStateBackgroundBudgetBytes);
    private readonly WarcraftCurrentRoundReader _currentRoundReader = new();
    private readonly WarcraftCurrentRoundReader _storyCompletionReader =
        new(MapSignalRecognitionProfile.ParseStoryCompletionTitle);
    private LocatorCache? _locatorCache;
    private readonly WarcraftRouteQuestReader _routeQuestReader = new();
    private readonly BulletGuideRuntimeReader _guideRuntimeReader = new();
    private readonly PlayerResourceReader _playerResourceReader = new();
    private readonly LoadedClearCountReader _loadedClearCountReader = new();
    private readonly DestructionKingReader _destructionKingReader = new();
    private long _questProcessStarted = long.MinValue;

    /// <summary>전체 힙 스캔 대신 매 인식 틱에 작은 조각만 읽는다.</summary>
    private static readonly TimeSpan MapStateSliceInterval = TimeSpan.FromSeconds(1);
    internal const int MapStateEndgameBudgetBytes = 128 * 1024;
    private DateTimeOffset _lastMapStateAt = DateTimeOffset.MinValue;
    private MapStateSample? _lastMapState;
    private static readonly TimeSpan WaitingLocatorRescanInterval = TimeSpan.FromSeconds(5);
    private bool _sessionBoundaryCachesCleared;
    private DateTimeOffset _nextWaitingLocatorRescanAt = DateTimeOffset.MinValue;

    public WarcraftMemoryRecognitionService(DataCatalog catalog)
        : this(catalog, AppPaths.UserDataDirectory) { }

    internal WarcraftMemoryRecognitionService(DataCatalog catalog, string userRoot)
        : this(catalog, userRoot, null) { }

    private readonly ExpectedReadTarget? _expectedReadTarget;

    internal WarcraftMemoryRecognitionService(DataCatalog catalog, string userRoot, ExpectedReadTarget? expectedTarget)
    {
        _expectedReadTarget = expectedTarget;
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _selectedMapVersion = catalog.MapVersion;
        GrowthPointerCachePath = Path.Combine(userRoot, "growth-unit-pointers.json");
        _profiles = new MemoryProfileRepository(Path.Combine(userRoot, "memory-profiles.json"));
        _unitMap = new RawcodeUnitMap(catalog);
        try
        {
            _mapSignalProfile = MapSignalRecognitionProfile.FromStory(
                MapStoryProfileLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")));
            _mapSignalTracker = new MapSignalSnapshotTracker(_mapSignalProfile);
        }
        catch (InvalidDataException exception)
        {
            _mapSignalProfileError = exception.Message;
        }
    }

    public Task<RecognitionResult> RecognizeAsync(AppSettings settings, CancellationToken cancellationToken) =>
        Task.Run(() => RunNativeRead(() => Recognize(cancellationToken,
            PlayModes.Current(settings) == PlayMode.Guide && settings.GuideNumber == 1), cancellationToken), cancellationToken);

    private RecognitionResult Recognize(CancellationToken token, bool readGuide,
        Func<Func<DiagnosticRecognitionFrame>, bool>? tryEmitFrame = null) =>
        RecognizeCore(token, readGuide, tryEmitFrame) ??
        throw new InvalidOperationException("Full recognition ended without a result.");

    private RecognitionResult? RecognizeCore(CancellationToken token, bool readGuide,
        Func<Func<DiagnosticRecognitionFrame>, bool>? tryEmitFrame = null,
        Action<DiagnosticRecognitionFrame>? basicOnly = null)
    {
        var recognitionStarted = Stopwatch.GetTimestamp();
        var localPlayerConfirmed = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var loaded = _profiles.GetProfiles();
            if (loaded.Error is not null)
                return Failure(RecognitionState.ConfigurationError, "게임 인식 설정을 불러오지 못했습니다 · 기존 패 유지", loaded.Error,
                    profileSource: loaded.Source);
            if (_unitMap.Error is not null)
                return Failure(RecognitionState.ConfigurationError, "유닛 정보를 불러오지 못했습니다 · 기존 패 유지", _unitMap.Error);
            if (_mapSignalProfileError is not null || _mapSignalProfile is null || _mapSignalTracker is null)
                return Failure(RecognitionState.ConfigurationError, "맵 인식 설정을 불러오지 못했습니다 · 기존 패 유지",
                    _mapSignalProfileError ?? "맵 신호 프로필을 불러오지 못했습니다.");

            using var process = FindNewestProcess("Warcraft III");
            if (process is null)
            {
                _mapIdentitySession.Reset();
                ResetSessionCaches(force: true);
                return Failure(RecognitionState.Waiting, "워크래프트 실행 대기 · 기존 패 유지", "워크래프트를 실행하면 게임을 확인합니다.");
            }

            // Explicit validation target fence: BEFORE module identity, hashing or any memory handle.
            _expectedReadTarget?.EnsureMatches(process.Id, process.StartTime.ToUniversalTime().Ticks);
            _mapIdentitySession.ResetIfDifferent(process.Id, process.StartTime.ToUniversalTime().Ticks);

            var mainModule = process.MainModule;
            var version = mainModule?.FileVersionInfo.FileVersion ?? "unknown";
            var executablePath = mainModule?.FileName;
            // File identity is safe to collect even when the new build is not authorized for memory reads.
            var actualHash = string.IsNullOrWhiteSpace(executablePath) ? "" : ExecutableHashCache.Sha256(executablePath);
            var baseDiagnostics = new RecognitionDiagnostics
            {
                Source = "WarcraftMemory",
                ProcessId = process.Id,
                ProcessVersion = version,
                ExecutableSha256 = actualHash,
                ProfileSource = loaded.Source
            };
            var profile = loaded.Profiles.FirstOrDefault(x =>
                x.FileVersion.Equals(version, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
                return Failure(RecognitionState.Unsupported, $"워크래프트 {version}은 아직 지원하지 않습니다 · 기존 패 유지",
                    "이 빌드와 정확히 일치하는 프로필만 사용할 수 있습니다.", baseDiagnostics);

            baseDiagnostics = WithProfile(baseDiagnostics, profile);
            var referenceOnly = MapDatasetRuntimePolicy.AllowsReferenceObservation(_selectedMapVersion,
                _catalog.SelectedDatasetFingerprint, profile, version, actualHash);
            // 검증 세션(ORAND_VERIFY_DIR 지정) 동안에만 미검증 프로필을 임시 활성한다.
            // 실전 1판 검증을 통과해야 verified=true로 핀되므로, 일반 실행에서는 fail-closed 유지.
            var verificationSession = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("ORAND_VERIFY_DIR"));
            if (!referenceOnly && (!profile.Enabled || (!profile.Verified && !verificationSession) ||
                (profile.KnownExperimental && !verificationSession)))
                return Failure(RecognitionState.UnverifiedProfile, $"워크래프트 {version}의 인식 방식은 확인 중입니다 · 기존 패 유지",
                    "enabled와 verified가 모두 true인 검증 프로필이 필요합니다.", baseDiagnostics);

            var validationErrors = MemoryProfileValidator.Validate(profile);
            if (validationErrors.Count > 0)
                return Failure(RecognitionState.ConfigurationError, "게임 인식 설정을 확인해 주세요 · 기존 패 유지",
                    string.Join("; ", validationErrors), baseDiagnostics);

            if (string.IsNullOrWhiteSpace(executablePath))
                return Failure(RecognitionState.TransientReadError, "워크 실행 파일 확인 실패 · 기존 패 유지",
                    "MainModule.FileName을 읽을 수 없습니다.", baseDiagnostics);
            if (!actualHash.Equals(profile.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                return Failure(RecognitionState.UnverifiedProfile, "확인된 워크래프트 실행 파일과 다릅니다 · 기존 패 유지",
                    $"프로필 {profile.ExecutableSha256[..12]}… / 실행 파일 {actualHash[..12]}…", baseDiagnostics);

            var module = process.Modules.Cast<ProcessModule>().FirstOrDefault(x =>
                x.ModuleName.Equals(profile.ModuleName, StringComparison.OrdinalIgnoreCase));
            if (module is null)
                return Failure(RecognitionState.TransientReadError, "게임 정보를 준비하고 있습니다",
                    "대상 모듈이 아직 로드되지 않았습니다.", baseDiagnostics);

            // A newly selected offline map must never lend its approval to legacy native offsets.
            if (_catalog.MapVersion != _selectedMapVersion ||
                (!referenceOnly && !MapDatasetRuntimePolicy.AllowsReader(_selectedMapVersion, profile.KnownExperimental, verificationSession)))
                return Failure(RecognitionState.UnverifiedProfile, MapDatasetRuntimePolicy.OfflineOnlyMessage,
                    "맵 원본 분석은 실행 파일·로컬 플레이어·보조 리더 검증을 대체하지 않습니다.", baseDiagnostics);
            if (basicOnly is not null &&
                (!(referenceOnly || profile.KnownExperimental) || !_catalog.HasModernSource))
                return Failure(RecognitionState.UnverifiedProfile, MapDatasetRuntimePolicy.OfflineOnlyMessage,
                    "Basic-only recognition requires the modern reference reader.", baseDiagnostics);
            if (_selectedMapVersion is "2.322" or "2.323")
            {
                var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Warcraft III", "Logs", "War3Log.txt");
                // War3Log records are local wall-clock fields; preserve StartTime's local offset.
                var pin = _selectedMapVersion == Map2323SourceContract.MapVersion
                    ? new MapArchivePin("", Map2323SourceContract.ArchiveLengthBytes, Map2323SourceContract.ArchiveSha256)
                    : new MapArchivePin("", Map2322SourceContract.ArchiveLengthBytes, Map2322SourceContract.ArchiveSha256);
                var archive = RuntimeMapIdentityProvider.Probe(
                    new DateTimeOffset(process.StartTime), log, pin,
                    _mapIdentitySession, process.Id, process.StartTime.ToUniversalTime().Ticks);
                if (!MapDatasetRuntimePolicy.AllowsObservedArchive(_selectedMapVersion, archive))
                    return Failure(RecognitionState.UnverifiedProfile, MapDatasetRuntimePolicy.OfflineOnlyMessage,
                        "현재 세션의 " + _selectedMapVersion + " 맵 압축파일을 확인할 수 없습니다: " + archive.Failure, baseDiagnostics);
            }
            using var memory = ReadOnlyProcessMemory.Open(process.Id);
            // Return before all legacy local identity, optional readers, growth caches and map claims.
            var processStarted = process.StartTime.ToUniversalTime().Ticks;
            if (basicOnly is not null)
            {
                basicOnly(ReadBasicInventory(memory, module, profile, baseDiagnostics,
                    DiagnosticSessionKey(baseDiagnostics, processStarted, profile, loaded.Generation), token));
                return null; // Explicit basic-only API, not a successful full completion.
            }
            if (referenceOnly || profile.KnownExperimental)
                return ReadDiagnosticInventory(memory, module, profile, baseDiagnostics, processStarted, loaded.Generation, token, tryEmitFrame);
            if (_questProcessStarted != processStarted)
            {
                _routeQuestReader.Reset();
                _guideRuntimeReader.Reset();
                _playerResourceReader.Reset();
                _loadedClearCountReader.Reset();
                _destructionKingReader.Reset();
                _questProcessStarted = processStarted;
            }
            EnsureGrowthPointerCache(processStarted);
            var moduleBase = (ulong)module.BaseAddress.ToInt64();
            // 로컬 슬롯은 실측 앵커가 있으면 실제 값을, 없으면 프로필 고정값을 쓴다.
            var measuredSlot = StructuralUnitPoolScanner.TryReadLocalPlayerSlot(memory, moduleBase, profile);
            // 앵커가 있는데 로컬 플레이어가 없으면 대전 중이 아니다. 비싼 구조 스캔을 돌리지 않는다.
            if (profile.HasLocalPlayerAnchor && measuredSlot is null)
            {
                // 로딩·로비에서 앵커가 잠깐 읽혀도 전체 재탐색은 5초에 한 번만 허용한다.
                ResetSessionCaches(allowPeriodicRescan: true);
                return Failure(RecognitionState.Waiting, "대전 대기 중 · 기존 패 유지",
                    "게임에 입장하면 인식을 시작합니다.", baseDiagnostics);
            }
            localPlayerConfirmed = profile.HasLocalPlayerAnchor && measuredSlot is not null;
            var localSlot = measuredSlot ?? profile.LocalPlayerSlot;
            var locatorAddress = GetLocatorAddress(memory, process, processStarted, module, profile, loaded.Generation, token);
            var listAddress = FollowPointerPath(memory, locatorAddress, profile.PointerOffsets);
            MemoryUnitSnapshot snapshot;
            try
            {
                snapshot = ReadConsistentSnapshot(memory, listAddress, profile, localSlot,
                    _unitMap.IsGrowthUnit, _growthPointers.Snapshot(), _mapSignalProfile, token,
                    readGuide && localPlayerConfirmed);
            }
            catch (SnapshotChangedException)
            {
                token.ThrowIfCancellationRequested();
                listAddress = FollowPointerPath(memory, locatorAddress, profile.PointerOffsets);
                snapshot = ReadConsistentSnapshot(memory, listAddress, profile, localSlot,
                    _unitMap.IsGrowthUnit, _growthPointers.Snapshot(), _mapSignalProfile, token,
                    readGuide && localPlayerConfirmed);
            }

            _growthPointers.Commit(snapshot.LocallyObservedGrowth, snapshot.SeenTrackedGrowthPointers);
            SaveGrowthPointerCache(processStarted);
            // 중립 성장형은 한 기뿐이어도 다른 플레이어 것일 수 있다. 로컬 소유로
            // 직접 봤거나 같은 CUnit 포인터로 소유권 이전을 증명한 카드만 포함한다.
            var counts = GrowthUnitOwnershipPolicy.InventoryCounts(
                snapshot.RawcodeCounts, snapshot.NeutralGrowthCounts,
                snapshot.RetainedGrowthObjects > 0 ||
                snapshot.LocallyObservedGrowth.Count > 0);
            var growthTotal = snapshot.NeutralGrowthCounts.Values.Sum();
            var adoptedNeutralGrowth = counts.Values.Sum() >
                                       snapshot.RawcodeCounts.Values.Sum();
            var trackedNow = _growthPointers.Snapshot();
            var growthRawcodes = snapshot.LocallyObservedGrowth.Values
                .Concat(snapshot.SeenTrackedGrowthPointers
                    .Where(trackedNow.ContainsKey)
                    .Select(pointer => trackedNow[pointer]))
                .ToList();
            if (adoptedNeutralGrowth)
                growthRawcodes.Add(snapshot.NeutralGrowthCounts.Single().Key);
            var mapped = _unitMap.Map(counts);
            var growthUnitIds = MapGrowthUnitIds(_unitMap, growthRawcodes);
            var mapState = ReadMapStateThrottled(memory, version,
                _mapSignalProfile.MapScriptSha256, localPlayerConfirmed ? measuredSlot : null,
                moduleBase, module.ModuleMemorySize, token);
            PlayerResourceState? playerResources;
            using (memory.BeginReadChannel(AdaptivePlanningReadChannel.MapState))
                playerResources = _playerResourceReader.Read(memory.ReadAvailable,
                    memory.ReadablePrivateRegions(), version, _mapSignalProfile.MapScriptSha256,
                    localPlayerConfirmed ? measuredSlot : null, token);
            var recognitionObservation = new AdaptivePlanningRecognitionObservation(
                memory.SnapshotReads(), Stopwatch.GetElapsedTime(recognitionStarted),
                snapshot.SideChannelByteBudget, snapshot.SideChannelCallBudget);
            var diagnostics = new RecognitionDiagnostics
            {
                Source = baseDiagnostics.Source,
                ProcessId = baseDiagnostics.ProcessId,
                ProcessVersion = baseDiagnostics.ProcessVersion,
                ExecutableSha256 = baseDiagnostics.ExecutableSha256,
                ProfileId = profile.ProfileId,
                ProfileRevision = profile.ProfileRevision,
                ProfileSource = loaded.Source,
                ResolvedListAddress = $"0x{listAddress:X}",
                ObservedObjects = snapshot.OwnedObjects + (adoptedNeutralGrowth ? 1 : 0),
                ForeignObjects = snapshot.ForeignObjects,
                MapState = mapState,
                AdaptivePlanningObservation = recognitionObservation,
                MappedObjects = mapped.KnownCount + mapped.CatalogNamedCount,
                UnknownObjects = mapped.UnknownCount,
                UnknownRawcodes = mapped.UnknownRawcodes,
                GrowthUnitIds = growthUnitIds,
                GoroseiMarker = new WarcraftGoroseiReader(memory.ReadAvailable, moduleBase, module.ModuleMemorySize)
                    .Read(version, _mapSignalProfile.MapScriptSha256, snapshot.GoroseiCandidates, token),
                Detail = $"목록 슬롯 {snapshot.ListCount} · 타 소유 {snapshot.ForeignObjects} · " +
                         $"추천 데이터 연결 {mapped.KnownCount} · " +
                         $"이름 카탈로그 연결 {mapped.CatalogNamedCount} · 중복 포인터 {snapshot.DuplicatePointers}" +
                         (snapshot.RetainedGrowthObjects > 0
                             ? $" · 소유권 이동 성장형 {snapshot.RetainedGrowthObjects}기 유지"
                             : "") +
                         (adoptedNeutralGrowth
                             ? " · 시작 지급 성장형 1기 포함"
                             : growthTotal > 0
                             ? $" · 중립 성장형 {growthTotal}기(로컬 관측 없음으로 제외)"
                             : "") +
                         (profile.LocatorKind == MemoryLocatorKind.StructuralScan
                             ? $" · 구조 탐색 {StructuralUnitPoolScanner.LastScanMilliseconds}ms"
                             : "")
            };
            if (profile.RequireNonEmptyInventory && snapshot.OwnedObjects == 0)
            {
                ResetSessionCaches(allowPeriodicRescan: true);
                return new RecognitionResult
                {
                    State = RecognitionState.Waiting,
                    Status = "보유 패 없음 · 대기 중",
                    ConfirmsSessionBoundary = true,
                    Diagnostics = diagnostics
                };
            }
            var catalogMatches = mapped.KnownCount + mapped.CatalogNamedCount;
            // 뽑기 전에는 로컬 소유 객체가 맵 컨트롤러·헬퍼뿐이라 카드가 0장이다.
            // 이것은 읽기 오류가 아니라 아직 패가 없는 상태다. 이전 판 목표를 여기서 푼다.
            if (catalogMatches == 0)
            {
                ResetSessionCaches(allowPeriodicRescan: true);
                return new RecognitionResult
                {
                    State = RecognitionState.Waiting,
                    Status = "보유 패 없음 · 대기 중",
                    ConfirmsSessionBoundary = true,
                    NativeUnitPointers = snapshot.NativeUnits,
                    VerifiedLocalPlayerSlot = localPlayerConfirmed ? measuredSlot : null,
                    Diagnostics = diagnostics
                };
            }
            var catalogRatio = (double)catalogMatches / Math.Max(1, diagnostics.ObservedObjects);
            if (catalogRatio < profile.MinimumCatalogMatchRatio)
                return new RecognitionResult
                {
                    State = RecognitionState.TransientReadError,
                    Status = "보유 유닛을 정확히 읽지 못했습니다 · 기존 패 유지",
                    Diagnostics = new RecognitionDiagnostics
                    {
                        Source = diagnostics.Source,
                        ProcessId = diagnostics.ProcessId,
                        ProcessVersion = diagnostics.ProcessVersion,
                        ExecutableSha256 = diagnostics.ExecutableSha256,
                        ProfileId = diagnostics.ProfileId,
                        ProfileRevision = diagnostics.ProfileRevision,
                        ProfileSource = diagnostics.ProfileSource,
                        ResolvedListAddress = diagnostics.ResolvedListAddress,
                        ObservedObjects = diagnostics.ObservedObjects,
                        MappedObjects = diagnostics.MappedObjects,
                        UnknownObjects = diagnostics.UnknownObjects,
                        UnknownRawcodes = diagnostics.UnknownRawcodes,
                        Detail = diagnostics.Detail + $" · 카탈로그 일치율 {catalogRatio:P0} < {profile.MinimumCatalogMatchRatio:P0}"
                    }
                };
            var suffix = mapped.UnknownCount > 0 ? $" · 미등록 {mapped.UnknownCount}" : "";
            var timerBoundary = _currentRoundReader.SessionChanged;
            if (timerBoundary) _mapSignalTracker.Reset();
            var story13Completed = !timerBoundary &&
                _mapSignalTracker.LastGood.CompletedStoryStageOrdinal < 13 && _storyCompletionReader.Read(
                memory.ReadAvailable, moduleBase, module.ModuleMemorySize, version,
                _mapSignalProfile.MapScriptSha256,
                localPlayerConfirmed ? measuredSlot : null, token) == 13;
            var mapSignals = _mapSignalTracker.Observe(snapshot.MapSignalSnapshot with
                { Story13CompletionObserved = story13Completed });
            var confirmedReadyBoundary = timerBoundary || _mapSignalTracker.LastObservationConfirmedReset;
            if (confirmedReadyBoundary)
                ResetSessionCaches(force: true, clearMapSignals: false);
            mapSignals = mapSignals with { RouteQuests = _routeQuestReader.Read(memory,
                version, _mapSignalProfile.MapScriptSha256,
                localPlayerConfirmed ? measuredSlot : null, token) };
            MarkSessionReady();
            mapSignals = mapSignals with { RouteQuests = mapSignals.RouteQuests with {
                HighGamble = confirmedReadyBoundary ? HighGambleObservation.Unknown : new WarcraftHighGambleReader().Read(
                    memory.ReadAvailable, version, _mapSignalProfile.MapScriptSha256,
                    localPlayerConfirmed ? measuredSlot : null, _routeQuestReader.GlobalAnchor, mapSignals.RouteQuests, token) } };
            mapSignals = mapSignals with { NativeNavigation = confirmedReadyBoundary ? NativeNavigationSnapshot.Unknown :
                new WarcraftNavigationReader().Read(memory.ReadAvailable, version, _mapSignalProfile.MapScriptSha256,
                    localPlayerConfirmed ? measuredSlot : null, _routeQuestReader.GlobalAnchor, token) };
            mapSignals = mapSignals with
            {
                DestructionKingAvailable = confirmedReadyBoundary ? null : _destructionKingReader.Read(
                    memory.ReadAvailable, version, _mapSignalProfile.MapScriptSha256,
                    _routeQuestReader.GlobalAnchor, token)
            };
            var guideRuntime = readGuide
                ? _guideRuntimeReader.Read(memory, version, _mapSignalProfile.MapScriptSha256,
                    localPlayerConfirmed ? measuredSlot : null,
                    mapped.Entries.Any(entry => entry.UnitId == BulletGuidePolicy.GoalId && entry.Count > 0),
                    _routeQuestReader.GlobalAnchor, token) : BulletGuideRuntimeState.Unknown;
            if (readGuide && localPlayerConfirmed && measuredSlot is { } confirmedOwner && guideRuntime.IsCurrent &&
                snapshot.NativeUnits.TryGetValue(0x68303831, out var bullets) && bullets.Count == 1)
            {
                var exact = new BulletInventoryReader(memory.ReadAvailable, moduleBase, module.ModuleMemorySize)
                    .Read(version, _mapSignalProfile.MapScriptSha256, bullets[0], confirmedOwner);
                guideRuntime = guideRuntime.WithExactCounts(exact);
            }
            HelperUnitState? helperState = null;
            var combat = ImmutableArray.CreateBuilder<CombatUnitState>();
            if (readGuide && !confirmedReadyBoundary && localPlayerConfirmed &&
                measuredSlot is { } helperOwner &&
                snapshot.NativeUnits.TryGetValue(0x68303841, out var helpers) && helpers.Count == 1)
                helperState = new WarcraftHelperReader(memory.ReadAvailable, moduleBase, module.ModuleMemorySize)
                    .Read(version, _mapSignalProfile.MapScriptSha256, helpers[0], helperOwner);
            if (readGuide && !confirmedReadyBoundary && localPlayerConfirmed && measuredSlot is { } combatOwner)
            {
                var combatReader = new WarcraftCombatReader(memory.ReadAvailable, moduleBase, module.ModuleMemorySize);
                for (var i = 0; i < snapshot.CombatCandidates.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var candidate = snapshot.CombatCandidates[i];
                    if (combatReader.Read(version, _mapSignalProfile.MapScriptSha256, candidate.Address,
                            candidate.Rawcode, candidate.Owner, combatOwner, i) is { } observed)
                        combat.Add(observed);
                }
            }
            return new RecognitionResult
            {
                GuideRuntime = guideRuntime,
                HelperState = helperState,
                CombatObservations = combat.ToImmutable(),
                NativeUnitPointers = snapshot.NativeUnits,
                VerifiedLocalPlayerSlot = localPlayerConfirmed ? measuredSlot : null,
                Entries = mapped.Entries,
                MapSignals = mapSignals,
                PlayerResources = confirmedReadyBoundary ? null : playerResources,
                GambleCounters = confirmedReadyBoundary ? null : new WarcraftGambleCountersReader().Read(
                    memory.ReadAvailable, version, _mapSignalProfile.MapScriptSha256,
                    localPlayerConfirmed ? measuredSlot : null, _routeQuestReader.GlobalAnchor, token),
                LoadedClearCount = confirmedReadyBoundary ? null : _loadedClearCountReader.Read(
                    memory.ReadAvailable, version, _mapSignalProfile.MapScriptSha256,
                    localPlayerConfirmed ? measuredSlot : null, _routeQuestReader.GlobalAnchor, token),
                State = RecognitionState.Ready,
                ConfirmsSessionBoundary = confirmedReadyBoundary,
                Status = $"확인한 패 {mapped.Entries.Sum(x => x.Count)}장{suffix}" +
                         (profile.Verified ? "" : " · 시험 중인 인식 기능") + $" · {DateTime.Now:HH:mm:ss}",
                Diagnostics = ReadyDiagnostics(diagnostics, confirmedReadyBoundary)
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (PoolNotReadyException exception)
        {
            // 로컬 플레이어가 확인된 대전 중에는 풀 벡터가 조합·라운드 전환 순간
            // 재구성될 수 있다. 이 한 틱을 Waiting으로 보내면 정상 패를 지우므로
            // transient로 유지하고 다음 틱에 locator를 다시 찾는다.
            var state = PoolNotReadyState(localPlayerConfirmed);
            ResetSessionCaches(allowPeriodicRescan: true,
                clearMapSignals: state == RecognitionState.Waiting);
            return Failure(state,
                state == RecognitionState.Waiting
                    ? "대전 준비 중 · 기존 패 유지"
                    : "유닛 목록을 다시 찾는 중 · 기존 패 유지",
                exception.Message);
        }
        catch (SnapshotChangedException)
        {
            // 유닛 생성 중 count/entries가 두 번 연속 바뀌어도 검증된 풀 구조체
            // 주소는 그대로다. locator를 버리면 다음 틱에 7초 전체 힙 스캔이
            // 재실행되어 Warcraft가 끊긴다.
            return Failure(RecognitionState.TransientReadError,
                "유닛 목록 갱신 중 · 기존 패 유지",
                "다음 자동 확인 때 유닛 목록을 다시 읽습니다.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidDataException or InvalidOperationException
                                          or OverflowException or IOException or UnauthorizedAccessException
                                          or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            if (ShouldInvalidateLocator(exception))
                lock (_cacheGate) _locatorCache = null;
            return Failure(RecognitionState.TransientReadError, "게임 정보를 읽지 못했습니다 · 기존 패 유지", exception.Message);
        }
    }

    private RecognitionResult ReadDiagnosticInventory(ReadOnlyProcessMemory memory, ProcessModule module,
        MemoryProfile profile, RecognitionDiagnostics basis, long processStarted, long profileGeneration,
        CancellationToken token, Func<Func<DiagnosticRecognitionFrame>, bool>? tryEmitFrame)
    {
        lock (_diagnosticGrowthGate)
        {
            if (tryEmitFrame is null || !_catalog.HasModernSource)
                return ReadDiagnosticInventoryCore(memory, module, profile, basis, processStarted, profileGeneration, token);
            var sessionKey = DiagnosticSessionKey(basis, processStarted, profile, profileGeneration);
            bool TryPublishBasic() => tryEmitFrame(() =>
                ReadBasicInventory(memory, module, profile, basis, sessionKey, token));
            try
            {
                TryPublishBasic();
                var checkpoint = new DiagnosticDiscoveryCheckpoint(TryPublishBasic);
                return ReadDiagnosticInventoryCore(memory, module, profile, basis, processStarted, profileGeneration, token, checkpoint.Poll);
            }
            catch
            {
                // A basic/world sample failure does not invalidate the independently checked
                // VM owner address. Read resets its own cache on validation failure, and each
                // next invocation rechecks the full context before reusing that address.
                _diagnosticObservationContext = null;
                throw;
            }
        }
    }

    private RecognitionResult ReadDiagnosticInventoryCore(ReadOnlyProcessMemory memory, ProcessModule module,
        MemoryProfile profile, RecognitionDiagnostics basis, long processStarted, long profileGeneration,
        CancellationToken token, Action? discoveryCheckpoint = null)
    {
        const string provenance = "DIAGNOSTIC-ONLY 3.0 CURRENT-VIEW (not proven local identity)";
        var startedAt = DateTimeOffset.UtcNow;
        var startedTick = Stopwatch.GetTimestamp();
        var sourceRevision = checked(++_diagnosticObservationRevision);
        try
        {
            var moduleBase = (ulong)module.BaseAddress.ToInt64();
            var locator = Warcraft300WorldLocator.Read(memory.ReadAvailable, moduleBase, token);
            var root = locator.World;
            Warcraft300Diagnostic.Inventory snapshot;
            GrowthMaterialInventory.Projection projection;
            var growthDetail = "growth source not selected";
            var growthUnitFingerprint = "";
            var roundDetail = "round read not attempted";
            int? observedRound = null;
            Warcraft300ActivityObservation? activity = null;
            string? observationContextKey = null;
            if (_catalog.HasModernSource)
            {
                if (_catalog.SelectedDatasetFingerprint.Length != 64)
                    throw new InvalidDataException("Selected offline source missing.");
                if (_diagnosticGrowthSource?.MapVersion != _selectedMapVersion)
                    _diagnosticGrowthSource = Map2320GrowthSource.LoadBundled(_selectedMapVersion);
                var sessionKey = DiagnosticSessionKey(basis, processStarted, profile, profileGeneration);
                var expectedView = Warcraft300Diagnostic.ReadView(memory.ReadAvailable, moduleBase);
                var growth = _diagnosticGrowthReader.Read(memory.ReadAvailable,
                    () => memory.ReadablePrivateRegionsStrict(token), moduleBase, expectedView, root,
                    sessionKey, _diagnosticGrowthSource.Globals, token,
                    discoveryBlockBytes: Warcraft300GrowthReader.LargeDiscoveryBlockBytes,
                    readDiscoveryBlock: memory.ReadAvailable, discoveryCheckpoint: discoveryCheckpoint, source: _diagnosticGrowthSource,
                    ownerDiscoveryMode: Warcraft300OwnerDiscoveryMode.RevalidateKnownContext,
                    activityArrayNames: ActivityRules?.IntegerArrays);
                if (growth.SampleStartedTimestamp <= 0)
                    throw new InvalidDataException("Growth sample timestamp missing.");
                startedTick = growth.SampleStartedTimestamp;
                byte[] RoundRead(ulong address, int count)
                {
                    ulong cursor = address;
                    foreach (var region in memory.ReadableModuleRegions(address, count, token))
                    {
                        if (region.BaseAddress != cursor) throw new InvalidDataException("Round span is not readable");
                        cursor = checked(cursor + region.Size);
                    }
                    if (cursor != checked(address + (ulong)count)) throw new InvalidDataException("Round span is not readable");
                    return memory.ReadAvailable(address, count);
                }
                var roundReference = Warcraft300ObservedRoundReader.Read(RoundRead, growth, moduleBase,
                    boundedRead => new(sessionKey, moduleBase,
                        Warcraft300Diagnostic.ReadView(boundedRead, moduleBase),
                        Warcraft300WorldLocator.Read(boundedRead, moduleBase, token).World), token);
                observedRound = roundReference.SuccessfulComparison ? roundReference.Round : null;
                if (ActivityRules is not null)
                    activity = Warcraft300ActivityReader.Read(RoundRead, growth,
                        boundedRead => new(sessionKey, moduleBase,
                            Warcraft300Diagnostic.ReadView(boundedRead, moduleBase),
                            Warcraft300WorldLocator.Read(boundedRead, moduleBase, token).World), token);
                roundDetail = $"round reader {roundReference.Status}; source {roundReference.Source}; owner discovery {growth.OwnerDiscoveryMode}; current owner-set uniqueness checked {growth.OwnerSetUniquenessChecked}";
                snapshot = Warcraft300Diagnostic.ReadInventory(memory.ReadAvailable, moduleBase, root, profile, token);
                if (snapshot.CurrentView != growth.CurrentView)
                    throw new InvalidDataException("World inventory view changed across QR attribution.");
                // Hash only already-validated reader context, never expose native pointers/keys.
                // Current-view/game root is NOT immutable local identity. Observed round is reference-only.
                observationContextKey = $"{sessionKey}:{locator}:{snapshot.CurrentView}:{growth.Instance}:{growth.Script}:{growth.MapManager}:{growth.NativeRegistry}:{growth.OwnerAggregate}:{growth.DataTable}:{growth.QrNode}";
                projection = GrowthMaterialInventory.Project(snapshot, growth.CurrentView, growth.UnitPointer,
                    growth.Rawcode, growth.Allocation, _unitMap.IsGrowthUnit);
                if (projection.AddedObjects == 1)
                    growthUnitFingerprint = DiagnosticInventoryBinding.GrowthUnit(_diagnosticObservationSalt,
                        snapshot.CurrentView, snapshot.Units.Single(unit => unit.Address == growth.UnitPointer));
                growthDetail = projection.AddedObjects == 1
                    ? "QR growth counted as its existing SPECIAL card for material completion; not a bonus; empirical diagnostic layout"
                    : "QR explicitly absent or same physical card already owned; no extra growth count";
                growthDetail += $"; {growth.StatusDescription}";
            }
            else
            {
                snapshot = Warcraft300Diagnostic.ReadInventory(memory.ReadAvailable, moduleBase, root, profile, token);
                projection = new GrowthMaterialInventory.Projection(snapshot.Rawcodes, [], 0);
            }
            if (locator != Warcraft300WorldLocator.Read(memory.ReadAvailable, moduleBase, token))
                throw new InvalidDataException("Diagnostic encoded world context changed");
            var mapped = _unitMap.Map(projection.Rawcodes);
            var matches = mapped.KnownCount + mapped.CatalogNamedCount;
            var attributedObjects = snapshot.Owned + projection.AddedObjects;
            var quality = Map2320DiagnosticHelperPolicy.Evaluate(_selectedMapVersion, _catalog.OfflineBundle,
                _diagnosticGrowthSource, projection.Rawcodes, mapped, attributedObjects, _unitMap.IsRecognizedCard, _catalog.MapBundle);
            var accepted = quality.Accepts(profile.MinimumCatalogMatchRatio, profile.RequireNonEmptyInventory);
            var completedAt = DateTimeOffset.UtcNow;
            var duration = Stopwatch.GetElapsedTime(startedTick);
            startedAt = completedAt - duration;
            var growthIds = MapGrowthUnitIds(_unitMap, projection.GrowingRawcodes);
            DiagnosticInventoryObservation observation;
            if (accepted && observationContextKey is not null)
            {
                if (_diagnosticObservationContext != observationContextKey)
                {
                    _diagnosticObservationContext = observationContextKey;
                    _diagnosticObservationEpoch = checked(_diagnosticObservationEpoch + 1);
                }
                var opaqueContext = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"{_diagnosticObservationSalt}:{_diagnosticObservationEpoch}:{observationContextKey}")));
                observation = DiagnosticInventoryObservation.Create(_catalog, basis.ProcessVersion ?? "",
                    basis.ExecutableSha256 ?? "", opaqueContext, sourceRevision, snapshot.CurrentView.Slot,
                    startedAt, completedAt, duration, mapped.Entries, growthIds, observedRound,
                    DiagnosticInventoryBinding.World(_diagnosticObservationSalt, snapshot),
                    DiagnosticInventoryBinding.Context(_diagnosticObservationSalt,
                        DiagnosticSessionKey(basis, processStarted, profile, profileGeneration), locator, snapshot.CurrentView),
                    growthUnitFingerprint);
            }
            else observation = DiagnosticInventoryObservation.Unavailable(_selectedMapVersion,
                _catalog.SelectedDatasetFingerprint, basis.ProcessVersion ?? "", basis.ExecutableSha256 ?? "",
                sourceRevision, startedAt, completedAt, duration, "Diagnostic mapping or selected source unavailable");
            var state = DiagnosticObservationState(accepted, observation);
            if (observation.Availability != DiagnosticInventoryAvailability.Ready)
                _diagnosticObservationContext = null; // No game-end claim; native growth cache stays untouched.
            return new RecognitionResult
            {
                DiagnosticObservation = observation,
                CompletionBasicSample = state == RecognitionState.Ready && observationContextKey is not null
                    ? CreateBasicInventorySample(snapshot, locator, profile, basis,
                        DiagnosticSessionKey(basis, processStarted, profile, profileGeneration),
                        checked(++_diagnosticBasicRevision), startedAt, completedAt, duration)
                    : null,
                State = state,
                Status = $"{provenance} · round {(observedRound?.ToString() ?? "?")} · slot {snapshot.CurrentView.Slot} · mapped {matches}",
                Entries = state == RecognitionState.Ready ? mapped.Entries : [],
                // No VerifiedLocalPlayerSlot, native pointers, gameplay Round, map or session-boundary claims.
                Diagnostics = new RecognitionDiagnostics
                {
                    Source = "WarcraftMemoryDiagnostic300CurrentView",
                    ActivityRawcodes = snapshot.Rawcodes.ToImmutableDictionary(
                        pair => RawcodeCodec.Format(pair.Key), pair => pair.Value, StringComparer.Ordinal),
                    ActivityProjectedRawcodes = projection.Rawcodes.ToImmutableDictionary(
                        pair => RawcodeCodec.Format(pair.Key), pair => pair.Value, StringComparer.Ordinal),
                    ActivityCounters = activity?.Values,
                    ActivityUnavailableCounterNames = activity?.UnavailableNames ?? [],
                    ActivityCounterStatus = activity?.Status ?? "not-requested",
                    ActivityCounterReadCalls = activity?.ReadCalls ?? 0,
                    ActivityCounterReadBytes = activity?.ReadBytes ?? 0,
                    ActivityCounterReadDurationMs = activity?.DurationMs ?? 0,
                    ProcessId = basis.ProcessId, ProcessVersion = basis.ProcessVersion ?? "",
                    ExecutableSha256 = basis.ExecutableSha256 ?? "", ProfileId = profile.ProfileId,
                    ProfileRevision = profile.ProfileRevision, ProfileSource = basis.ProfileSource,
                    ResolvedListAddress = $"0x{root:X}", ObservedObjects = attributedObjects,
                    ForeignObjects = snapshot.Foreign - projection.AddedObjects, MappedObjects = matches,
                    UnknownObjects = mapped.UnknownCount, UnknownRawcodes = mapped.UnknownRawcodes,
                    GrowthUnitIds = growthIds,
                    ExcludedSourceHelperObjects = quality.ExcludedSourceHelperObjects,
                    EligibleObjects = quality.EligibleObjects, EligibleMappedObjects = quality.EligibleMappedObjects,
                    EligibleUnknownObjects = quality.EligibleUnknownObjects,
                    Detail = $"{provenance}; raw quality {matches}/{attributedObjects}; excluded source helpers {quality.ExcludedSourceHelperObjects}; eligible quality {quality.EligibleMappedObjects}/{quality.EligibleObjects}; eligible unknown {quality.EligibleUnknownObjects}; helper source conflict {quality.SourceConflict}; minimum ratio {profile.MinimumCatalogMatchRatio}; typed frame slots {snapshot.Count}; excluded unregistered slots {snapshot.UnregisteredEntries.Count}; bounded native world locator; exact stable vector and allocated handle/serial stamps; owner DWORD 0..27; alive/local identity unproven; {growthDetail}; observed round {observedRound?.ToString() ?? "?"}; {roundDetail}; other optional readers disabled; no authoritative map/gameplay-round"
                }
            };
        }
        catch (Exception e) when (e is InvalidDataException or IOException or Win32Exception or OverflowException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Keep only the owner address across unrelated inventory/round read failures.
            // Growth Read already clears it on its own failure; no sampled values are reused.
            _diagnosticObservationContext = null;
            return new RecognitionResult
            {
                DiagnosticObservation = DiagnosticInventoryObservation.Unavailable(_selectedMapVersion,
                    _catalog.SelectedDatasetFingerprint, basis.ProcessVersion ?? "", basis.ExecutableSha256 ?? "",
                    sourceRevision, startedAt, DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(startedTick),
                    "Diagnostic native validation/read failed"),
                State = RecognitionState.TransientReadError, Status = provenance + " · rejected",
                Diagnostics = new RecognitionDiagnostics
                {
                    Source = "WarcraftMemoryDiagnostic300CurrentView", ProcessId = basis.ProcessId,
                    ProcessVersion = basis.ProcessVersion ?? "", ExecutableSha256 = basis.ExecutableSha256 ?? "",
                    ProfileId = profile.ProfileId, ProfileRevision = profile.ProfileRevision,
                    ProfileSource = basis.ProfileSource, Detail = e.Message
                }
            };
        }
        catch (OperationCanceledException)
        {
            _diagnosticObservationContext = null;
            throw; // Cancellation is not an observation or a game-end event.
        }
    }

    /// <summary>
    /// 한 판이 끝나면 같은 Warcraft 프로세스라도 다음 판의 유닛 풀 주소가 달라질 수 있다.
    /// 이전 판 locator/map-state 캐시를 그대로 재사용하면 다음 판에서 옛 패를 읽을 수 있으므로
    /// 확정된 대기/세션 경계에서는 반드시 둘 다 비운다.
    /// </summary>

    private void ResetSessionCaches(bool allowPeriodicRescan = false, bool force = false,
        bool clearMapSignals = true)
    {
        var now = DateTimeOffset.UtcNow;
        if (clearMapSignals) _mapSignalTracker?.Reset();
        lock (_diagnosticGrowthGate)
        {
            _diagnosticGrowthReader.Reset();
            _diagnosticObservationContext = null;
        }
        _routeQuestReader.Reset();
        _guideRuntimeReader.Reset();
        _playerResourceReader.Reset();
        _loadedClearCountReader.Reset();
        _destructionKingReader.Reset();
        if (!force && _sessionBoundaryCachesCleared &&
            (!allowPeriodicRescan || now < _nextWaitingLocatorRescanAt))
            return;

        lock (_cacheGate) _locatorCache = null;
        _lastMapState = null;
        _lastMapStateAt = DateTimeOffset.MinValue;
        _mapStateScanner.Reset();
        _currentRoundReader.Reset();
        _storyCompletionReader.Reset();
        _growthPointers.Reset();
        GrowthUnitPointerCacheStore.Delete(GrowthPointerCachePath);
        _growthCacheProcessStarted = long.MinValue;
        _savedGrowthRevision = -1;
        _sessionBoundaryCachesCleared = true;
        _nextWaitingLocatorRescanAt = allowPeriodicRescan
            ? now + WaitingLocatorRescanInterval
            : DateTimeOffset.MinValue;
    }

    internal static RecognitionDiagnostics ReadyDiagnostics(
        RecognitionDiagnostics diagnostics, bool confirmedReadyBoundary) =>
        !confirmedReadyBoundary ? diagnostics : new RecognitionDiagnostics
        {
            Source = diagnostics.Source,
            ProcessVersion = diagnostics.ProcessVersion,
            ExecutableSha256 = diagnostics.ExecutableSha256,
            ProcessId = diagnostics.ProcessId,
            ProfileId = diagnostics.ProfileId,
            ProfileRevision = diagnostics.ProfileRevision,
            ProfileSource = diagnostics.ProfileSource,
            ResolvedListAddress = diagnostics.ResolvedListAddress,
            ObservedObjects = diagnostics.ObservedObjects,
            MappedObjects = diagnostics.MappedObjects,
            UnknownObjects = diagnostics.UnknownObjects,
            Gorosei = diagnostics.Gorosei,
            ForeignObjects = diagnostics.ForeignObjects,
            // Captured before the confirmed reset; it belongs to the previous session.
            MapState = null,
            AdaptivePlanningObservation = diagnostics.AdaptivePlanningObservation,
            UnknownRawcodes = diagnostics.UnknownRawcodes,
            GrowthUnitIds = diagnostics.GrowthUnitIds,
            Detail = diagnostics.Detail
        };

    private void MarkSessionReady()
    {
        _sessionBoundaryCachesCleared = false;
        _nextWaitingLocatorRescanAt = DateTimeOffset.MinValue;
    }

    private void EnsureGrowthPointerCache(long processStarted)
    {
        if (_growthCacheProcessStarted == processStarted) return;
        _growthPointers.Restore(GrowthUnitPointerCacheStore.Load(
            GrowthPointerCachePath, processStarted));
        _growthCacheProcessStarted = processStarted;
        _savedGrowthRevision = _growthPointers.Revision;
    }

    private void SaveGrowthPointerCache(long processStarted)
    {
        if (_savedGrowthRevision == _growthPointers.Revision) return;
        GrowthUnitPointerCacheStore.Save(GrowthPointerCachePath, processStarted,
            _growthPointers.Snapshot());
        _savedGrowthRevision = _growthPointers.Revision;
    }

    private ulong GetLocatorAddress(ReadOnlyProcessMemory memory, Process process, long processStarted,
        ProcessModule module, MemoryProfile profile, long generation, CancellationToken token)
    {
        var key = new LocatorCacheKey(process.Id, processStarted, (ulong)module.BaseAddress.ToInt64(),
            profile.ProfileId, profile.ProfileRevision, generation);
        lock (_cacheGate)
            if (_locatorCache is { } cached && cached.Key == key) return cached.Address;

        var address = ResolveLocator(memory, module, profile, token);
        lock (_cacheGate) _locatorCache = new LocatorCache(key, address);
        return address;
    }

    private static ulong ResolveLocator(ReadOnlyProcessMemory memory, ProcessModule module, MemoryProfile profile,
        CancellationToken token)
    {
        var moduleBase = (ulong)module.BaseAddress.ToInt64();
        if (profile.LocatorKind == MemoryLocatorKind.ModuleOffset)
        {
            if ((ulong)profile.ModuleOffset >= (ulong)module.ModuleMemorySize)
                throw new InvalidDataException("moduleOffset이 대상 모듈 범위를 벗어났습니다.");
            return AddressMath.Add(moduleBase, profile.ModuleOffset);
        }

        if (profile.LocatorKind == MemoryLocatorKind.StructuralScan)
            return StructuralUnitPoolScanner.Resolve(memory, module, profile, token);

        var moduleSize = module.ModuleMemorySize;
        if (moduleSize <= 0 || moduleSize > 512 * 1024 * 1024)
            throw new InvalidDataException($"비정상 모듈 크기: {moduleSize}");
        var bytes = memory.Read(moduleBase, moduleSize);
        var pattern = SignaturePattern.Parse(profile.Signature);
        var matches = pattern.Find(bytes, 2);
        if (matches.Count == 0) throw new InvalidOperationException("현재 빌드의 유닛 목록 서명을 찾지 못했습니다.");
        if (matches.Count > 1) throw new InvalidOperationException("유닛 목록 서명이 고유하지 않아 프로필을 차단했습니다.");

        token.ThrowIfCancellationRequested();
        var instruction = AddressMath.Add(moduleBase, matches[0]);
        var displacementAddress = AddressMath.Add(instruction, profile.RelativeDisplacementOffset);
        var displacement = memory.ReadInt32(displacementAddress);
        return AddressMath.Add(AddressMath.Add(instruction, profile.InstructionLength), displacement);
    }

    private static ulong FollowPointerPath(ReadOnlyProcessMemory memory, ulong address, IReadOnlyList<int> offsets)
    {
        foreach (var offset in offsets)
        {
            var pointer = memory.ReadUInt64(address);
            if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(pointer))
                throw new InvalidDataException($"포인터 체인에 비정상 주소가 있습니다: 0x{pointer:X}");
            address = AddressMath.Add(pointer, offset);
        }
        return address;
    }

    private static MemoryUnitSnapshot ReadConsistentSnapshot(ReadOnlyProcessMemory memory, ulong listAddress,
        MemoryProfile profile, byte localPlayerSlot, Func<uint, bool> isGrowthUnit,
        IReadOnlyDictionary<ulong, uint> trackedGrowthPointers,
        MapSignalRecognitionProfile mapSignalProfile, CancellationToken token, bool readCombat = false)
    {
        var countAddress = AddressMath.Add(listAddress, profile.CountOffset);
        var entriesAddress = AddressMath.Add(listAddress, profile.EntriesPointerOffset);
        var countBefore = memory.ReadInt32(countAddress);
        if (countBefore < 0 || countBefore > profile.MaximumUnits)
            throw new InvalidDataException($"유닛 수 {countBefore}가 허용 범위 0~{profile.MaximumUnits}를 벗어났습니다.");
        var entriesBefore = profile.EntriesAreInline ? entriesAddress : memory.ReadUInt64(entriesAddress);
        if (countBefore > 0 && !ReadOnlyProcessMemory.IsPlausibleUserAddress(entriesBefore))
            throw new InvalidDataException($"유닛 배열 주소가 비정상입니다: 0x{entriesBefore:X}");

        var counts = new Dictionary<uint, int>();
        var nativeUnits = new Dictionary<uint, List<ulong>>();
        var combatCandidates = new List<(ulong Address, uint Rawcode, byte Owner)>();
        var goroseiCandidates = new List<(ulong Address, uint Rawcode)>();
        var neutralGrowth = new Dictionary<uint, int>();
        var locallyObservedGrowth = new Dictionary<ulong, uint>();
        var seenTrackedGrowthPointers = new HashSet<ulong>();
        var retainedGrowthObjects = 0;
        var gorosei = GoroseiMode.None;
        var foreignObjects = 0;
        var seenPointers = new HashSet<ulong>();
        var duplicatePointers = 0;
        var ownedObjects = 0;
        var objectiveRawcodes = new List<uint>();
        var rewardWispRawcodes = new List<uint>();
        long sideChannelByteBudget = 0;
        long sideChannelCallBudget = 0;
        byte[]? entryPointers = null;
        ulong firstEntry = 0;
        if (countBefore > 0 && profile.EntriesContainPointers)
        {
            firstEntry = AddressMath.Add(entriesBefore, profile.EntryPointerOffset);
            var pointerBytes = checked((countBefore - 1) * profile.EntryStride + sizeof(ulong));
            entryPointers = memory.Read(firstEntry, pointerBytes);
        }
        for (var index = 0; index < countBefore; index++)
        {
            token.ThrowIfCancellationRequested();
            var entry = AddressMath.Add(entriesBefore, checked((long)index * profile.EntryStride + profile.EntryPointerOffset));
            var unit = entryPointers is not null
                ? BitConverter.ToUInt64(entryPointers, checked(index * profile.EntryStride))
                : entry;
            if (unit == 0) continue;
            if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(unit))
                throw new InvalidDataException($"비정상 유닛 객체 포인터: 0x{unit:X}");
            if (!seenPointers.Add(unit)) { duplicatePointers++; continue; }

            var ownerAddress = FollowObjectFieldPath(memory, unit, profile.OwnerPointerOffsets, profile.OwnerOffset);
            var owner = memory.ReadByte(ownerAddress);
            var neutral = owner == profile.NeutralPlayerSlot;
            var trackedGrowth = trackedGrowthPointers.TryGetValue(unit, out var trackedRawcode);
            uint rawcode = 0;
            var rawcodeRead = false;
            AdaptivePlanningReadCounters? rawcodeReadBefore = null;
            var ownerFiveCandidate = false;
            var combatOwnerCandidate = readCombat && owner == 6;
            if (MapSignalReadPolicy.ShouldReadRawcode(
                    owner, localPlayerSlot, profile.NeutralPlayerSlot, trackedGrowth) || combatOwnerCandidate)
            {
                rawcodeReadBefore = memory.SnapshotReads();
                ownerFiveCandidate = owner == MapSignalRecognitionProfile.StoryObjectiveOwner;
                using (ownerFiveCandidate || combatOwnerCandidate
                           ? memory.BeginReadChannel(AdaptivePlanningReadChannel.SideChannel)
                           : null)
                {
                    var markerAddress = FollowObjectFieldPath(
                        memory, unit, profile.RawcodePointerOffsets, profile.RawcodeOffset);
                    rawcode = memory.ReadUInt32(markerAddress);
                }
                rawcodeRead = true;
                if (ownerFiveCandidate || combatOwnerCandidate)
                {
                    sideChannelByteBudget = checked(sideChannelByteBudget +
                        profile.RawcodePointerOffsets.Length * sizeof(ulong) + sizeof(uint));
                    sideChannelCallBudget = checked(sideChannelCallBudget +
                        profile.RawcodePointerOffsets.Length + 1);
                }
                if (owner == 7)
                {
                    var detected = GoroseiMemoryDetector.FromRawcode(rawcode);
                    if (detected != GoroseiMode.None) goroseiCandidates.Add((unit, rawcode));
                }
            }
            if (readCombat && rawcodeRead && (owner == localPlayerSlot || owner is 5 or 6 or 7))
                combatCandidates.Add((unit, rawcode, owner));
            var mapRawcode = MapSignalReadPolicy.ToMapRawcode(rawcode);
            var signalKind = rawcodeRead
                ? mapSignalProfile.Classify(owner, localPlayerSlot, mapRawcode)
                : MapSignalKind.None;
            if (signalKind is MapSignalKind.StoryObjective or MapSignalKind.RewardWisp)
            {
                if (!ownerFiveCandidate && rawcodeReadBefore is not null)
                {
                    memory.ReattributeUnitReadsToSideChannel(rawcodeReadBefore);
                    sideChannelByteBudget = checked(sideChannelByteBudget +
                        profile.RawcodePointerOffsets.Length * sizeof(ulong) + sizeof(uint));
                    sideChannelCallBudget = checked(sideChannelCallBudget +
                        profile.RawcodePointerOffsets.Length + 1);
                }
            }
            if (signalKind == MapSignalKind.StoryObjective)
            {
                objectiveRawcodes.Add(mapRawcode);
                continue;
            }
            if (signalKind == MapSignalKind.RewardWisp)
            {
                rewardWispRawcodes.Add(mapRawcode);
                continue;
            }
            if (owner != localPlayerSlot) foreignObjects++;
            if (owner != localPlayerSlot && !neutral && !trackedGrowth) continue;
            if (!rawcodeRead)
            {
                var rawcodeAddress = FollowObjectFieldPath(
                    memory, unit, profile.RawcodePointerOffsets, profile.RawcodeOffset);
                rawcode = memory.ReadUInt32(rawcodeAddress);
            }
            var isGrowth = isGrowthUnit(rawcode);
            if (owner == localPlayerSlot && rawcode is 0x68303831 or 0x68303841 or 0x48304334 or 0x68303853)
            {
                if (!nativeUnits.TryGetValue(rawcode, out var pointers))
                    nativeUnits[rawcode] = pointers = [];
                pointers.Add(unit);
            }
            var retainedGrowth = trackedGrowth && trackedRawcode == rawcode && isGrowth;
            if (retainedGrowth) seenTrackedGrowthPointers.Add(unit);
            if (owner == localPlayerSlot && isGrowth)
            {
                locallyObservedGrowth[unit] = rawcode;
                seenTrackedGrowthPointers.Add(unit);
            }
            if (owner != localPlayerSlot && retainedGrowth)
            {
                ownedObjects++;
                retainedGrowthObjects++;
                counts[rawcode] = counts.GetValueOrDefault(rawcode) + 1;
                continue;
            }
            if (neutral)
            {
                if (isGrowth)
                {
                    var playerColor = memory.ReadByte(
                        AddressMath.Add(unit, UnitPlayerColorOffset));
                    if (GrowthUnitOwnershipPolicy.IsLocalNeutralGrowth(
                            owner, playerColor, localPlayerSlot,
                            profile.NeutralPlayerSlot))
                    {
                        locallyObservedGrowth[unit] = rawcode;
                        seenTrackedGrowthPointers.Add(unit);
                        ownedObjects++;
                        counts[rawcode] = counts.GetValueOrDefault(rawcode) + 1;
                        continue;
                    }
                    neutralGrowth[rawcode] = neutralGrowth.GetValueOrDefault(rawcode) + 1;
                }
                continue;
            }
            ownedObjects++;
            counts[rawcode] = counts.GetValueOrDefault(rawcode) + 1;
        }

        var countAfter = memory.ReadInt32(countAddress);
        var entriesAfter = profile.EntriesAreInline ? entriesAddress : memory.ReadUInt64(entriesAddress);
        if (countAfter != countBefore || entriesAfter != entriesBefore)
            throw new SnapshotChangedException();
        return new MemoryUnitSnapshot(countBefore, ownedObjects, duplicatePointers, counts, neutralGrowth,
            foreignObjects, locallyObservedGrowth, seenTrackedGrowthPointers, retainedGrowthObjects,
            gorosei, new MapSignalRawSnapshot(
                objectiveRawcodes.ToImmutableArray(), rewardWispRawcodes.ToImmutableArray()),
            sideChannelByteBudget, sideChannelCallBudget)
            { NativeUnits = nativeUnits, CombatCandidates = combatCandidates, GoroseiCandidates = goroseiCandidates };
    }

    private static ulong FollowObjectFieldPath(ReadOnlyProcessMemory memory, ulong objectAddress,
        IReadOnlyList<int> pointerOffsets, int fieldOffset)
    {
        var address = objectAddress;
        foreach (var pointerOffset in pointerOffsets)
        {
            address = memory.ReadUInt64(AddressMath.Add(address, pointerOffset));
            if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(address))
                throw new InvalidDataException($"객체 필드 포인터가 비정상입니다: 0x{address:X}");
        }
        return AddressMath.Add(address, fieldOffset);
    }

    internal static Process? FindNewestProcess(string name)
    {
        var processes = Process.GetProcessesByName(name);
        Process? selected = null;
        long selectedStart = long.MinValue;
        foreach (var process in processes)
        {
            try
            {
                var start = process.StartTime.ToUniversalTime().Ticks;
                if (start <= selectedStart) continue;
                selected = process;
                selectedStart = start;
            }
            catch { process.Dispose(); }
        }
        foreach (var process in processes)
            if (!ReferenceEquals(process, selected)) process.Dispose();
        return selected;
    }

    /// <summary>매 틱 고정 예산만큼 맵 상태 메모리를 순환하고 누적값을 돌려준다.</summary>
    private MapStateSample? ReadMapStateThrottled(ReadOnlyProcessMemory memory, string version,
        string mapHash, byte? owner, ulong moduleBase, int moduleSize, CancellationToken token)
    {
        using var observation = memory.BeginReadChannel(AdaptivePlanningReadChannel.MapState);
        // Ownership is checked every recognition, even when the heap slice is throttled.
        // Otherwise a cached round could leak across a same-process new match.
        var round = _currentRoundReader.Read(memory.ReadAvailable, moduleBase, moduleSize,
            version, mapHash, owner, token);
        _lastMapState = MapStateReader.SelectCurrentRound(
            _lastMapState ?? new MapStateSample(0, 0, "unknown"), round);
        var now = DateTimeOffset.UtcNow;
        if (_lastMapState is not null && now - _lastMapStateAt < MapStateSliceInterval)
            return _lastMapState;
        _lastMapStateAt = now;
        try
        {
            var endgame = _lastMapState is { MaxRound: >= 60 };
            int? budget = endgame
                ? MapStateEndgameBudgetBytes
                : null;
            var heap = _mapStateScanner.ScanStep(memory, token, budget, hotRescanEverySteps: 4);
            _lastMapState = MapStateReader.SelectCurrentRound(heap, round);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // 맵 상태는 부가 정보다 — 못 읽어도 패 인식을 실패시키지 않는다.
        }
        return _lastMapState;
    }

    private RecognitionResult Failure(RecognitionState state, string status, string detail,
        RecognitionDiagnostics? diagnostics = null, string profileSource = "") => new()
    {
        State = state,
        Status = status,
        // 로비·워크 종료·맵 로딩(Waiting)은 이전 판이 끝난 것이다. 안 켜면 징베 초월
        // 같은 자동시작 목표가 다음 판까지 남고, 희귀함 추천이 다시 안 열린다.
        ConfirmsSessionBoundary = state == RecognitionState.Waiting,
        MapSignals = _mapSignalTracker?.RetainOnTransient() ?? MapSignals.Empty,
        Diagnostics = diagnostics is null
            ? new RecognitionDiagnostics { Source = "WarcraftMemory", ProfileSource = profileSource, Detail = detail }
            : new RecognitionDiagnostics
            {
                Source = diagnostics.Source,
                ProcessVersion = diagnostics.ProcessVersion,
                ExecutableSha256 = diagnostics.ExecutableSha256,
                ProcessId = diagnostics.ProcessId,
                ProfileId = diagnostics.ProfileId,
                ProfileRevision = diagnostics.ProfileRevision,
                ProfileSource = diagnostics.ProfileSource,
                Gorosei = diagnostics.Gorosei,
                Detail = detail
            }
    };

    internal static RecognitionState DiagnosticObservationState(
        bool qualityAccepted, DiagnosticInventoryObservation observation) =>
        qualityAccepted && observation.Availability == DiagnosticInventoryAvailability.Ready
            ? RecognitionState.Ready
            : RecognitionState.TransientReadError;

    internal static RecognitionState PoolNotReadyState(bool localPlayerConfirmed) =>
        localPlayerConfirmed
            ? RecognitionState.TransientReadError
            : RecognitionState.Waiting;

    internal static bool ShouldInvalidateLocator(Exception exception) =>
        exception is not SnapshotChangedException;

    internal static List<string> MapGrowthUnitIds(
        RawcodeUnitMap unitMap, IEnumerable<uint> rawcodes) =>
        unitMap.Map(rawcodes
                .Distinct()
                .ToDictionary(rawcode => rawcode, _ => 1))
            .Entries
            .Select(entry => entry.UnitId)
            .ToList();

    private static RecognitionDiagnostics WithProfile(RecognitionDiagnostics source, MemoryProfile profile) => new()
    {
        Source = source.Source,
        ProcessId = source.ProcessId,
        ProcessVersion = source.ProcessVersion,
        ExecutableSha256 = source.ExecutableSha256,
        ProfileSource = source.ProfileSource,
        ProfileId = profile.ProfileId,
        ProfileRevision = profile.ProfileRevision
    };

    private sealed record LocatorCache(LocatorCacheKey Key, ulong Address);
    private sealed record LocatorCacheKey(int ProcessId, long Started, ulong ModuleBase, string ProfileId,
        int Revision, long ProfileGeneration);
    private sealed record MemoryUnitSnapshot(int ListCount, int OwnedObjects, int DuplicatePointers,
        Dictionary<uint, int> RawcodeCounts, Dictionary<uint, int> NeutralGrowthCounts, int ForeignObjects,
        Dictionary<ulong, uint> LocallyObservedGrowth, HashSet<ulong> SeenTrackedGrowthPointers,
        int RetainedGrowthObjects, GoroseiMode Gorosei, MapSignalRawSnapshot MapSignalSnapshot,
        long SideChannelByteBudget, long SideChannelCallBudget)
    {
        public IReadOnlyDictionary<uint, List<ulong>> NativeUnits { get; init; } =
            new Dictionary<uint, List<ulong>>();
        public IReadOnlyList<(ulong Address, uint Rawcode, byte Owner)> CombatCandidates { get; init; } = [];
        public IReadOnlyList<(ulong Address, uint Rawcode)> GoroseiCandidates { get; init; } = [];
    }
    internal sealed class SnapshotChangedException : InvalidOperationException;
}

internal static class AddressMath
{
    public static ulong Add(ulong address, long offset)
    {
        if (offset >= 0) return checked(address + (ulong)offset);
        return checked(address - (ulong)(-offset));
    }
}

internal sealed class SignaturePattern(byte?[] bytes)
{
    public static SignaturePattern Parse(string text) => new(text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x is "?" or "??" ? (byte?)null : Convert.ToByte(x, 16)).ToArray());

    public List<int> Find(byte[] source, int maximumMatches = int.MaxValue)
    {
        var result = new List<int>();
        for (var start = 0; start <= source.Length - bytes.Length; start++)
        {
            var found = true;
            for (var index = 0; index < bytes.Length; index++)
                if (bytes[index] is byte expected && source[start + index] != expected) { found = false; break; }
            if (!found) continue;
            result.Add(start);
            if (result.Count >= maximumMatches) break;
        }
        return result;
    }
}

internal sealed class ReadOnlyProcessMemory : IDisposable
{
    private const uint ProcessVmRead = 0x0010;
    // VirtualQueryEx requires QUERY_INFORMATION; neither access right permits writes.
    private const uint ProcessQueryInformation = 0x0400;
    private readonly SafeProcessHandle _handle;
    private readonly AdaptivePlanningReadObserver _readObserver = new();

    private ReadOnlyProcessMemory(SafeProcessHandle handle) => _handle = handle;

    public static ReadOnlyProcessMemory Open(int processId)
    {
        var handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, processId);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "프로세스를 읽기 전용으로 열 수 없습니다.");
        return new ReadOnlyProcessMemory(handle);
    }

    public static bool IsPlausibleUserAddress(ulong address) => address is >= 0x10000 and <= 0x00007FFFFFFFFFFF;

    public byte[] Read(ulong address, int count)
    {
        if (count < 0 || count > 512 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(count));
        if (count > 0 && !IsPlausibleUserAddress(address)) throw new InvalidDataException($"비정상 읽기 주소: 0x{address:X}");
        var bytes = new byte[count];
        var started = Stopwatch.GetTimestamp();
        var succeeded = ReadProcessMemory(_handle, (nint)address, bytes, count, out var read);
        var actual = Math.Clamp(read.ToInt64(), 0, count);
        _readObserver.Record(actual, Stopwatch.GetElapsedTime(started));
        if (!succeeded || actual != count)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"메모리 읽기 실패: 0x{address:X}");
        return bytes;
    }

    public byte[] ReadAvailable(ulong address, int count)
    {
        if (count <= 0) return [];
        if (count > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(count));
        if (!IsPlausibleUserAddress(address)) return [];
        var bytes = new byte[count];
        var actual = ReadAvailable(address, bytes, count);
        if (actual == count) return bytes;
        Array.Resize(ref bytes, actual);
        return bytes;
    }

    public int ReadAvailable(ulong address, byte[] buffer, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (count < 0 || count > buffer.Length || count > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0 || !IsPlausibleUserAddress(address)) return 0;
        var started = Stopwatch.GetTimestamp();
        ReadProcessMemory(_handle, (nint)address, buffer, count, out var read);
        var actual = (int)Math.Clamp(read.ToInt64(), 0, count);
        _readObserver.Record(actual, Stopwatch.GetElapsedTime(started));
        return actual;
    }

    /// <summary>
    /// 구조 스캔이 훑을 영역. 유닛 객체와 풀 배열은 모두 힙에 있으므로 전용(private) 커밋 영역만 본다.
    /// 매핑된 이미지·데이터 파일을 빼면 훑을 양이 크게 줄어든다.
    /// </summary>
    public IEnumerable<MemoryRegion> ReadableRegions() => ReadablePrivateRegions();

    /// <summary>
    /// 영역을 겹침 있는 청크로 나눠 읽는다. 일부 페이지를 못 읽어도 영역 전체를 버리지 않는다.
    /// 겹침은 구조체가 청크 경계에 걸려 누락되는 것을 막는다.
    /// </summary>
    public IEnumerable<(ulong Base, byte[] Buffer)> ReadChunks(IEnumerable<MemoryRegion> regions,
        int chunkBytes = 16 * 1024 * 1024, int overlap = 0x2000)
    {
        foreach (var region in regions)
        {
            ulong position = 0;
            while (position < region.Size)
            {
                var length = (int)Math.Min((ulong)chunkBytes, region.Size - position);
                var address = region.BaseAddress + position;
                var buffer = ReadAvailable(address, length);
                if (buffer.Length >= 0x1000) yield return (address, buffer);
                position += (ulong)Math.Max(length - overlap, 0x1000);
            }
        }
    }

    public int ReadInto(ulong address, byte[] buffer, int count)
    {
        if (count < 0 || count > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        var started = Stopwatch.GetTimestamp();
        var succeeded = ReadProcessMemory(_handle, (nint)address, buffer, count, out var read);
        var actual = Math.Clamp(read.ToInt64(), 0, count);
        _readObserver.Record(actual, Stopwatch.GetElapsedTime(started));
        return succeeded || actual > 0 ? (int)actual : 0;
    }

    public IEnumerable<MemoryRegion> ReadablePrivateRegions()
    {
        ulong address = 0x10000;
        while (address < 0x00007FFFFFFFFFFF)
        {
            var queried = VirtualQueryEx(_handle, (nint)address, out var info, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
            if (queried == 0) yield break;
            var baseAddress = (ulong)info.BaseAddress.ToInt64();
            var size = info.RegionSize.ToUInt64();
            if (size == 0) yield break;
            if (info.State == 0x1000 && info.Type == 0x20000 && IsReadable(info.Protect) && IsPlausibleUserAddress(baseAddress))
                yield return new MemoryRegion(baseAddress, size);
            var next = baseAddress + size;
            if (next <= address) yield break;
            address = next;
        }
    }

    internal IEnumerable<MemoryRegion> ReadablePrivateRegionsStrict(CancellationToken token) =>
        ReadOnlyPrivateRegionScan.Enumerate(address =>
        {
            var queried = VirtualQueryEx(_handle, (nint)address, out var info,
                (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
            return queried == 0 ? null : new ModuleRegionInfo((ulong)info.BaseAddress.ToInt64(),
                info.RegionSize.ToUInt64(), info.State, info.Protect, info.Type);
        }, token);

    // Unlike the heap sweep, module reads include committed image/mapped/private pages,
    // but never query or read outside this exact module. No protection changes are made.
    internal IEnumerable<MemoryRegion> ReadableModuleRegions(ulong moduleBase, int moduleSize,
        CancellationToken token) => EnumerateReadableModuleRegions(moduleBase, moduleSize, address =>
        {
            var queried = VirtualQueryEx(_handle, (nint)address, out var info,
                (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
            return queried == 0 ? null : new ModuleRegionInfo((ulong)info.BaseAddress.ToInt64(),
                info.RegionSize.ToUInt64(), info.State, info.Protect, info.Type);
        }, token);

    internal readonly record struct ModuleRegionInfo(ulong BaseAddress, ulong Size, uint State,
        uint Protect, uint Type);

    internal static void ValidateModuleBounds(ulong moduleBase, int moduleSize)
    {
        if (moduleSize <= 0 || moduleSize > 512 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(moduleSize));
        if (!IsPlausibleUserAddress(moduleBase) ||
            (ulong)moduleSize - 1 > 0x00007FFFFFFFFFFF - moduleBase)
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
    }

    // The query delegate makes gaps, protections and malformed query results testable offline.
    internal static IEnumerable<MemoryRegion> EnumerateReadableModuleRegions(ulong moduleBase,
        int moduleSize, Func<ulong, ModuleRegionInfo?> query, CancellationToken token = default)
    {
        ValidateModuleBounds(moduleBase, moduleSize);
        var end = moduleBase + (ulong)moduleSize;
        var address = moduleBase;
        var queries = 0;
        while (address < end)
        {
            token.ThrowIfCancellationRequested();
            if (++queries > 131074)
                throw new InvalidDataException("Module region query budget exceeded.");
            var result = query(address);
            if (result is not { } info)
                throw new InvalidDataException($"Module region query failed: 0x{address:X}");
            if (info.Size == 0 || info.BaseAddress > address ||
                info.Size > ulong.MaxValue - info.BaseAddress ||
                info.BaseAddress + info.Size <= address)
                throw new InvalidDataException("Invalid module region bounds.");
            var next = Math.Min(end, info.BaseAddress + info.Size);
            if (info.State == 0x1000 && info.Type is 0x1000000 or 0x20000 or 0x40000 &&
                IsReadable(info.Protect))
                yield return new MemoryRegion(address, next - address);
            address = next;
        }
    }

    private static bool IsReadable(uint protect)
    {
        if ((protect & 0x100) != 0 || (protect & 0x01) != 0) return false;
        return (protect & 0xFF) is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
    }

    public byte ReadByte(ulong address) => Read(address, 1)[0];
    public int ReadInt32(ulong address) => BitConverter.ToInt32(Read(address, 4));
    public uint ReadUInt32(ulong address) => BitConverter.ToUInt32(Read(address, 4));
    public ulong ReadUInt64(ulong address) => BitConverter.ToUInt64(Read(address, 8));
    internal IDisposable BeginReadChannel(AdaptivePlanningReadChannel channel) =>
        _readObserver.Begin(channel);
    internal AdaptivePlanningReadCounters SnapshotReads() => _readObserver.Snapshot();
    internal void ReattributeUnitReadsToSideChannel(AdaptivePlanningReadCounters before) =>
        _readObserver.ReattributeUnitDelta(before);
    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint baseAddress, byte[] buffer, int size, out nint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQueryEx(SafeProcessHandle process, nint address,
        out MEMORY_BASIC_INFORMATION buffer, nuint length);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }
}

internal readonly record struct MemoryRegion(ulong BaseAddress, ulong Size);
