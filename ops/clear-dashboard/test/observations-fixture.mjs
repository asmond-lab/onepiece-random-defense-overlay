import { readFile } from "node:fs/promises";
import { DatabaseSync } from "node:sqlite";
import worker from "../worker/src/index.mjs";

export const endpoint = "https://fixture.invalid/v4/observations";
export function packet(overrides = {}) {
  return { schemaVersion: 4, consentVersion: 4, packetId: "1".repeat(32), sessionId: "2".repeat(32),
    appVersion: "1.0.2-test.9", mapVersion: "2.320", datasetFingerprint: "a".repeat(64), gameVersion: "3.0.0.24268",
    source: "live", events: [{ sequence: 1, elapsedMs: 100, kind: "inventory", state: "fresh", inventory: { "rawcode:hfoo": 2 },
      round: 27, sourceRevision: 4, durationMs: 500, ageMs: 550, reasonCode: "none", lane: "basic" }], ...overrides };
}
export async function setup(t) {
  const sqlite = new DatabaseSync(":memory:");
  t.after(() => sqlite.close());
  for (const name of ["0002_gameplay_v3.sql", "0003_gameplay_learning.sql", "0004_bullet_guide_learning.sql", "0005_observed_telemetry.sql"])
    sqlite.exec(await readFile(new URL("../worker/migrations/" + name, import.meta.url), "utf8"));
  let pending = Promise.resolve();
  const db = {
    prepare(sql) {
      return { sql, values: [], bind(...values) { this.values = values; return this; },
        async run() { return { success: true, meta: sqlite.prepare(sql).run(...this.values) }; },
        async all() { return { success: true, results: sqlite.prepare(sql).all(...this.values) }; },
        async first() { return sqlite.prepare(sql).get(...this.values) ?? null; } };
    },
    batch(statements) {
      const result = pending.then(() => {
        sqlite.exec("BEGIN");
        try {
          const results = statements.map(item => ({ success: true, results: [], meta: sqlite.prepare(item.sql).run(...item.values) }));
          sqlite.exec("COMMIT"); return results;
        } catch (error) { sqlite.exec("ROLLBACK"); throw error; }
      });
      pending = result.catch(() => {});
      return result;
    },
  };
  const env = { DB: db, CLOCK: () => new Date("2026-09-15T12:00:00Z") };
  const call = (method = "POST", body, headers = {}, suffix = "") => worker.fetch(new Request(endpoint + suffix, {
    method, headers, ...(body === undefined ? {} : { body: typeof body === "string" ? body : JSON.stringify(body) }),
  }), env);
  return { sqlite, env, call, post: body => call("POST", body),
    count: () => sqlite.prepare("SELECT count(*) AS n FROM telemetry_v4_packets").get().n };
}
