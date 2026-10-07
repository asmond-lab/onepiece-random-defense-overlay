const STATUS = new Set(["ok", "empty", "stale", "error"]);
const WINDOW_KEYS = ["1h", "24h", "7d"];

export class SnapshotContractError extends Error {
  constructor() {
    super("Dashboard snapshot contract is invalid");
    this.name = "SnapshotContractError";
  }
}

export function parseSnapshot(value) {
  if (!isObject(value) || value.schemaVersion !== 1 || !STATUS.has(value.status)) {
    throw new SnapshotContractError();
  }
  if (!isObject(value.windows) || !WINDOW_KEYS.every((key) => validWindow(value.windows[key]))) {
    throw new SnapshotContractError();
  }
  if (
    !isObject(value.distributions)
    || !validDistribution(value.distributions.byDifficulty)
    || !validDistribution(value.distributions.byGoal)
  ) {
    throw new SnapshotContractError();
  }
  if (
    !nullableTimestamp(value.recentClearAt)
    || !nullableNonNegativeNumber(value.dataDelaySeconds)
    || !timestamp(value.refreshedAt)
    || !validError(value.status, value.error)
  ) {
    throw new SnapshotContractError();
  }
  return value;
}

export function connectionErrorSnapshot(now, _detail) {
  return errorSnapshot(now, "connection_error");
}

export function configurationErrorSnapshot(now) {
  return errorSnapshot(now, "configuration_error");
}

function errorSnapshot(now, code) {
  return {
    schemaVersion: 1,
    status: "error",
    windows: {
      "1h": { clearCount: 0 },
      "24h": { clearCount: 0 },
      "7d": { clearCount: 0 },
    },
    distributions: { byDifficulty: [], byGoal: [] },
    recentClearAt: null,
    dataDelaySeconds: null,
    refreshedAt: now.toISOString(),
    error: {
      code,
      message: code === "connection_error"
        ? "Dashboard source is unavailable"
        : "Dashboard source configuration is invalid",
    },
  };
}

function validWindow(value) {
  return isObject(value) && nonNegativeInteger(value.clearCount);
}

function validDistribution(value) {
  return Array.isArray(value) && value.every(
    (entry) => isObject(entry)
      && typeof entry.key === "string"
      && entry.key.length > 0
      && nonNegativeInteger(entry.clearCount),
  );
}

function validError(status, value) {
  if (status === "error") {
    return isObject(value)
      && ["connection_error", "configuration_error"].includes(value.code)
      && typeof value.message === "string";
  }
  return value === null;
}

function timestamp(value) {
  return typeof value === "string" && Number.isFinite(Date.parse(value));
}

function nullableTimestamp(value) {
  return value === null || timestamp(value);
}

function nullableNonNegativeNumber(value) {
  return value === null || (typeof value === "number" && Number.isFinite(value) && value >= 0);
}

function nonNegativeInteger(value) {
  return Number.isInteger(value) && value >= 0;
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
