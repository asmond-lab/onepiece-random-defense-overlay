# Gameplay telemetry v3 implementation contract

This is the shared implementation boundary for mandatory, versioned launch consent and
automatic gameplay records. Existing v2 aggregate/disclosure consent does not authorize v3.
Do not deploy or send real gameplay data during implementation or fixture verification.

## Consent and effects

- Current policy version: integer `3`; wire schema version: integer `3`.
- Explicit Agree is required before the main window, recognition, journals, upload queues,
  profile refresh or update/clear-data network work starts. Reject, Escape and closing the
  dialog exit. Current versioned consent may be remembered; changed policy requires consent
  again. Existing version-2 disclosure is not sufficient.
- Production execution factories enforce consent, not just a window visibility check.
  Fixture execution remains inert regardless of simulated consent.
- Automatic collection/transmission cannot be silently disabled with the old checkbox.
  No real user's consent may be accepted by tests or agents.
- Collection excludes nickname, chat, screenshots, memory addresses, local file paths,
  machine identifiers and free-form coach prose.
- Offline packets retain their original policy/schema association and immutable IDs.
  Local pending retention is 30 days; disclose bounded local storage. Raw server event
  retention is also 30 days; derived non-identifying statistical aggregates may persist.

## Endpoints and acknowledgements

Canonical server source: `ops/clear-dashboard/worker` in this repository. Preserve its
existing v2 and dashboard routes. Do not modify or deploy the older external Worker fork.
Use additive migrations, never the older destructive schema reset.

- OPTIONS/POST `/v3/gameplay`
- Authenticated GET `/v3/gameplay?cursor=...&limit=...` for the private collector.
- Public GET `/v3/live-stats?mapScriptSha256=...&difficulty=...` returns only validated
  aggregate recommendation weights in the existing `LiveStats` schema-1 document
  format, not raw events. Cohorts are never mixed across map hash or difficulty.
- Collector-authenticated PUT `/v3/live-stats` accepts
  `{schemaVersion:3,mapScriptSha256,difficulty,stats:<LiveStats schema-1 document>}`.
  Validate the complete statistics document and weight bounds before replacing a cohort.
- OPTIONS and successful POST require `X-Orand-Telemetry-Schema: 3`,
  `X-Orand-Telemetry-Accepted: true`, and enabled status. Disabled service receives no POST.
- POST success body: `{"schemaVersion":3,"packetId":"...","accepted":true}`.
  The client removes an outbox file only after matching acknowledgement.
- Exact retry is idempotent. Reusing a packet ID with different content is a conflict,
  never a second count. Persist event identity `(matchId, sequence)` too.
- GET raw response: `{"packets":[...],"nextCursor":null-or-string}`.
- Reject extra fields, invalid enums, excessive body/counts, non-finite numbers and
  free-form identifiers. Server limits and client splitting must agree.

## JSON envelope

All wire names are camelCase; enum values below are literal strings.

Required packet fields:

```
schemaVersion: 3
consentVersion: 3
packetId: 32 lowercase hexadecimal characters
matchId: 32 lowercase hexadecimal characters
chunkIndex: nonnegative integer
appVersion: bounded version string
mapVersion: bounded version string
mapScriptSha256: 64 lowercase hexadecimal characters
profileVersion: bounded version string
difficulty: one existing difficulty name or "unknown"
events: 1..64 events, body at most 512 KiB
```

`matchId` is random per observed match/session fragment, not a player/installation ID.
App restart may produce a new fragment; do not pretend to merge fragments by user identity.
No absolute client wall-clock is needed; the server timestamps receipt.

Required common event fields:

```
sequence: positive integer, increasing within the match
recognitionRevision: nonnegative integer
elapsedMs: nonnegative integer
round: 1..65
completedStory: 0..14
mode: "Normal" | "Manual" | "Beginner" | "Guide"
guideNumber: 0..99
kind: see below
evidence: see below
```

Evidence enum:
`native-observed`, `inventory-matched`, `observation-only`, `user-confirmed`,
`recommendation`, `unknown`.

Only these additional fields are permitted, as appropriate for each kind:

| kind | fields |
| --- | --- |
| `observation` | `inventory`, `rewardWisps`, `resources`, optional `gambleFailures`, optional `gambleCounters` |
| `recommendation` | `action`, optional `targetUnitId`, `goalUnitIds`, optional `selection` |
| `craft` | `unitId`, `count`, `consumed` |
| `selection` | `wispId`, `count`, `outputs` |
| `gamble` | `gambleType`, `count`, `cost`, `outputs` |
| `outcome` | `outcome`, `outcomeSource`, `inventory`, `goalUnitIds` |

Unit collections (`inventory`, `consumed`, `outputs`, `selection`) are JSON objects mapping
bounded catalog-style unit IDs to nonnegative integer counts (at most 512 entries).
Unit IDs contain only ASCII letters, numbers, `_`, `-`, `:` and have length 1..80.
`rewardWisps` permits only known wisp rawcodes.
`resources`/`cost` permits `gold`, `lumber`, `trait-points`; unknown values are omitted.
`action` is a `CoachActionKind` name.
`goalUnitIds` has at most 8 unit IDs.
`outcome` is `clear`, `fail`, or `interrupted`.
`outcomeSource` is `mapSettlement`, `clearRound`, `unitWipe`, `appExit`, or `unknown`.
`gambleType` is `low`, `medium`, or `high`.
`gambleFailures` is a nonnegative observed accumulator, NEVER an attempt count.
`gambleCounters`, when available from a validated native read, maps `low`, `middle`,
`high`, `world`, `absalom`, or `lumberWisp` to
`{attempts:nonnegative integer,successes:nonnegative integer,failures:nonnegative integer}`.
Each entry must satisfy `attempts == successes + failures`.
These are cumulative resolved counters at observation time, not ordered individual
attempts and not proof of the exact awarded unit. A missing reader/result is omitted.
Known pinned tariffs (gold/lumber) for the first five types are respectively
200/1, 1000/2, 2000/4, 3500/5, 500/1. Counter delta times tariff is source-derived
gross cost, not observed payment or net resource change; never label it as such.

## Evidence and lifecycle

- Feed the recorder from accepted current observations even when coach display is paused
  or hidden. Do not make collection depend on a displayed recommendation.
- Preserve observation snapshots, including actual reward-wisp counts and recognition
  revision. Deduplicate unchanged observations without losing relevant state transitions.
- Recommendations are separate events and are not executions. Do not serialize prose.
- Inventory-matched crafts/selection results must remain labeled as correlation.
  Record all observable deltas even when a stronger receipt cannot be established.
- No general per-attempt gambling receipt is currently verified. Do not infer attempts,
  costs, wins or resulting units from resource differences or the high-gamble failure
  accumulator. Source-proven per-player totals exist for the counter types above;
  include them only after native node/type/owner/coherence validation. Never assign
  interval counter changes to exact per-attempt rounds or awarded unit identities.
- Emit one terminal outcome per session, using last-good roster rather than a post-defeat
  empty roster. App exit is interrupted, not failure. Fence new match/revision resets.
- Persist bounded immutable chunks during play and on terminal/shutdown; network failure
  must not stop gameplay. Serialize flushes; retry automatically without duplicate sends.

## Statistical feedback

Raw upload alone is not recommendation learning. Cloudflare Worker scheduled events
perform incremental D1-backed aggregation and publish cohort statistics directly.
No PC scheduler, external collector credential or always-on PC is required. The Python
collector remains an offline parity/reference tool, not the production scheduler.
Connect validated aggregate output to client refresh/cache. Preserve map-version,
difficulty and goal segmentation and existing capped `LiveStats` weights.

Exclude uncertain outcomes, `clearRound`-only clears, interrupted fragments, unknown
executions and recommendations mistaken for actions. Reuse existing minimum sample,
both-outcome-group and Wilson-interval gates where compatible; weights stay within
the existing +/-0.1 bounds. Statistics describe associations, not causal proof.

Tests use local/mock HTTP and temporary isolated storage. Live Worker deployment,
remote migrations, publishing and real uploads require a separate explicit authorization.
