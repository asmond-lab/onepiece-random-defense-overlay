import { BodyError, canonical, emptyStats, readJson, validCohort, validProfile, validatePacket, validateStats, validateStatsEnvelope } from "./gameplay-validate.mjs";

const RETENTION_MS = 30 * 24 * 60 * 60 * 1000;
const headers = {
  "X-Orand-Telemetry-Schema": "3",
  "Cache-Control": "no-store",
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "OPTIONS, POST, GET, PUT",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
  "Access-Control-Expose-Headers": "X-Orand-Telemetry-Schema, X-Orand-Telemetry-Accepted, X-Orand-Telemetry-Enabled, X-Orand-Stats-Profile",
};
function reply(body, status = 200, enabled = true, profile = null) {
  const options = { status, headers: { ...headers,
    "X-Orand-Telemetry-Enabled": String(enabled),
    "X-Orand-Telemetry-Accepted": String(enabled && status >= 200 && status < 300),
    ...(profile ? { "X-Orand-Stats-Profile": profile } : {}) } };
  return body === null ? new Response(null, options) : Response.json(body, options);
}
const authorized = (request, env) => typeof env.COLLECTOR_KEY === "string" && env.COLLECTOR_KEY.length > 0 &&
  request.headers.get("Authorization") === `Bearer ${env.COLLECTOR_KEY}`;
const now = env => (env.CLOCK?.() ?? new Date()).getTime();
function query(url, allowed) {
  return [...url.searchParams.keys()].every(k => allowed.includes(k) && url.searchParams.getAll(k).length === 1);
}

export async function gameplay(request, env, url) {
  const enabled = String(env.TELEMETRY_ENABLED ?? "true").toLowerCase() !== "false" &&
    String(env.GAMEPLAY_V3_ENABLED ?? "true").toLowerCase() !== "false";
  if (!enabled) return reply(null, 503, false);
  if (request.method === "OPTIONS") return reply(null, 204);
  const raw = url.pathname === "/v3/gameplay";
  if ((raw && request.method === "GET") || (!raw && request.method === "PUT")) {
    if (!authorized(request, env)) return reply(null, 403);
  }
  try {
    if (raw && request.method === "POST") return await upload(request, env);
    if (raw && request.method === "GET") return await pull(env, url);
    if (!raw && request.method === "PUT") return await putStats(request, env);
    if (!raw && request.method === "GET") return await getStats(env, url);
    return reply(null, 405);
  } catch (error) {
    if (error instanceof BodyError) return reply(null, error.status);
    // Only our explicit transactional identity guards are conflicts; storage faults
    // remain failures, never acknowledgements. Do not expose DB details or payloads.
    if (String(error?.message).includes("v3_conflict")) return reply(null, 409);
    return reply(null, 503);
  }
}
async function digest(value) {
  const bytes = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(canonical(value)));
  return Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, "0")).join("");
}
export async function purgeGameplay(env) {
  const cutoff = now(env) - RETENTION_MS;
  await env.DB.batch([
    env.DB.prepare("UPDATE gameplay_v3_packets SET raw_expired = 1 WHERE raw_expired = 0 AND received_at <= ?").bind(cutoff),
    env.DB.prepare("DELETE FROM gameplay_v3_raw WHERE received_at <= ?").bind(cutoff),
  ]);
}
async function upload(request, env) {
  const p = await readJson(request);
  if (!validatePacket(p)) return reply(null, 400);
  const { events, packetId, chunkIndex, ...metadata } = p;
  const [packetHash, metadataHash, eventHashes] = await Promise.all([
    digest(p), digest(metadata), Promise.all(events.map(digest)),
  ]);
  const receivedAt = now(env);
  const cutoff = receivedAt - RETENTION_MS;
  await env.DB.batch([
    env.DB.prepare("INSERT INTO gameplay_v3_matches(match_id, metadata_hash) VALUES(?, ?) ON CONFLICT(match_id) DO NOTHING")
      .bind(p.matchId, metadataHash),
    env.DB.prepare(`INSERT INTO gameplay_v3_packets(packet_id, match_id, chunk_index, content_hash, received_at)
      VALUES(?, ?, ?, ?, ?) ON CONFLICT(packet_id) DO NOTHING`).bind(packetId, p.matchId, chunkIndex, packetHash, receivedAt),
    ...events.map((e, i) => env.DB.prepare(`INSERT INTO gameplay_v3_events(match_id, sequence, packet_id, content_hash)
      VALUES(?, ?, ?, ?) ON CONFLICT(match_id, sequence) DO NOTHING`).bind(p.matchId, e.sequence, packetId, eventHashes[i])),
    // On a retry use the original receipt, never renew raw retention or resurrect it.
    env.DB.prepare(`INSERT INTO gameplay_v3_raw(packet_id, received_at, payload)
      SELECT packet_id, received_at, ? FROM gameplay_v3_packets
      WHERE packet_id = ? AND raw_expired = 0 AND received_at > ?
      ON CONFLICT(packet_id) DO NOTHING`).bind(canonical(p), packetId, cutoff),
    env.DB.prepare("UPDATE gameplay_v3_packets SET raw_expired = 1 WHERE raw_expired = 0 AND received_at <= ?").bind(cutoff),
    env.DB.prepare("DELETE FROM gameplay_v3_raw WHERE received_at <= ?").bind(cutoff),
  ]);
  return reply({ schemaVersion: 3, packetId, accepted: true });
}
async function pull(env, url) {
  if (!query(url, ["cursor", "limit"])) return reply(null, 400);
  const cursorText = url.searchParams.get("cursor") ?? "0";
  const limitText = url.searchParams.get("limit") ?? "100";
  if (!/^(0|[1-9][0-9]{0,15})(?![\s\S])/.test(cursorText) || !/^[1-9][0-9]{0,3}(?![\s\S])/.test(limitText)) return reply(null, 400);
  const cursor = Number(cursorText), limit = Number(limitText);
  if (!Number.isSafeInteger(cursor) || limit > 1000) return reply(null, 400);
  await purgeGameplay(env);
  // Bound returned payload bytes as well as packet count: 1000 maximum-sized
  // packets would exceed Worker memory. Keep one payload-free lookahead marker.
  const rows = await env.DB.prepare(`WITH candidates AS (
      SELECT cursor, payload, length(CAST(payload AS BLOB)) AS bytes FROM gameplay_v3_raw
      WHERE cursor > ? AND received_at > ? ORDER BY cursor LIMIT ?
    ), sized AS (
      SELECT *, SUM(bytes) OVER (ORDER BY cursor) AS cumulative FROM candidates
    ) SELECT cursor, CASE WHEN cumulative <= 4194304 THEN payload ELSE NULL END AS payload
      FROM sized WHERE cumulative - bytes <= 4194304 ORDER BY cursor`)
    .bind(cursor, now(env) - RETENTION_MS, limit + 1).all();
  const page = rows.results.filter(row => row.payload !== null).slice(0, limit);
  return reply({ packets: page.map(row => JSON.parse(row.payload)),
    nextCursor: rows.results.length > page.length ? String(page.at(-1).cursor) : null });
}
async function putStats(request, env) {
  const p = await readJson(request);
  if (!validateStatsEnvelope(p)) return reply(null, 400);
  if (p.profile) {
    await env.DB.prepare(`INSERT INTO gameplay_v3_profile_stats(map_script_sha256,difficulty,profile,stats,updated_at)
      VALUES(?,?,?,?,?) ON CONFLICT(map_script_sha256,difficulty,profile)
      DO UPDATE SET stats=excluded.stats,updated_at=excluded.updated_at`)
      .bind(p.mapScriptSha256, p.difficulty, p.profile, canonical(p.stats), now(env)).run();
    return reply({ schemaVersion: 3, accepted: true });
  }
  await env.DB.prepare(`INSERT INTO gameplay_v3_live_stats(map_script_sha256, difficulty, stats, updated_at)
    VALUES(?, ?, ?, ?) ON CONFLICT(map_script_sha256, difficulty)
    DO UPDATE SET stats = excluded.stats, updated_at = excluded.updated_at`)
    .bind(p.mapScriptSha256, p.difficulty, canonical(p.stats), now(env)).run();
  return reply({ schemaVersion: 3, accepted: true });
}
async function getStats(env, url) {
  const hash = url.searchParams.get("mapScriptSha256"), difficulty = url.searchParams.get("difficulty");
  const profile = url.searchParams.get("profile");
  if (!query(url, ["mapScriptSha256", "difficulty", "profile"]) || !validCohort(hash, difficulty) || !validProfile(profile)) return reply(null, 400);
  const row = profile ? await env.DB.prepare("SELECT stats FROM gameplay_v3_profile_stats WHERE map_script_sha256=? AND difficulty=? AND profile=?")
    .bind(hash, difficulty, profile).first() : await env.DB.prepare("SELECT stats FROM gameplay_v3_live_stats WHERE map_script_sha256 = ? AND difficulty = ?")
    .bind(hash, difficulty).first();
  if (!row) return reply(profile ? { ...emptyStats(), generatedAt: new Date(now(env)).toISOString(), difficulties: { [difficulty]: 0 } } : emptyStats(), 200, true, profile);
  const stats = JSON.parse(row.stats);
  // Validate on output as well: corrupted or externally modified storage is not public data.
  if (!validateStats(stats, difficulty, profile)) return reply(null, 503);
  return reply(stats, 200, true, profile);
}
