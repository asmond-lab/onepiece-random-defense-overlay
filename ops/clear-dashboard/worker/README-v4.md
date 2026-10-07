# Deployed observed telemetry v4

Deployment127466f9-6268-4143-a534-d935e196f050 on2026-09-15; additive migration0005 applied before Worker. Contract: ../../../docs/observed-telemetry-v4-contract.md. Existing v2/v3 remain isolated. POST/OPTIONS /v4/observations. Kill switches TELEMETRY_ENABLED=false or OBSERVATIONS_V4_ENABLED=false. RawGET is unavailable; authorized D1 SELECT only. No player/installation identity; session_id is random perfragment. source=synthetic-validation is verification and MUST be excluded from live-player diagnostics. Detailed records expire after30days.

Read-only operator query:

SELECT app_version, source, COUNT(*) AS packets, COUNT(DISTINCT session_id) AS fragments, MAX(received_at) AS latest_ms FROM telemetry_v4_packets GROUP BY app_version,source;

For actual tester timing, select source='live', group events bysession_id, then sort json_extract(event.value,'$.sequence'). elapsedMs is relative tofragment start, not an exact wall-clock client time. Server received_at is upload receipt time; offline uploads can arrive later. basic/full lanes are producer results, presentation lane marks visible-data expiry/recovery. gapMs on presentation/fresh is duration of a visible-data interruption, not an inferred network gap. Unknown rounds remain absent. Neither session-reset nor app-exit proves a gameclear/fail.

The current endpoint stores these observations for diagnosis; it does not train or enable legacy gameplay-v3 recommendations.
