using Xunit;

namespace OrandOverlay.Tests;

public sealed class ExecutionContextBehaviorTests
{
    [Fact]
    public async Task ActivityLog_UsesExecutableDirectoryWhenBundleExtractionDirectoryDiffers()
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-activity-path-" + Guid.NewGuid().ToString("N"));
        var userRoot = Path.Combine(root, "user");
        var executableDirectory = Path.Combine(root, "publish");
        var executablePath = Path.Combine(executableDirectory, "RandyPick.exe");
        try
        {
            var factory = typeof(OverlayExecutionContext).GetMethods(
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .SingleOrDefault(method => method.Name == "Production" &&
                    method.GetParameters().Length == 2);
            Assert.NotNull(factory);
            var context = Assert.IsType<OverlayExecutionContext>(
                factory!.Invoke(null, [userRoot, executablePath]));
            Assert.True(context.EnsureConsent(() => true));

            var property = typeof(OverlayExecutionContext).GetProperty("ActivityLogDirectory",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(property);
            var activityDirectory = Assert.IsType<string>(property!.GetValue(context));
            Assert.Equal(Path.Combine(executableDirectory, "activity-logs"), activityDirectory);
            Assert.NotEqual(Path.Combine(AppContext.BaseDirectory, "activity-logs"), activityDirectory);

            var log = context.CreateActivityLog();
            Assert.NotNull(log);
            await using (log!)
            {
                Assert.True(log.TryRecord("path-proof", new { Value = 1 }));
                await log.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.StartsWith(activityDirectory + Path.DirectorySeparatorChar, log.CurrentPath!,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ActivityLog_FixtureInjectionNeverEvaluatesAnExecutableDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-activity-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var injected = new LocalActivityLog(root);
            var context = OverlayExecutionContext.FixtureWithActivityLog(new AppSettings(), injected);
            var property = typeof(OverlayExecutionContext).GetProperty("ActivityLogDirectory",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(property);
            Assert.Null(property!.GetValue(context));
            Assert.Same(injected, context.CreateActivityLog());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ActivityLog_ProductionWithoutExecutablePathFailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-activity-no-exe-" + Guid.NewGuid().ToString("N"));
        try
        {
            var factory = typeof(OverlayExecutionContext).GetMethods(
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Single(method => method.Name == "Production" && method.GetParameters().Length == 2);
            var context = Assert.IsType<OverlayExecutionContext>(factory.Invoke(null, [root, null]));
            Assert.True(context.EnsureConsent(() => true));

            var property = typeof(OverlayExecutionContext).GetProperty("ActivityLogDirectory",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(property);
            Assert.Null(property!.GetValue(context));
            Assert.Null(context.CreateActivityLog());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProfileRefresh_UsesInjectedFetchAndOwnedCacheAndStamp()
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bytes = MemoryProfileRefreshFixture.ReadVerifiedLegacyBytes();
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.True(MemoryProfileRefreshService.TryValidate(json));
            using var key = new TestSigningKey();
            var envelope = key.Sign(TestSigningKey.Payload("memory-profiles", bytes));
            var calls = 0;
            Task<string> Fetch(CancellationToken _) { calls++; return Task.FromResult(envelope); }
            Task<byte[]> FetchAsset(string url, CancellationToken _)
            {
                Assert.Equal(SignedUpdateManifest.Origin + "/profiles/9.9.9/memory-profiles.json", url);
                return Task.FromResult(bytes);
            }
            await MemoryProfileRefreshService.TryRefreshAsync(root, fetch: Fetch, fetchAsset: FetchAsset, trust: key.Trust);
            Assert.Equal(json, File.ReadAllText(Path.Combine(root, "memory-profiles.json")));
            Assert.True(File.Exists(Path.Combine(root, "memory-profiles.cloudflare.last-check")));
            Assert.False(File.Exists(Path.Combine(root, "memory-profiles.json.tmp")));
            var loaded = new MemoryProfileRepository(Path.Combine(root, "memory-profiles.json")).GetProfiles();
            Assert.Null(loaded.Error);
            Assert.StartsWith("사용자:", loaded.Source);
            await MemoryProfileRefreshService.TryRefreshAsync(root, fetch: Fetch, fetchAsset: FetchAsset, trust: key.Trust);
            Assert.Equal(1, calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Catalog_ProductionOverrideIsExplicitAndFixtureRemainsBundled()
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "game-data.demo.json")))!;
            json["dataVersion"] = "owned-override";
            File.WriteAllText(Path.Combine(root, "game-data.json"), json.ToJsonString());
            var execution = OverlayExecutionContext.Production(root);
            Assert.True(execution.EnsureConsent(() => true));
            var catalog = execution.CreateCatalog();
            catalog.Load(loadCarryPolicy: false);
            Assert.Equal("owned-override", catalog.Data.DataVersion);
            var fixture = OverlayExecutionContext.Fixture(new AppSettings()).CreateCatalog();
            fixture.Load(loadCarryPolicy: false);
            Assert.NotEqual("owned-override", fixture.Data.DataVersion);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void UpdateLogPath_IsExplicitlyOwnedByProductionAdapter()
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-update-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            var execution = OverlayExecutionContext.Production(root);
            Assert.True(execution.EnsureConsent(() => true));
            var updater = execution.CreateUpdateService();
            var property = typeof(UpdateService).GetProperty("UpdateLogPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(property);
            Assert.Equal(Path.Combine(root, "update.log"), property!.GetValue(updater));
            Assert.Single(Directory.GetFiles(root)); // Only explicit consent was persisted.
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ClearCacheWriter_CreatesItsOwnedParentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-clear-writer-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "clear.json");
        try
        {
            ClearSnapshotRefreshService.MergeIntoCache(path,
                [new ClearSample("fixture", DateTimeOffset.UnixEpoch, "fixture", 0, [])], "fixture");
            Assert.True(File.Exists(path), "Cache writer must create its own directory; getters no longer do so.");
            Assert.Equal("fixture", Assert.Single(ClearSampleDocument.Parse(File.ReadAllText(path))).Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void FixtureTelemetry_DoesNotRetainAnHttpCapability()
    {
        var uploader = OverlayExecutionContext.Fixture(new AppSettings()).CreateTelemetry(true);
        var field = typeof(TelemetryUploader).GetField("_http", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Null(field!.GetValue(uploader));
    }

    [Fact]
    public void SettingsWriter_CreatesOwnedDirectoryWithoutPathGetterSideEffects()
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-settings-writer-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "settings.json");
        try
        {
            var method = typeof(SettingsStore).GetMethod("SaveEnsuringDirectory", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (method is null) SettingsStore.Save(new AppSettings { GoalUnitId = "saved" }, path);
            else method.Invoke(null, [new AppSettings { GoalUnitId = "saved" }, path]);
            Assert.Equal("saved", SettingsStore.Load(path).GoalUnitId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FixtureMemoryJournal_IsOptInAndDoesNotCreateReplayFiles()
    {
        var sentinel = Path.Combine(Path.GetTempPath(), "orand-journal-sentinel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sentinel);
        try
        {
            Assert.Null(OverlayExecutionContext.Fixture(new AppSettings()).CreateCoachJournal());
            var context = OverlayExecutionContext.FixtureWithMemoryJournal(new AppSettings());
            var journal = context.CreateCoachJournal();
            Assert.NotNull(journal);
            Assert.False(journal.IsPersistent);
            var action = new CoachDecision(CoachActionKind.Craft, "craft:goal", "목표 제작", "", "관측", "", "");
            Assert.True(await journal.RecordAsync(new CoachFrame
            {
                MatchGeneration = 1, Revision = 1, Round = 20, CompletedStoryStage = 9,
                IsCurrent = true, Inventory = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty
            }, action));
            var review = CoachReview.Read(journal);
            Assert.False(review.Incomplete);
            Assert.Equal(20, review.LastRound);
            Assert.Null(journal.LatestPath);
            Assert.Empty(Directory.GetFileSystemEntries(sentinel));
        }
        finally { Directory.Delete(sentinel, true); }
    }

    [Fact]
    public async Task FixtureMemoryJournals_AreIsolatedPerContext()
    {
        var left = OverlayExecutionContext.FixtureWithMemoryJournal(new AppSettings());
        var right = OverlayExecutionContext.FixtureWithMemoryJournal(new AppSettings());
        var leftJournal = left.CreateCoachJournal();
        var rightJournal = right.CreateCoachJournal();
        Assert.NotNull(leftJournal);
        Assert.NotNull(rightJournal);
        Assert.Same(leftJournal, left.CreateCoachJournal());
        Assert.NotSame(leftJournal, rightJournal);
        Assert.False(leftJournal.IsPersistent);
        Assert.False(rightJournal.IsPersistent);
        var action = new CoachDecision(CoachActionKind.Finished, "end", "종료", "", "관측", "", "");
        Assert.True(await leftJournal.RecordAsync(new CoachFrame
        {
            MatchGeneration = 1, Revision = 1, Round = 55, CompletedStoryStage = 13,
            IsCurrent = true, Outcome = "fail",
            Inventory = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty
        }, action));
        Assert.True(leftJournal.HasLatest);
        Assert.False(rightJournal.HasLatest);
        Assert.Equal("fail", CoachReview.Read(leftJournal).Outcome);
        Assert.Equal("unknown", CoachReview.Read(rightJournal).Outcome);
        Assert.Empty(CoachReview.Read(rightJournal).Entries);
    }

    [Fact]
    public async Task FixtureComposition_HasNoPersistentOrRuntimeCapabilities()
    {
        var calls = 0;
        var context = OverlayExecutionContext.Fixture(new AppSettings { TelemetryEnabled = true });
        Assert.False(context.RuntimeEnabled);
        context.RunRuntime(() => { calls++; throw new Exception("IPC/network/hotkey must not run"); });
        Assert.False(context.TryRuntime(() => { calls++; return true; }));
        var catalog = context.CreateCatalog();
        catalog.Load(loadCarryPolicy: false);
        Assert.NotEmpty(catalog.AllUnits);
        Assert.Single(context.ClearSamplePaths());
        Assert.Null(context.CreateCoachJournal());
        Assert.Null(context.CreateUpdateService());
        Assert.Null(context.CreateClearRefreshService());
        await context.RefreshProfilesAsync();
        var updates = new AppUpdateCoordinator(context, context.LoadSettings(),
            () => throw new Exception("Fixture must not evaluate update policy"),
            _ => throw new Exception("Fixture must not notify updates"),
            (_, _) => throw new Exception("Fixture must not install updates"));
        await updates.RunStartupAsync();
        await updates.CheckForUpdateAsync();
        var reader = context.CreateRecognizer(catalog);
        Assert.IsNotType<WarcraftMemoryRecognitionService>(reader);
        Assert.Equal(RecognitionState.Waiting,
            (await reader.RecognizeAsync(context.LoadSettings(), default)).State);
        var telemetry = context.CreateTelemetry(true);
        telemetry.SetEnabled(true);
        await telemetry.FlushPendingAsync();
        telemetry.DeletePending();
        telemetry.TrimQueue();
        Assert.False(telemetry.Enabled);
        Assert.Equal(0, telemetry.PendingCount);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Diagnostics_NonemptyResultsAreDeduplicatedAndSinkFailureIsNonfatal()
    {
        var recorded = new List<string>();
        var context = OverlayExecutionContext.Fixture(new AppSettings(), lines => recorded.AddRange(lines));
        var result = new RecognitionResult { Diagnostics = new() { UnknownRawcodes = ["Z999", "Z999"] } };
        context.LogUnknownRawcodes(result);
        context.LogUnknownRawcodes(result);
        context.LogUnknownRawcodes(new RecognitionResult());
        Assert.EndsWith(" Z999", Assert.Single(recorded));
        var throwing = OverlayExecutionContext.Fixture(new AppSettings(), _ => throw new IOException("fake sink"));
        throwing.LogUnknownRawcodes(result);
        Assert.True(result.ShouldReplaceInventory);
    }

    [Fact]
    public void ProductionAdapters_UseOnlyExplicitTemporaryRootAndRetainBehavior()
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-context-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = OverlayExecutionContext.Production(root);
            Assert.False(Directory.Exists(root));
            Assert.True(context.EnsureConsent(() => true));
            Assert.Equal(Path.Combine(root, "game-data.json"), context.CatalogOverride);
            Assert.Equal(Path.Combine(root, "tmo-clear-cache.json"), context.ClearCacheFile);
            Assert.True(context.CreateCoachJournal()!.IsPersistent);
            context.SaveSettings(new AppSettings { GoalUnitId = "first" });
            context.SaveSettings(new AppSettings { GoalUnitId = "second" });
            Assert.Equal("second", context.LoadSettings().GoalUnitId);
            Assert.False(File.Exists(Path.Combine(root, "settings.json.tmp")));
            context.LogUnknownRawcodes(new RecognitionResult { Diagnostics = new() { UnknownRawcodes = ["Z999"] } });
            Assert.EndsWith(" Z999", Assert.Single(File.ReadAllLines(Path.Combine(root, "unknown-rawcodes.log"))));
            var calls = 0;
            context.RunRuntime(() => calls++);
            Assert.True(context.TryRuntime(() => { calls++; return true; }));
            Assert.Equal(2, calls);
            var pointerPath = Path.Combine(root, "growth-unit-pointers.json");
            GrowthUnitPointerCacheStore.Save(pointerPath, 123, new Dictionary<ulong, uint> { [42] = 99 });
            Assert.Equal((uint)99, GrowthUnitPointerCacheStore.Load(pointerPath, 123)[42]);
            GrowthUnitPointerCacheStore.Delete(pointerPath);
            Assert.False(File.Exists(pointerPath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
