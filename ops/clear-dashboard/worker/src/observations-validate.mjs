import { BodyError } from "./gameplay-validate.mjs";

export const MAX_OBSERVATION_BYTES = 256 * 1024;
const UNIT = /^[A-Za-z0-9_:-]{1,80}(?![\s\S])/;
const VERSION = /^[A-Za-z0-9_.-]{1,80}(?![\s\S])/;
const HEX32 = /^[a-f0-9]{32}(?![\s\S])/;
const HEX64 = /^[a-f0-9]{64}(?![\s\S])/;
const RECOGNITION_STATES = new Set(["fresh", "expired", "read-failed", "rejected", "scanning", "paused", "auto-off", "game-unavailable"]);
const SESSION_STATES = new Set(["session-start", "session-reset", "app-exit"]);
const REASONS = new Set(["none", "freshness", "binding", "unavailable", "unsupported", "configuration", "cancelled", "read-error", "unknown"]);
const STAGES = new Set(["Rare", "Legend", "Upper", "Utility"]);
const requiredPacket = ["schemaVersion", "consentVersion", "packetId", "sessionId", "appVersion", "mapVersion", "datasetFingerprint", "gameVersion", "source", "events"];
const optionalEvent = ["round", "durationMs", "ageMs", "gapMs", "sourceRevision", "inventory", "targetUnitId", "stage", "reasonCode", "lane"];
const object = value => value !== null && typeof value === "object" && !Array.isArray(value);
const integer = (value, min = 0, max = Number.MAX_SAFE_INTEGER) => Number.isSafeInteger(value) && value >= min && value <= max;
const matches = (value, pattern) => typeof value === "string" && pattern.test(value);
function keys(value, required, optional = []) {
  return object(value) && required.every(key => Object.hasOwn(value, key)) &&
    Object.keys(value).every(key => required.includes(key) || optional.includes(key));
}
function inventory(value) {
  return object(value) && Object.keys(value).length <= 512 &&
    Object.entries(value).every(([key, count]) => matches(key, UNIT) && integer(count, 1, 10000));
}
function event(value) {
  if (!keys(value, ["sequence", "elapsedMs", "kind", "state"], optionalEvent) ||
      !integer(value.sequence, 1) || !integer(value.elapsedMs)) return false;
  if (Object.hasOwn(value, "inventory") && (value.kind !== "inventory" || value.state !== "fresh" || !inventory(value.inventory))) return false;
  switch (value.kind) {
    case "inventory": if (value.state !== "fresh" || !Object.hasOwn(value, "inventory")) return false; break;
    case "recognition": if (!RECOGNITION_STATES.has(value.state)) return false; break;
    case "selection": if (value.state !== "target-selected" || !Object.hasOwn(value, "targetUnitId")) return false; break;
    case "recommendation": if (value.state !== "recommended") return false; break;
    case "session": if (!SESSION_STATES.has(value.state)) return false; break;
    default: return false;
  }
  if (Object.hasOwn(value, "round") && !integer(value.round, 1, 65)) return false;
  for (const key of ["durationMs", "ageMs"])
    if (Object.hasOwn(value, key) && !integer(value[key], 0, 86400000)) return false;
  for (const key of ["gapMs", "sourceRevision"])
    if (Object.hasOwn(value, key) && !integer(value[key])) return false;
  return (!Object.hasOwn(value, "lane") || ["basic", "full", "presentation", "scan"].includes(value.lane)) &&
    (!Object.hasOwn(value, "targetUnitId") || matches(value.targetUnitId, UNIT)) &&
    (!Object.hasOwn(value, "stage") || STAGES.has(value.stage)) &&
    (!Object.hasOwn(value, "reasonCode") || REASONS.has(value.reasonCode));
}
export function validateObservationPacket(value) {
  if (!keys(value, requiredPacket) || value.schemaVersion !== 4 || value.consentVersion !== 4 ||
      !matches(value.packetId, HEX32) || !matches(value.sessionId, HEX32) || !matches(value.appVersion, VERSION) ||
      !matches(value.datasetFingerprint, HEX64) || value.mapVersion !== "2.320" || value.gameVersion !== "3.0.0.24268" ||
      !["live", "synthetic-validation"].includes(value.source) || !Array.isArray(value.events) ||
      value.events.length < 1 || value.events.length > 64) return false;
  let previous = 0;
  for (const item of value.events) {
    if (!event(item) || item.sequence <= previous) return false;
    previous = item.sequence;
  }
  return true;
}

export async function readObservationJson(request) {
  if (Number(request.headers.get("Content-Length")) > MAX_OBSERVATION_BYTES) throw new BodyError(413);
  const reader = request.body?.getReader();
  if (!reader) throw new BodyError(400);
  const chunks = [];
  let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > MAX_OBSERVATION_BYTES) { await reader.cancel(); throw new BodyError(413); }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  try {
    const text = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
    const parsed = JSON.parse(text);
    const stack = [];
    const token = /"(?:\\.|[^"\\])*"|[{}\[\]]/g;
    for (const match of text.matchAll(token)) {
      const value = match[0];
      if (value === "{" || value === "[") {
        stack.push(value === "{" ? new Set() : null);
        if (stack.length > 16) throw new BodyError(400);
      } else if (value === "}" || value === "]") stack.pop();
      else if (/^\s*:/.test(text.slice(match.index + value.length))) {
        const key = JSON.parse(value), seen = stack.at(-1);
        if (seen.has(key)) throw new BodyError(400);
        seen.add(key);
      }
    }
    return parsed;
  } catch { throw new BodyError(400); }
}
