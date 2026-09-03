# Clear dashboard operations

This directory produces a read-only dashboard snapshot and a Discord Components v2
payload. It never sends a Discord message and contains no Discord token or webhook.

## Contract

`GET /v1/dashboard-snapshot` returns schema version 1:

- `status`: `ok`, `empty`, `stale`, or `error`
- `windows.{1h,24h,7d}.clearCount`
- seven-day `distributions.byDifficulty` and `distributions.byGoal`
- `recentClearAt`, `dataDelaySeconds`, `refreshedAt`
- `error`: `null` unless the normalized state is `error`

The machine-readable contract is `dashboard-snapshot.schema.json`.

The Worker records only server receipt time in hourly anonymous rollups. It does not
add a client timestamp or identifier to the existing privacy-preserving v2 record.
Existing rows cannot be backfilled into time windows because the deployed schema has
no event or receipt timestamp.

## SecretRef and host boundary

| Consumer | SecretRef / value | Purpose |
|---|---|---|
| Worker | `COLLECTOR_KEY` | Existing authenticated raw aggregate pull |
| Worker | `DASHBOARD_READ_KEY` | New read-only dashboard snapshot |
| OpenClaw automation | `ORAND_DASHBOARD_READ_KEY` | Injected value of `DASHBOARD_READ_KEY` |
| OpenClaw automation | `ORAND_DASHBOARD_ENDPOINT` | Optional endpoint override |

The poller allows only `orand-telemetry.epic42121.workers.dev` over HTTPS. Target IDs
for the OpenClaw message action are in `openclaw-target.json`.

## Local verification

```powershell
cd D:\OrandOverlay\outputs\OrandOverlay\ops\clear-dashboard
node --test
node bin\render-payload.mjs
```

Without `ORAND_DASHBOARD_READ_KEY`, the renderer deliberately emits a Components v2
configuration-error card. With SecretRef injection it performs a read-only poll:

```powershell
$env:ORAND_DASHBOARD_READ_KEY = '<injected SecretRef>'
node bin\render-payload.mjs
```

## Deployment handoff (not run here)

The checked-in Worker source preserves `POST` and authenticated `GET` on
`/v2/aggregates`, adds hourly rollup writes for clear outcomes, and adds the read-only
snapshot route. Before a future deployment:

```powershell
cd ops\clear-dashboard\worker
npx wrangler d1 migrations apply orand-telemetry --remote
npx wrangler secret put DASHBOARD_READ_KEY
npx wrangler deploy
```

`COLLECTOR_KEY` remains the existing injected Worker secret. Do not recreate or print
it. Apply the migration before deploying the Worker source. The dashboard starts with
an empty state and fills prospectively; historical time windows cannot be reconstructed.
