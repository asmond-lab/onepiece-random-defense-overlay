const EXACT_KEYS = new Set([
  "schemaVersion", "appVersion", "mapVersion", "difficulty", "damageLane",
  "goalTierFamily", "surface", "urgency", "outcome", "matchCount",
  "observedObjects", "completedTops", "readyScans", "waitingScans",
  "transientScans", "unsupportedScans",
]);
const VERSION = /^[0-9]+(?:\.[0-9]+){0,3}(?:-[A-Za-z0-9.-]+)?$/;
const DIFFICULTIES = new Set(["easy", "normal", "hard", "nightmare", "hell", "god", "god-plus", "unknown"]);
const DAMAGE_LANES = new Set(["physical", "magic", "unknown"]);
const TIER_FAMILIES = new Set(["전설", "히든", "변화된", "랜덤전용", "제한", "초월", "불멸", "영원", "unknown"]);
const SURFACES = new Set(["fast-rare", "story-legend", "top-navigation", "unknown"]);
const URGENCIES = new Set(["none", "boss-survival", "story-deadline"]);
const OUTCOMES = new Set(["unknown", "clear", "fail"]);
const COUNT_BUCKETS = new Set(["0", "1", "2-4", "5-9", "10-24", "25+"]);
const SENSITIVE_VALUE = /(?:rawcode:|[12][0-9]{3}-[01][0-9]-[0-3][0-9]T|[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12})/i;

export function validateRecord(record) {
  if (!record || typeof record !== "object" || Array.isArray(record)) return "not an object";
  const keys = Object.keys(record);
  if (keys.length !== EXACT_KEYS.size || keys.some((key) => !EXACT_KEYS.has(key))) {
    return "property set invalid";
  }
  if (record.schemaVersion !== 2) return "schemaVersion must be 2";
  if (!version(record.appVersion) || !version(record.mapVersion)) return "version invalid";
  if (!DIFFICULTIES.has(record.difficulty)) return "difficulty invalid";
  if (!DAMAGE_LANES.has(record.damageLane)) return "damageLane invalid";
  if (!TIER_FAMILIES.has(record.goalTierFamily)) return "goalTierFamily invalid";
  if (!SURFACES.has(record.surface)) return "surface invalid";
  if (!URGENCIES.has(record.urgency)) return "urgency invalid";
  if (!OUTCOMES.has(record.outcome)) return "outcome invalid";
  if (record.matchCount !== 1) return "matchCount must be 1";
  for (const key of ["observedObjects", "completedTops", "readyScans", "waitingScans", "transientScans", "unsupportedScans"]) {
    if (!COUNT_BUCKETS.has(record[key])) return `${key} invalid`;
  }
  if (Object.values(record).some((value) => typeof value === "string" && SENSITIVE_VALUE.test(value))) {
    return "sensitive value invalid";
  }
  return null;
}

function version(value) {
  return value === "unknown"
    || (typeof value === "string" && value.length <= 32 && VERSION.test(value));
}
