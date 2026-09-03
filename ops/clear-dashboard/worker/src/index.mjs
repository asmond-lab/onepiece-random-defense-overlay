import { validateRecord } from "./validate.mjs";

const UPLOAD_PATH = "/v2/aggregates";
const DASHBOARD_PATH = "/v1/dashboard-snapshot";
const STALE_AFTER_SECONDS = 1800;
const HEADERS = {
  "X-Orand-Telemetry-Schema": "2",
  "X-Orand-Telemetry-Accepted": "true",
  "X-Orand-Telemetry-Enabled": "true",
};

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === DASHBOARD_PATH && request.method === "GET") {
      return dashboard(request, env);
    }
    if (url.pathname !== UPLOAD_PATH) return new Response(null, { status: 404 });
    if (!enabled(env)) {
      return response(null, 503, {
        "X-Orand-Telemetry-Accepted": "false",
        "X-Orand-Telemetry-Enabled": "false",
      });
    }
    if (request.method === "OPTIONS") return response(null, 204);
    if (request.method === "POST") return upload(request, env);
    if (request.method === "GET") return pull(request, env, url);
    return response(null, 405);
  },
};

async function dashboard(request, env) {
  if (!authorized(request, env.DASHBOARD_READ_KEY)) return new Response(null, { status: 403 });
  const now = env.CLOCK?.() ?? new Date();
  try {
    const [windows, difficulty, goal, recent] = await Promise.all([
      env.DB.prepare(`
        SELECT /* dashboard:windows */
          COALESCE(SUM(CASE WHEN bucket_start >= datetime(?, '-1 hour') THEN clear_count ELSE 0 END), 0) AS one_hour,
          COALESCE(SUM(CASE WHEN bucket_start >= datetime(?, '-1 day') THEN clear_count ELSE 0 END), 0) AS one_day,
          COALESCE(SUM(clear_count), 0) AS seven_days
        FROM clear_rollups WHERE bucket_start >= datetime(?, '-7 days')`)
        .bind(now.toISOString(), now.toISOString(), now.toISOString()).first(),
      env.DB.prepare(`
        SELECT /* dashboard:difficulty */ difficulty AS key, SUM(clear_count) AS clear_count
        FROM clear_rollups WHERE bucket_start >= datetime(?, '-7 days')
        GROUP BY difficulty ORDER BY clear_count DESC`)
        .bind(now.toISOString()).all(),
      env.DB.prepare(`
        SELECT /* dashboard:goal */ goal_tier_family AS key, SUM(clear_count) AS clear_count
        FROM clear_rollups WHERE bucket_start >= datetime(?, '-7 days')
        GROUP BY goal_tier_family ORDER BY clear_count DESC`)
        .bind(now.toISOString()).all(),
      env.DB.prepare(`
        SELECT /* dashboard:recent */ recent_clear_at
        FROM dashboard_state WHERE singleton = 1`).first(),
    ]);
    return Response.json(buildSnapshot(now, windows, difficulty.results, goal.results, recent));
  } catch {
    return Response.json({
      schemaVersion: 1,
      status: "error",
      error: { code: "connection_error", message: "Dashboard storage is unavailable" },
    }, { status: 503 });
  }
}

function buildSnapshot(now, windows, difficulty, goal, recent) {
  const oneHour = Number(windows?.one_hour ?? 0);
  const oneDay = Number(windows?.one_day ?? 0);
  const sevenDays = Number(windows?.seven_days ?? 0);
  const recentClearAt = recent?.recent_clear_at ?? null;
  const dataDelaySeconds = recentClearAt === null
    ? null
    : Math.max(0, Math.floor((now.getTime() - Date.parse(recentClearAt)) / 1000));
  let status = "ok";
  if (recentClearAt === null) status = "empty";
  else if (dataDelaySeconds > STALE_AFTER_SECONDS) status = "stale";
  return {
    schemaVersion: 1,
    status,
    windows: {
      "1h": { clearCount: oneHour },
      "24h": { clearCount: oneDay },
      "7d": { clearCount: sevenDays },
    },
    distributions: {
      byDifficulty: distribution(difficulty),
      byGoal: distribution(goal),
    },
    recentClearAt,
    dataDelaySeconds,
    refreshedAt: now.toISOString(),
    error: null,
  };
}

function distribution(rows) {
  return rows.map((row) => ({ key: String(row.key), clearCount: Number(row.clear_count) }));
}

async function upload(request, env) {
  const body = await request.text();
  if (body.length > 4096) return response(null, 400);
  let record;
  try {
    record = JSON.parse(body);
  } catch {
    return response(null, 400);
  }
  if (validateRecord(record) !== null) return response(null, 400);

  const aggregate = env.DB.prepare(`
    INSERT INTO aggregates(
      app_version, map_version, difficulty, damage_lane, goal_tier_family,
      surface, urgency, outcome, observed_objects, completed_tops,
      ready_scans, waiting_scans, transient_scans, unsupported_scans, match_count)
    VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    ON CONFLICT(
      app_version, map_version, difficulty, damage_lane, goal_tier_family,
      surface, urgency, outcome, observed_objects, completed_tops,
      ready_scans, waiting_scans, transient_scans, unsupported_scans)
    DO UPDATE SET match_count = aggregates.match_count + excluded.match_count`)
    .bind(
      record.appVersion, record.mapVersion, record.difficulty, record.damageLane,
      record.goalTierFamily, record.surface, record.urgency, record.outcome,
      record.observedObjects, record.completedTops, record.readyScans,
      record.waitingScans, record.transientScans, record.unsupportedScans,
      record.matchCount,
    );
  try {
    if (record.outcome !== "clear") {
      await aggregate.run();
      return response(null, 204);
    }
    const receivedAt = (env.CLOCK?.() ?? new Date()).toISOString();
    const bucket = `${receivedAt.slice(0, 13).replace("T", " ")}:00:00`;
    await env.DB.batch([
      aggregate,
      env.DB.prepare(`
        INSERT INTO clear_rollups(bucket_start, difficulty, goal_tier_family, clear_count)
        VALUES(?, ?, ?, 1)
        ON CONFLICT(bucket_start, difficulty, goal_tier_family)
        DO UPDATE SET clear_count = clear_rollups.clear_count + 1`)
        .bind(bucket, record.difficulty, record.goalTierFamily),
      env.DB.prepare(`
        INSERT INTO dashboard_state(singleton, recent_clear_at) VALUES(1, ?)
        ON CONFLICT(singleton) DO UPDATE SET recent_clear_at = excluded.recent_clear_at`)
        .bind(receivedAt),
    ]);
    return response(null, 204);
  } catch {
    return response(null, 507);
  }
}

async function pull(request, env, url) {
  if (!authorized(request, env.COLLECTOR_KEY)) return response(null, 403);
  const requested = Number(url.searchParams.get("limit") ?? 500);
  const limit = Number.isInteger(requested) ? Math.min(Math.max(requested, 1), 1000) : 500;
  try {
    const rows = await env.DB.prepare(
      "SELECT * FROM aggregates ORDER BY match_count DESC LIMIT ?",
    ).bind(limit).all();
    return Response.json({ schemaVersion: 2, aggregates: rows.results }, { headers: HEADERS });
  } catch {
    return response(null, 507);
  }
}

function authorized(request, expected) {
  return typeof expected === "string"
    && expected.length > 0
    && request.headers.get("Authorization") === `Bearer ${expected}`;
}

function enabled(env) {
  return String(env.TELEMETRY_ENABLED ?? "true").toLowerCase() !== "false";
}

function response(body, status, extraHeaders = {}) {
  return new Response(body, {
    status,
    headers: { ...HEADERS, ...extraHeaders },
  });
}
