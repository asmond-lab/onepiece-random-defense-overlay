# Gameplay v3 feedback pipeline

Production scheduling runs in Cloudflare Worker, not on this PC. This Python tool is
retained for offline imports and parity tests. Do not register its Windows scheduler.

Python 3.12+, standard library only. The external legacy collector is not used or modified.
No command performs git operations or deployment. Interactive collection publishes only
with `--publish`; the approved scheduling entry point explicitly runs collection AND
publication. There is no default remote endpoint.

## Automatic hourly operation

`run-hourly-job.ps1` / `run_hourly_job.py` provide a bounded unattended job using the same
collector and learner. [SCHEDULING.md](SCHEDULING.md) contains the exact approval-gated
DPAPI credential setup and hourly Windows Task Scheduler registration commands. Once
registered, it collects and publishes automatically every hour; no per-run operator
command is needed. No task has been registered during implementation.

## Local verification

From the repository root:

```text
python -m unittest discover -s ops/gameplay-collector -p "test_*.py"
python ops/gameplay-collector/collect_telemetry.py --help
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --filter "FullyQualifiedName~GameplayStatsTests|FullyQualifiedName~LiveStatsTests|FullyQualifiedName~RecommendationCommunityPrioritiesTests" --artifacts-path artifacts/.gameplay-learning-artifacts
```

The C# integration test invokes `synthetic_fixture.py`, which runs the real collector CLI
against a local synthetic page containing valid, duplicated, and invalid-outcome cohorts.
The resulting JSON crosses the refresh/cache parser and changes the actual engine's
community-priority score from 40 to 44. No app, game, real consent, or remote endpoint is used.

## Operator interface (requires separate authorization)

`collect_telemetry.py --input PAGE.json --artifacts DIRECTORY` imports a local raw-response
page and produces cohort envelopes. Default storage is `.gameplay-learning-artifacts`.

Actual network collection additionally requires `--collect --endpoint ORIGIN` and a bearer
collector credential in `ORAND_COLLECTOR_TOKEN` (or the environment variable named by
`--token-env`). Aggregate upload separately requires `--publish`. HTTPS is required except
for loopback test servers. Failures exit nonzero; they are not silently ignored.

GET `/v3/gameplay` is cursor-paginated. SQLite commits raw data and the cursor together only
after validating the complete retained event set. Packet IDs and `(matchId, sequence)` are
both deduplicated; conflicting content aborts the transaction. `nextCursor:null` does not
provide a final-page watermark, so the final page may be replayed next time; identity
checks make that harmless. Raw retention is 30 days. Aggregate files contain no raw history.
PUT `/v3/live-stats` receives the schema-3 cohort envelope around a schema-1 document.

## Statistical interpretation

- The exact map script hash, exact difficulty, and goal are independent cohorts. No
  English/Korean normalization or cross-goal/global-weight fallback is performed.
- A complete eligible session needs chunk zero, contiguous chunks/events starting at
  sequence one, an initial round-one observation, and one final native-observed or
  user-confirmed outcome. An observation-only unit-wipe failure is also eligible when
  the complete fragment contains a native-observed nonempty roster; it remains labeled
  observation-only, never upgraded to a native result. Midmatch fragments, interrupted outcomes, unknown evidence,
  `clearRound`-only clears, inconsistent identities, and revision/time resets do not learn.
- Clear labels require `mapSettlement`; failure labels require `unitWipe` or
  `mapSettlement`. Empty terminal rosters use the last good observed roster, not a
  recommendation or inferred craft. Zero-count inventory entries are not ownership.
- Schema-1 cannot address a multi-goal combination. Such sessions, goal-changing
  sessions, and ambiguous map-version aliases are conservatively excluded rather than
  mixed into single-goal weights. Global `weights` stays empty.
- A goal needs at least 30 labeled sessions, both clear and fail groups, and nonoverlapping
  95% Wilson adoption-rate intervals. The interval gap is capped at +/-0.1.
- Adherence is final-inventory-matched recommendation correlation. Recommendations are
  never counted as execution receipts, and no causal benefit is claimed.
- Optional native `gambleCounters` retain their cumulative resolved values at each
  observed round/revision in `Session.history` and the bounded private raw store. All six
  counter kinds are validated, including `attempts == successes + failures`. Missing
  counters stay missing. Neither attempt rounds, costs, nor awarded units are inferred.
  Legacy `gambleFailures` remains a failure accumulator, not an attempt count.

## Parent-owned client integration

```csharp
var service = new GameplayStatsRefreshService(httpClient, cacheDirectory,
    () => execution.RuntimeEnabled && currentVersionedConsentIsValid);
engine.SetGameplayCohort(mapScriptSha256, exactDifficulty);
engine.SetLiveStats(service.GetSnapshot(mapScriptSha256, exactDifficulty));
await service.RefreshAsync(mapScriptSha256, exactDifficulty, engine, cancellationToken);
```

The supplied `HttpClient.BaseAddress` is the canonical telemetry service origin; configure
its timeout at composition. The authorization callback must check **both** current v3
consent and runtime capability on every call. Fixture execution must never return true.
Call `SetGameplayCohort` immediately when the current map/difficulty changes, including
unknown/unverified state, before computing recommendations. This clears stale weights
without waiting for a network response. Refresh also establishes the requested cohort.

The service retains its latest validated snapshot independently of the captured engine.
On every engine rebuild, call `SetGameplayCohort` first, then install
`service.GetSnapshot(hash, difficulty)`; never copy the legacy unbound stats field.
After awaiting refresh, obtain that exact-cohort snapshot and redraw only if the request's
hash/difficulty still match the current observation, the snapshot `MatchesCohort(...)`,
and its reference differs from the last applied snapshot. `RefreshAsync` returning false
means no successful network refresh; a valid disk snapshot may still have been accepted.
`GetSnapshot` returns empty stats for a different cohort or revoked authorization.

`LiveStats.TryParse(json, hash, difficulty, out stats)` validates the full schema, duplicate
keys, counters, finite bounds, goal gates, and exact difficulty. The public GET document
has no hash field: hash binding comes from its cohort-specific request and is retained in
the local schema-3 cache envelope. Cache replacement is atomic, invalid/offline responses
preserve the existing same-cohort cache, and delayed responses cannot install weights in
a different engine cohort. Authorization is rechecked after asynchronous work.

`RecommendationEngine.SetLiveStats` rejects unbound legacy files and mismatched cohorts.
Goal weights affect the existing clear-profile priority score, or the existing community
priority score when no clear-profile score exists; they do not alter action evidence or
execution. `MainWindow` and execution-composition integration are parent-owned.
