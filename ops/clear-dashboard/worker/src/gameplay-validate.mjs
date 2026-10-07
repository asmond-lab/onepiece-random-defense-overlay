export const MAX_BODY_BYTES = 512 * 1024;
// Existing recognition names and v2 wire names are distinct cohorts, never aliases.
export const DIFFICULTIES = new Set(["쉬움", "보통", "어려움", "악몽", "지옥", "신", "easy", "normal", "hard", "nightmare", "hell", "god", "god-plus", "unknown"]);
const WISPS = new Set(["e016", "e017", "e019", "e0IX", "e018", "e01A"]);
const ACTIONS = new Set(["Waiting", "Recognition", "Craft", "Gather", "Story", "Reward", "Navigation", "Economy", "Upgrade", "Maintain", "Finished", "Item"]);
const EVIDENCE = new Set(["native-observed", "inventory-matched", "observation-only", "user-confirmed", "recommendation", "unknown"]);
const MODES = new Set(["Normal", "Manual", "Beginner", "Guide"]);
// Unlike $, this end assertion cannot accept a trailing newline.
const VERSION = /^[A-Za-z0-9_.-]{1,80}(?![\s\S])/;
const UNIT = /^[A-Za-z0-9_:-]{1,80}(?![\s\S])/;
const HEX32 = /^[a-f0-9]{32}(?![\s\S])/;
const HEX64 = /^[a-f0-9]{64}(?![\s\S])/;
const common = ["sequence", "recognitionRevision", "elapsedMs", "round", "completedStory", "mode", "guideNumber", "kind", "evidence"];
const fields = {
  observation: [["inventory", "rewardWisps", "resources"], ["gambleFailures", "gambleCounters"]],
  recommendation: [["action", "goalUnitIds"], ["targetUnitId", "selection"]],
  craft: [["unitId", "count", "consumed"], []],
  selection: [["wispId", "count", "outputs"], []],
  gamble: [["gambleType", "count", "cost", "outputs"], []],
  outcome: [["outcome", "outcomeSource", "inventory", "goalUnitIds"], []],
};
const object = v => v !== null && typeof v === "object" && !Array.isArray(v);
const integer = (v, min = 0, max = Number.MAX_SAFE_INTEGER) => Number.isSafeInteger(v) && v >= min && v <= max;
const unit = v => typeof v === "string" && UNIT.test(v);
const version = v => typeof v === "string" && VERSION.test(v);
function keys(v, required, optional = []) {
  return object(v) && required.every(k => Object.hasOwn(v, k)) && Object.keys(v).every(k => required.includes(k) || optional.includes(k));
}
function dictionary(v, keyCheck = unit, valueCheck = integer, max = 512) {
  return object(v) && Object.keys(v).length <= max && Object.entries(v).every(([k, n]) => keyCheck(k) && valueCheck(n));
}
const units = v => dictionary(v);
const resources = v => dictionary(v, k => ["gold", "lumber", "trait-points"].includes(k));
const goals = v => Array.isArray(v) && v.length <= 8 && v.every(unit);
export function validCohort(hash, difficulty) {
  return typeof hash === "string" && HEX64.test(hash) && DIFFICULTIES.has(difficulty);
}
export function validatePacket(p) {
  if (!keys(p, ["schemaVersion", "consentVersion", "packetId", "matchId", "chunkIndex", "appVersion", "mapVersion", "mapScriptSha256", "profileVersion", "difficulty", "events"])) return false;
  if (p.schemaVersion !== 3 || p.consentVersion !== 3 || typeof p.packetId !== "string" || !HEX32.test(p.packetId) ||
      typeof p.matchId !== "string" || !HEX32.test(p.matchId) || !integer(p.chunkIndex) ||
      ![p.appVersion, p.mapVersion, p.profileVersion].every(version) || !validCohort(p.mapScriptSha256, p.difficulty) ||
      !Array.isArray(p.events) || p.events.length < 1 || p.events.length > 64) return false;
  let sequence = 0;
  for (const e of p.events) {
    if (!object(e) || !Object.hasOwn(fields, e.kind)) return false;
    const [required, optional] = fields[e.kind];
    if (!keys(e, [...common, ...required], optional) || !integer(e.sequence, 1) || e.sequence <= sequence ||
        !integer(e.recognitionRevision) || !integer(e.elapsedMs) || !integer(e.round, 1, 65) ||
        !integer(e.completedStory, 0, 14) || !integer(e.guideNumber, 0, 99) || !MODES.has(e.mode) || !EVIDENCE.has(e.evidence)) return false;
    sequence = e.sequence;
    for (const k of ["inventory", "consumed", "outputs", "selection"]) if (Object.hasOwn(e, k) && !units(e[k])) return false;
    for (const k of ["resources", "cost"]) if (Object.hasOwn(e, k) && !resources(e[k])) return false;
    for (const k of ["unitId", "targetUnitId"]) if (Object.hasOwn(e, k) && !unit(e[k])) return false;
    for (const k of ["count", "gambleFailures"]) if (Object.hasOwn(e, k) && !integer(e[k])) return false;
    if (Object.hasOwn(e, "gambleCounters") && !dictionary(e.gambleCounters,
      k => ["low", "middle", "high", "world", "absalom", "lumberWisp"].includes(k),
      c => keys(c, ["attempts", "successes", "failures"]) &&
        [c.attempts, c.successes, c.failures].every(n => integer(n)) &&
        c.attempts === c.successes + c.failures, 6)) return false;
    if (Object.hasOwn(e, "goalUnitIds") && !goals(e.goalUnitIds)) return false;
    if (e.kind === "observation" && !dictionary(e.rewardWisps, k => WISPS.has(k))) return false;
    if (e.kind === "selection" && !WISPS.has(e.wispId)) return false;
    if (e.kind === "recommendation" && !ACTIONS.has(e.action)) return false;
    if (e.kind === "gamble" && !["low", "medium", "high"].includes(e.gambleType)) return false;
    if (e.kind === "outcome" && (!["clear", "fail", "interrupted"].includes(e.outcome) ||
        !["mapSettlement", "clearRound", "unitWipe", "appExit", "unknown"].includes(e.outcomeSource))) return false;
  }
  return true;
}
const statsCount = v => integer(v, 0, 2147483647);
const weight = v => typeof v === "number" && Number.isFinite(v) && v >= -0.1 && v <= 0.1;
// LiveStats uses case-insensitive dictionaries. Reject ambiguous aliases rather than overwrite.
function statsDictionary(v, valueCheck) {
  return dictionary(v, unit, valueCheck) && new Set(Object.keys(v).map(k => k.toLowerCase())).size === Object.keys(v).length;
}
export const BULLET_PROFILE = "bullet-guide-1";
export const validProfile = profile => profile === null || profile === BULLET_PROFILE;
export function validateStats(s, difficulty, profile = null) {
  if (!validProfile(profile)) return false;
  if (!keys(s, ["schemaVersion", "totalRecords", "labeledRecords", "goals"], ["weights", "goalWeights", "generatedAt", "difficulties"]) ||
      s.schemaVersion !== 1 || !statsCount(s.totalRecords) || !statsCount(s.labeledRecords) || s.labeledRecords > s.totalRecords) return false;
  if (!statsDictionary(s.goals, g => keys(g, ["plays", "labeled", "clears"], ["failHeavyUnits", "adherenceMean"]) &&
      [g.plays, g.labeled, g.clears].every(statsCount) && g.clears <= g.labeled && g.labeled <= g.plays &&
      g.plays <= s.totalRecords && g.labeled <= s.labeledRecords &&
      (!Object.hasOwn(g, "failHeavyUnits") || (Array.isArray(g.failHeavyUnits) && g.failHeavyUnits.length <= 512 && g.failHeavyUnits.every(unit))) &&
      (!Object.hasOwn(g, "adherenceMean") || g.adherenceMean === null || (typeof g.adherenceMean === "number" && Number.isFinite(g.adherenceMean) && g.adherenceMean >= 0 && g.adherenceMean <= 1)))) return false;
  if (Object.hasOwn(s, "weights") && !statsDictionary(s.weights, weight)) return false;
  if (Object.hasOwn(s, "goalWeights") && !statsDictionary(s.goalWeights, w => statsDictionary(w, weight))) return false;
  if (Object.hasOwn(s, "generatedAt") && !(typeof s.generatedAt === "string" && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z(?![\s\S])/.test(s.generatedAt) && Number.isFinite(Date.parse(s.generatedAt)))) return false;
  if (Object.hasOwn(s, "difficulties") && !dictionary(s.difficulties, k => k === difficulty, n => statsCount(n) && n <= s.totalRecords)) return false;
  if (profile !== null && (Object.keys(s.goals).some(goal => goal !== "rawcode:180h") ||
      Object.keys(s.weights ?? {}).length > 0 || Object.keys(s.goalWeights ?? {}).some(goal => goal !== "rawcode:180h"))) return false;
  if (profile !== null) {
    if (!keys(s, ["schemaVersion", "totalRecords", "labeledRecords", "goals", "weights", "goalWeights", "generatedAt", "difficulties"]) ||
        Object.values(s.goals).reduce((n, g) => n + g.plays, 0) !== s.totalRecords ||
        Object.values(s.goals).reduce((n, g) => n + g.labeled, 0) !== s.labeledRecords ||
        Object.keys(s.difficulties).length !== 1 || s.difficulties[difficulty] !== s.totalRecords) return false;
    for (const g of Object.values(s.goals))
      if (!keys(g, ["plays", "labeled", "clears", "adherenceMean", "failHeavyUnits"]) ||
          new Set(g.failHeavyUnits).size !== g.failHeavyUnits.length) return false;
    for (const [goal, weights] of Object.entries(s.goalWeights)) {
      const g = s.goals[goal];
      if (!g || Object.keys(weights).length > 0 && (g.labeled < 30 || g.clears === 0 || g.clears === g.labeled)) return false;
    }
  }
  return true;
}
export function validateStatsEnvelope(p) {
  return keys(p, ["schemaVersion", "mapScriptSha256", "difficulty", "stats"], ["profile"]) && p.schemaVersion === 3 &&
    (!Object.hasOwn(p, "profile") || p.profile === BULLET_PROFILE) &&
    validCohort(p.mapScriptSha256, p.difficulty) && validateStats(p.stats, p.difficulty, p.profile ?? null);
}
export function emptyStats() {
  return { schemaVersion: 1, totalRecords: 0, labeledRecords: 0, goals: {}, weights: {}, goalWeights: {} };
}
export function canonical(value) {
  if (Array.isArray(value)) return `[${value.map(canonical).join(",")}]`;
  if (object(value)) return `{${Object.keys(value).sort().map(k => `${JSON.stringify(k)}:${canonical(value[k])}`).join(",")}}`;
  return JSON.stringify(value);
}

export class BodyError extends Error {
  constructor(status) { super("Invalid request body"); this.status = status; }
}
export async function readJson(request) {
  if (Number(request.headers.get("Content-Length")) > MAX_BODY_BYTES) throw new BodyError(413);
  const reader = request.body?.getReader();
  if (!reader) throw new BodyError(400);
  const chunks = [];
  let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > MAX_BODY_BYTES) { await reader.cancel(); throw new BodyError(413); }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  try {
    const text = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
    const parsed = JSON.parse(text);
    // JSON.parse accepts duplicate property names; reject them (including escaped aliases).
    const stack = [];
    const token = /"(?:\\.|[^"\\])*"|[{}\[\]]/g;
    for (const match of text.matchAll(token)) {
      const t = match[0];
      if (t === "{" || t === "[") {
        stack.push(t === "{" ? new Set() : null);
        if (stack.length > 16) throw new BodyError(400);
      } else if (t === "}" || t === "]") stack.pop();
      else if (text.slice(match.index + t.length).match(/^\s*:/)) {
        const key = JSON.parse(t);
        const seen = stack.at(-1);
        if (seen.has(key)) throw new BodyError(400);
        seen.add(key);
      }
    }
    return parsed;
  } catch { throw new BodyError(400); }
}
