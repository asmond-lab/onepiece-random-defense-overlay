# Gameplay client wiring (owned child boundary)

Public construction (no default production paths or HTTP):

```csharp
var recorder = new GameplaySessionRecorder(permission, metadata, catalog, timeProvider);
var outbox = new GameplayTelemetryOutbox(permission, ownedUserRoot, httpClient, endpoint, timeProvider);
var client = new GameplayTelemetryClient(permission, recorder, outbox, timeProvider);
InitializeGameplayTelemetry(client); // starts automatic startup + periodic processing
```

`permission` is `Func<bool>` for CURRENT v3 consent AND production execution authority. Fixture factories must return null / never construct an active client. `metadata` is `GameplayTelemetryMetadata(AppVersion, MapVersion, MapScriptSha256, ProfileVersion)`; no defaults that resolve a production root. Endpoint must be supplied and end in `/v3/gameplay`.

Parent-owned MainWindow.xaml.cs calls `ResetGameplayTelemetry()` before resetting match state, and calls/awaits `ShutdownGameplayTelemetryAsync()` on shutdown (bounded cancellation internal). No main-window edits from child. Accepted observation/recommendation/outcome hooks are child-owned in MainWindow.Coach.cs. Child does not retire v2.

Recorder APIs: `Observe(GameplayTelemetryObservation)`, `Recommend(CoachFrame, CoachDecision)`, `Complete(string outcome, string outcomeSource)`, `DrainPackets()` and `Reset()`.

Transport: `GameplayTelemetryOutbox.EnqueueAsync(GameplayTelemetryPacket, CancellationToken = default)` returns `Task<bool>`, `FlushAsync(CancellationToken = default)` returns `Task`; observable `LastError`. Client orchestrates in background; `Start()`, `NotifyObservation()` (persist only), `RequestFlush()`, `ShutdownAsync(CancellationToken = default)`. `Recorder` exposes the pure recorder. Retention: 30 days, 256 packets, 32 MiB; oldest pending files evicted at the bound. Retry interval: 30 seconds. Shutdown uses a three-second cancellation budget after stopping the background consumer.

## Confirmed consent-factory compatibility

The implemented `OverlayExecutionContext.CreateGameplayTelemetry(GameplayTelemetryMetadata, DataCatalog, HttpClient? = null)` uses the exact constructors above. Parent should call `InitializeGameplayTelemetry(_execution.CreateGameplayTelemetry(metadata, _catalog))` once after catalog and match state are ready. The helper starts the client; no additional Start is necessary. Source `metadata` from pinned app/map/profile values, not process display text or any path. The existing count-local collision has been fixed (`quantity` for map entries).

## Native gamble counter bridge

`GameplayTelemetryObservation.GambleCounters` and `GameplayTelemetryEvent.GambleCounters` are nullable `ImmutableDictionary<string, GameplayTelemetryGambleCounter>`. The value constructor is `GameplayTelemetryGambleCounter(int Attempts, int Successes, int Failures)`. Keys: `low`, `middle`, `high`, `world`, `absalom`, `lumberWisp`. Values require nonnegative integers and attempts == successes + failures, checked using long arithmetic. `IsValid` is JSON-ignored. Null is omitted. Counter changes trigger new observation events, even with unchanged inventory/round/resources. No costs, exact awarded units or individual-attempt rounds are inferred. Parent/native owner may fill this field in the initializer in `MainWindow.GameplayTelemetry.cs` after its native validation; it is intentionally unset until that bridge exists. No native-reader files are changed by this child.

Collector/backend owner: preserve optional `gambleCounters` in observation/round history using the above cumulative semantics. This client child does not edit collector/server files. Shared synthetic golden wire: `docs/fixtures/gameplay-v3-golden.json`; this includes the new counter field and all emitted event kinds. Fixture IDs are synthetic; do not send the fixture to a live service.

## Verification and ownership

Owned new production files: `GameplayTelemetryContracts.cs`, `GameplaySessionRecorder.cs`, `GameplayTelemetryOutbox.cs`, `GameplayTelemetryClient.cs`, `MainWindow.GameplayTelemetry.cs`. Two hooks added in `MainWindow.Coach.cs`; no edits to parent-owned MainWindow.xaml.cs, consent/execution factory, LiveStats, recommendation engine, native readers or server. New tests: `OrandOverlay.Tests/GameplayTelemetryTests.cs`; independent focused runner `OrandOverlay.Tests/GameplayTelemetry.Focused.csproj` avoids unrelated sibling test compilation while still building the real main assembly.

Run: `dotnet test OrandOverlay.Tests/GameplayTelemetry.Focused.csproj --artifacts-path .gameplay-client-artifacts`. Thirty-three focused tests cover rawcode allowlisting, all-mode capture, duplicate/stale frames, generation/reset, independent craft and selection receipts in both read orders, uncertain deltas, terminal roster, chunk count/bytes, strict JSON including counters, durable restart/offline/idempotent retry/ACK, cancellation/serialization, consent fences, retention bounds, automatic startup/periodic/shutdown and golden JSON. No real HTTP, game input, production restart or user data roots are used. RED evidence: `.gameplay-client-artifacts/red-focused.log`; green/build evidence: `.gameplay-client-artifacts/green-final.log`, `.gameplay-client-artifacts/build.log`. C# LSP unavailable; compiler verification used instead.

Intentional conservative limits: observations require round 1..65 (the frozen wire has no pre-round representation); uncertain/mixed receipts remain snapshots only; a pending exact-correlation baseline spans at most eight changed observations. Reset fences must be wired before parent match reset. UI wiring and the native counter bridge remain parent-owned and are not claimed verified by this focused client run.
