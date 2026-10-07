import { BodyError, canonical } from "./gameplay-validate.mjs";
import { readObservationJson, validateObservationPacket } from "./observations-validate.mjs";

const RETENTION_MS = 30 * 24 * 60 * 60 * 1000;
const headers = {
  "X-Orand-Telemetry-Schema": "4",
  "Cache-Control": "no-store",
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "OPTIONS, POST",
  "Access-Control-Allow-Headers": "Content-Type",
  "Access-Control-Expose-Headers": "X-Orand-Telemetry-Schema, X-Orand-Telemetry-Accepted, X-Orand-Telemetry-Enabled",
};
const now = env => (env.CLOCK?.() ?? new Date()).getTime();
function reply(body, status = 200, enabled = true) {
  const options = { status, headers: { ...headers,
    "X-Orand-Telemetry-Enabled": String(enabled),
    "X-Orand-Telemetry-Accepted": String(enabled && status >= 200 && status < 300) } };
  return body === null ? new Response(null, options) : Response.json(body, options);
}
export async function observations(request, env, url) {
  const enabled = String(env.TELEMETRY_ENABLED ?? "true").toLowerCase() !== "false" &&
    String(env.OBSERVATIONS_V4_ENABLED ?? "true").toLowerCase() !== "false";
  if (!enabled) return reply(null, 503, false);
  if (url.search) return reply(null, 400);
  if (request.method === "OPTIONS") return reply(null, 204);
  if (request.method !== "POST") return reply(null, 405);
  try {
    const packet = await readObservationJson(request);
    if (!validateObservationPacket(packet)) return reply(null, 400);
    const payload = canonical(packet);
    const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(payload));
    const hash = Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, "0")).join("");
    const receivedAt = now(env);
    await env.DB.batch([
      env.DB.prepare(`INSERT INTO telemetry_v4_packets(packet_id, session_id, app_version, source, received_at, content_hash, payload)
        VALUES(?, ?, ?, ?, ?, ?, ?) ON CONFLICT(packet_id) DO NOTHING`)
        .bind(packet.packetId, packet.sessionId, packet.appVersion, packet.source, receivedAt, hash, payload),
      env.DB.prepare(`DELETE FROM telemetry_v4_packets WHERE packet_id IN
        (SELECT packet_id FROM telemetry_v4_packets WHERE received_at <= ? ORDER BY received_at LIMIT 100)`)
        .bind(receivedAt - RETENTION_MS),
    ]);
    return reply({ schemaVersion: 4, packetId: packet.packetId, accepted: true });
  } catch (error) {
    if (error instanceof BodyError) return reply(null, error.status);
    if (String(error?.message).includes("v4_packet_conflict")) return reply(null, 409);
    return reply(null, 503);
  }
}
export async function purgeObservations(env) {
  await env.DB.prepare("DELETE FROM telemetry_v4_packets WHERE received_at <= ?")
    .bind(now(env) - RETENTION_MS).run();
}
