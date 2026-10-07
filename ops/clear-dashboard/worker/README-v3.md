# Gameplay v3 Worker API

Canonical source: this directory. No deployment or remote migration has been performed.
Existing `/v2/aggregates` and `/v1/dashboard-snapshot` handlers are unchanged.
Wire contract: `../../../docs/gameplay-telemetry-v3-contract.md`.

## Cloudflare automatic aggregation

The Worker cron runs every minute; no PC process, collector token or scheduled Windows
task is needed. Each invocation advances a durable D1 cursor through at most 32 new
packets, reconstructs the affected complete matches and saves compact contributions.
Concurrent ticks use a 10-minute lease. A failed tick does not advance the cursor and
can safely repeat its replacements. Only a caught-up input cursor publishes statistics,
up to eight dirty cohorts per tick, atomically per cohort.

The same 30-sample, both-outcome-group, Wilson-interval and +/-0.1 limits apply.
Missing chunks, midmatch fragments, uncertain results and mixed goals never train.
Observation-only unit-wipe failures retain that label and require a prior observed
nonempty roster. Expired contributions dirty their cohorts so stale weights are removed.
The app continues using the existing public `/v3/live-stats` endpoint.

Memory/work bounds are explicit: at most 8 MiB and 2,048 packets per match; oversized
matches are excluded as whole fragments, never truncated into a training sample.
Cohorts support at most 512 goals and 512 unit keys per goal. A backlog continues on
later ticks; publication waits rather than claiming a partial backlog is complete.
Monitor scheduled errors/backlog before increasing these limits.

Apply additive migration `0003_gameplay_learning.sql` after migrations 0001/0002 before
deploying. The Python collector is an offline reference, not a second scheduled writer.

## Storage and API behavior

- `OPTIONS /v3/gameplay`: 204 with schema 3, accepted true, enabled true.
- `POST /v3/gameplay`: strict JSON envelope/event validation, 512 KiB **UTF-8 bytes**
  inclusive, 1..64 events. Success is 200 with the frozen packet acknowledgement.
  Bad JSON/schema is 400, excessive bytes 413, identity conflict 409, storage failure 503.
  Errors never carry accepted=true. Duplicate JSON property names are rejected.
- Both `TELEMETRY_ENABLED=false` and `GAMEPLAY_V3_ENABLED=false` disable v3 before
  reading any upload body or touching storage. The latter does not disable v2.
- Packet IDs, `(matchId, chunkIndex)` and `(matchId, sequence)` are unique.
  Canonical JSON hashes ignore object key order and whitespace, not event order or values.
  SQL triggers and a D1 atomic batch fence concurrent conflicting uploads; no read/write race.
  An event repeated under a different packet ID is a 409, even if identical: raw collector
  pages cannot double-count it. Clients must retry the original immutable packet.
  Match metadata (including map hash, difficulty and versions) is immutable within a fragment.
  Chunks can arrive out of order, while sequences must increase within each packet.
- `GET /v3/gameplay` requires the existing `COLLECTOR_KEY` bearer secret. Dashboard
  credentials do not authorize it. Optional `cursor` is an opaque decimal string;
  `limit` defaults to 100 and accepts 1..1000. Unknown/duplicate query parameters are 400.
  Packets are returned unchanged, ordered by monotonic server cursor, with nullable
  `nextCursor`. A page can contain fewer packets than `limit` to stay within 4 MiB of
  payload data; follow every non-null cursor. Cursor values may have gaps.
- Raw JSON is excluded from reads at age >=30 days. Uploads, collector reads and the hourly
  scheduled handler delete expired raw rows; the cron operates even when uploads are disabled.
  Physical cleanup has hourly scheduling granularity when idle. Original receipt time never
  changes on retry. Minimal random-ID/hash tombstones persist, without event payloads,
  so retries after expiry acknowledge the original packet without resurrecting raw data.
- `GET /v3/live-stats?mapScriptSha256=...&difficulty=...` is public and returns a schema-1
  LiveStats document only. Unknown valid cohorts return zero counts and empty weights/goals.
  Missing or malformed cohort selectors are 400; storage failure is 503, not fake empty data.
- `PUT /v3/live-stats` requires the existing `COLLECTOR_KEY` and the frozen schema-3
  cohort envelope. Replacement is atomic for exactly that cohort. All fields, count relations
  and both global/goal weights are validated before storage. Every weight must be finite
  and in [-0.1, +0.1]; values are rejected, never silently clamped. Stored stats are validated
  again on public reads. No fallback to another map/difficulty or raw data is possible.
  All v3 responses use `Cache-Control: no-store`.

## Shared bounds and optional fields

- Version tokens: `[A-Za-z0-9_.-]{1,80}`, matching the parallel v3 client DTO, not the
  narrower legacy v2 numeric version pattern. IDs: the frozen ASCII unit-ID pattern,
  length 1..80; packet/match IDs: lowercase hex32; map hash: lowercase hex64.
- Gameplay integers: 0..9007199254740991 unless the contract specifies a narrower range.
  Sequence starts at 1. This prevents lossy JavaScript/D1 identity parsing. Collections
  contain at most 512 entries; goal lists at most 8. JSON nesting is capped at 16.
- Difficulty selectors accept the six existing recognition names (Korean) and the existing
  v2 names `easy`, `normal`, `hard`, `nightmare`, `hell`, `god`, `god-plus`, `unknown`.
  They are **distinct literal cohorts**, never translated or merged.
- Known reward wisps: `e016`, `e017`, `e018`, `e019`, `e0IX`, `e01A`, from
  `FirstLegendRewardGate.RewardIds`. The client may emit a narrower supported subset.
- Optional observation `gambleCounters` preserves only the six frozen counter keys:
  `low`, `middle`, `high`, `world`, `absalom`, `lumberWisp`. Each entry requires exactly
  nonnegative safe-integer `attempts`, `successes`, `failures`, with attempts equal to
  successes plus failures. Absent data stays absent. `gambleFailures` remains accepted.
  No costs, individual attempt rounds or awarded units are inferred from counters.
  Collector round-history processing and native-reader wiring belong to parallel tracks.
- LiveStats counts are nonnegative signed-32-bit integers, compatible with the client.
  Goals/global weights/goal-weight maps each have at most 512 entries, with unambiguous
  case-insensitive unit keys. Required stats fields are schemaVersion=1, totalRecords,
  labeledRecords, goals. Optional fields preserve existing shipped schema-1 documents:
  weights, goalWeights, generatedAt (UTC ISO timestamp), difficulties (only this cohort),
  and goal failHeavyUnits/adherenceMean. Unknown fields are rejected at every level.
  Goal counts must satisfy clears <= labeled <= plays; plays/labeled cannot exceed totals.
  `adherenceMean: null` preserves unknown adherence when no recommendation was displayed.
  The collector, not this storage API, owns evidence/sample/Wilson eligibility gates.

## Verification (local only)

From the repository root, Node 24 supplies the real SQLite adapter with no installation:

```powershell
node --test ops/clear-dashboard/test/*.test.mjs
node --check ops/clear-dashboard/worker/src/index.mjs
node --check ops/clear-dashboard/worker/src/gameplay.mjs
node --check ops/clear-dashboard/worker/src/gameplay-validate.mjs
```

Evidence lives in `.gameplay-server-artifacts/`: initial RED, counter-extension RED,
full GREEN, browser bundle, and local workerd/D1 integration script/log. The local runtime
script uses the already-installed Miniflare 5 alpha adapter, isolated fixture storage,
fixture-only credentials, and a blocked outbound service; no external Worker is contacted.
TypeScript LSP was unavailable, so no LSP-clean claim is made. Syntax checks and esbuild
provide the available static validation. Tests cover existing v2/dashboard routes too.

## Bullet guide profile (parent review before deployment)

`GET /v3/live-stats?mapScriptSha256=...&difficulty=...&profile=bullet-guide-1`
returns the same schema-1 projection in a separate namespace. Successful profile
responses include `X-Orand-Stats-Profile: bullet-guide-1`; the client requires it.
No query profile means the unchanged generic cohort, with no profile fallback.
The authenticated PUT envelope optionally accepts `profile: "bullet-guide-1"` and
validates the complete projection, goal identity, count sums and weight sample gates.

Migration `0004_bullet_guide_learning.sql` adds a per-session eligibility flag,
an isolated `(map hash, difficulty, profile)` projection table, and a durable
backfill flag. The new cron consumes that flag under its existing lease and
reconstructs retained raw matches in the existing 32-packet pages. It publishes
both namespaces atomically after the backlog is complete, without resetting
generic tables. Retries and retention expiry update both namespaces.

Only complete matches whose **every event** is `mode=Guide, guideNumber=1` and
whose single stable goal is `rawcode:180h` enter the Bullet profile. Opening and
support recommendation targets may differ from the goal. Mixed-mode histories,
partial captures, ambiguous outcomes and different goals never become Bullet
samples. Generic aggregation behavior is unchanged.

Client cache envelopes bind schema version, map hash, difficulty and profile;
profile filenames have a `-bullet-guide-1.json` suffix. Authorization is checked
before effects and after asynchronous responses. Profile snapshots cannot feed
the generic recommendation engine. Parent-owned app hook details are recorded in
`.bullet-learning-artifacts/parent-hooks.md` at the repository root.

## Approval-only migration/deployment commands

These commands are **not executed** by this task. Parent approval is required for remote
schema changes and deployment. No old fork, schema reset, commit or push is involved.
Run from PowerShell with the existing Wrangler installation and existing auth setup:

```powershell
Set-Location 'C:\Users\123\Desktop\dev\orand-overnight-overlay-20260907-111725\ops\clear-dashboard\worker'
npx --no-install wrangler d1 migrations list orand-telemetry --remote --config wrangler.toml
npx --no-install wrangler d1 migrations apply orand-telemetry --remote --config wrangler.toml
npx --no-install wrangler deploy --config wrangler.toml
```

Review the migration list first: `0001_dashboard_rollups.sql` (if pending),
`0002_gameplay_v3.sql`, `0003_gameplay_learning.sql`, and
`0004_bullet_guide_learning.sql` are additive.
Apply the pending migrations before deploying the v3 routes/cron. Never run `schema.sql`.
The existing `COLLECTOR_KEY` is referenced, not changed or printed. Verify its presence
through the approved secret-management process; do not paste its value into commands/logs.
`DASHBOARD_READ_KEY` continues to serve only the existing dashboard route.
