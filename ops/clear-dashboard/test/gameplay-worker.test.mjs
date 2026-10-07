import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { DatabaseSync } from "node:sqlite";
import test from "node:test";
import worker from "../worker/src/index.mjs";

const origin = "https://fixture.invalid";
const auth = { Authorization: "Bearer fixture-collector" };
const hash = "a".repeat(64);
const bulletPath = `/v3/live-stats?mapScriptSha256=${hash}&difficulty=${encodeURIComponent("신")}&profile=bullet-guide-1`;
function packet(overrides = {}) {
  return { schemaVersion: 3, consentVersion: 3, packetId: "1".repeat(32), matchId: "2".repeat(32),
    chunkIndex: 0, appVersion: "1.2.3", mapVersion: "1.0", profileVersion: "unknown",
    mapScriptSha256: hash, difficulty: "신", events: [{ sequence: 1, recognitionRevision: 1,
      elapsedMs: 100, round: 1, completedStory: 0, mode: "Guide", guideNumber: 1,
      kind: "observation", evidence: "native-observed", inventory: { luffy_common: 1 },
      rewardWisps: { e018: 2 }, resources: { gold: 100 }, gambleFailures: 0 }], ...overrides };
}
function stats() {
  return { schemaVersion: 1, totalRecords: 20, labeledRecords: 15,
    goals: { luffy_common: { plays: 20, labeled: 15, clears: 10 } },
    weights: { luffy_common: 0.1 }, goalWeights: { luffy_common: { luffy_common: -0.1 } } };
}
async function setup(t, migrateBullet = true) {
  const sqlite = new DatabaseSync(":memory:");
  t.after(() => sqlite.close());
  sqlite.exec(await readFile(new URL("../worker/migrations/0002_gameplay_v3.sql", import.meta.url), "utf8"));
  sqlite.exec(await readFile(new URL("../worker/migrations/0003_gameplay_learning.sql", import.meta.url), "utf8"));
  if (migrateBullet) sqlite.exec(await readFile(new URL("../worker/migrations/0004_bullet_guide_learning.sql", import.meta.url), "utf8"));
  sqlite.exec(await readFile(new URL("../worker/migrations/0005_observed_telemetry.sql", import.meta.url), "utf8"));
  const db = {
    prepare(sql) {
      return { sql, values: [], bind(...values) { this.values = values; return this; },
        async run() { return sqlite.prepare(sql).run(...this.values); },
        async all() { return { results: sqlite.prepare(sql).all(...this.values) }; },
        async first() { return sqlite.prepare(sql).get(...this.values) ?? null; } };
    },
    async batch(statements) {
      sqlite.exec("BEGIN");
      try {
        const results = [];
        for (const statement of statements) results.push(await statement.run());
        sqlite.exec("COMMIT");
        return results;
      } catch (error) { sqlite.exec("ROLLBACK"); throw error; }
    },
  };
  const env = { DB: db, COLLECTOR_KEY: "fixture-collector", CLOCK: () => new Date("2026-09-09T00:00:00Z") };
  const call = (path, method = "GET", body, headers = {}) => worker.fetch(new Request(origin + path, {
    method, headers, ...(body === undefined ? {} : { body: typeof body === "string" ? body : JSON.stringify(body) }),
  }), env);
  return { sqlite, env, call, post: (body) => call("/v3/gameplay", "POST", body) };
}

test("Cloudflare scheduled invocation learns complete cohorts without a PC collector", async t => {
  const { post, call, env, sqlite } = await setup(t);
  for (let i = 1; i <= 40; i++) {
    const p = packet({ packetId: i.toString(16).padStart(32, "0"), matchId: i.toString(16).padStart(32, "0") });
    const clear = i <= 20, inventory = { [clear ? "support" : "other"]: 1 };
    p.events[0].inventory = inventory;
    p.events[0].rewardWisps = { e0IX: 1, e01A: 1 };
    p.events.push({ ...p.events[0], sequence: 2, kind: "outcome",
      evidence: clear ? "native-observed" : "observation-only", outcome: clear ? "clear" : "fail",
      outcomeSource: clear ? "mapSettlement" : "unitWipe", goalUnitIds: ["goal"] });
    delete p.events[1].rewardWisps; delete p.events[1].resources; delete p.events[1].gambleFailures;
    assert.equal((await post(p)).status, 200);
  }
  await worker.scheduled({}, env);
  let r = await call(`/v3/live-stats?mapScriptSha256=${hash}&difficulty=${encodeURIComponent("신")}`);
  assert.equal((await r.json()).totalRecords, 0, "partial backlog must not publish");
  await worker.scheduled({}, env);
  r = await call(`/v3/live-stats?mapScriptSha256=${hash}&difficulty=${encodeURIComponent("신")}`);
  const learned = await r.json();
  assert.equal(learned.totalRecords, 40);
  assert.equal(learned.goalWeights.goal.support, 0.1);
  assert.equal(learned.goalWeights.goal.other, -0.1);
  assert.equal(learned.goals.goal.adherenceMean, null);
  await worker.scheduled({}, env);
  assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_learning_sessions").get().n, 40);
  env.CLOCK = () => new Date("2026-10-10T00:00:00Z");
  await worker.scheduled({}, env);
  r = await call(`/v3/live-stats?mapScriptSha256=${hash}&difficulty=${encodeURIComponent("신")}`);
  assert.equal((await r.json()).totalRecords, 0, "expired cohorts must not retain stale weights");
});

test("scheduled Bullet profile excludes whole mixed matches without changing generic totals", async t => {
  const { post, call, env, sqlite } = await setup(t);
  for (let i = 1; i <= 40; i++) {
    const p = packet({ packetId: i.toString(16).padStart(32, "0"), matchId: i.toString(16).padStart(32, "0") });
    const clear = i <= 20;
    p.events[0].inventory = { [clear ? "rawcode:H30h" : "rawcode:M30h"]: 1 };
    p.events.push({ sequence: 2, recognitionRevision: 2, elapsedMs: 200, round: 10, completedStory: 4,
      mode: "Guide", guideNumber: 1, kind: "recommendation", evidence: "recommendation",
      action: "Gather", targetUnitId: "rawcode:530h", goalUnitIds: ["rawcode:180h"] });
    p.events.push({ sequence: 3, recognitionRevision: 3, elapsedMs: 300, round: 52, completedStory: 13,
      mode: "Guide", guideNumber: 1, kind: "outcome", evidence: "native-observed",
      outcome: clear ? "clear" : "fail", outcomeSource: "mapSettlement",
      inventory: p.events[0].inventory, goalUnitIds: ["rawcode:180h"] });
    if (i === 1) p.events[0].mode = "Normal";
    if (i === 2) p.events[1].guideNumber = 2;
    if (i === 3) p.events[2].mode = "Manual";
    if (i === 4) for (const e of p.events) if (e.goalUnitIds) e.goalUnitIds = ["other"];
    assert.equal((await post(p)).status, 200);
  }
  await worker.scheduled({}, env);
  assert.equal((await (await call(bulletPath)).json()).totalRecords, 0);
  await worker.scheduled({}, env);
  const response = await call(bulletPath);
  assert.equal(response.status, 200);
  assert.equal(response.headers.get("X-Orand-Stats-Profile"), "bullet-guide-1");
  const result = await response.json();
  assert.equal(result.totalRecords, 36);
  assert.equal(result.goals["rawcode:180h"].plays, 36);
  assert.equal(result.goalWeights["rawcode:180h"]["rawcode:H30h"], 0.1);
  assert.equal((await (await call(bulletPath.split("&profile=")[0])).json()).totalRecords, 40);
  await worker.scheduled({}, env);
  assert.equal((await (await call(bulletPath)).json()).totalRecords, 36);
  assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_learning_sessions").get().n, 40);
  env.CLOCK = () => new Date("2026-10-10T00:00:00Z");
  await worker.scheduled({}, env);
  assert.equal((await (await call(bulletPath)).json()).totalRecords, 0);
});

test("profile API rejects unknown and duplicate profile identity", async t => {
  const { call } = await setup(t);
  assert.equal((await call(bulletPath)).status, 200);
  assert.equal((await call(bulletPath.replace("bullet-guide-1", "other"))).status, 400);
  assert.equal((await call(bulletPath + "&profile=bullet-guide-1")).status, 400);
});

test("additive migration backfills retained raw even after the old cursor has advanced", async t => {
  const { post, call, env, sqlite } = await setup(t, false);
  const p = packet();
  p.events.push({ sequence: 2, recognitionRevision: 2, elapsedMs: 200, round: 52, completedStory: 13,
    mode: "Guide", guideNumber: 1, kind: "outcome", evidence: "native-observed", outcome: "fail",
    outcomeSource: "mapSettlement", inventory: { "rawcode:U20h": 1 }, goalUnitIds: ["rawcode:180h"] });
  assert.equal((await post(p)).status, 200);
  sqlite.exec("UPDATE gameplay_v3_learning_job SET cursor=(SELECT max(cursor) FROM gameplay_v3_raw)");
  sqlite.exec(await readFile(new URL("../worker/migrations/0004_bullet_guide_learning.sql", import.meta.url), "utf8"));
  assert.equal(sqlite.prepare("SELECT bullet_backfill FROM gameplay_v3_learning_job").get().bullet_backfill, 1);
  await worker.scheduled({}, env);
  assert.equal((await (await call(bulletPath)).json()).totalRecords, 1);
  assert.equal(sqlite.prepare("SELECT bullet_backfill FROM gameplay_v3_learning_job").get().bullet_backfill, 0);
  await worker.scheduled({}, env);
  assert.equal((await (await call(bulletPath)).json()).totalRecords, 1);
});

test("profile publication validates strict projection and cannot overwrite generic namespace", async t => {
  const { call, sqlite } = await setup(t);
  const projection = { schemaVersion: 1, generatedAt: "2026-09-09T00:00:00Z", totalRecords: 40, labeledRecords: 40,
    goals: { "rawcode:180h": { plays: 40, labeled: 40, clears: 20, adherenceMean: null, failHeavyUnits: [] } },
    weights: {}, goalWeights: { "rawcode:180h": { "rawcode:H30h": 0.1 } }, difficulties: { "신": 40 } };
  const body = { schemaVersion: 3, mapScriptSha256: hash, difficulty: "신", profile: "bullet-guide-1", stats: projection };
  assert.equal((await call("/v3/live-stats", "PUT", body, auth)).status, 200);
  assert.deepEqual(await (await call(bulletPath)).json(), projection);
  assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_live_stats").get().n, 0);
  for (const mutate of [s => delete s.generatedAt, s => s.totalRecords++,
    s => s.goals["rawcode:180h"].clears = 40, s => s.weights.other = 0.1,
    s => s.goalWeights.other = {}, s => s.difficulties = {}, s => s.schemaVersion = 2]) {
    const bad = structuredClone(body); mutate(bad.stats);
    assert.equal((await call("/v3/live-stats", "PUT", bad, auth)).status, 400);
  }
  sqlite.prepare("UPDATE gameplay_v3_profile_stats SET stats=?").run(JSON.stringify({ ...projection, weights: { other: 0.1 } }));
  assert.equal((await call(bulletPath)).status, 503);
});

test("v3 handshake and kill switch do not read a disabled upload", async () => {
  const response = await worker.fetch(new Request(origin + "/v3/gameplay", { method: "OPTIONS" }), {});
  assert.equal(response.status, 204);
  for (const [name, value] of Object.entries({ Schema: "3", Accepted: "true", Enabled: "true" }))
    assert.equal(response.headers.get("X-Orand-Telemetry-" + name), value);
  const disabled = await worker.fetch(new Request(origin + "/v3/gameplay", { method: "POST", body: "bad" }), { TELEMETRY_ENABLED: "false" });
  assert.equal(disabled.status, 503);
  assert.equal(disabled.headers.get("X-Orand-Telemetry-Accepted"), "false");
});

test("recommendation-free cohort preserves unknown adherence without rejecting publication", async t => {
  const { call } = await setup(t);
  const snapshot = stats();
  snapshot.goals.luffy_common.adherenceMean = null;
  const envelope = { schemaVersion: 3, mapScriptSha256: hash, difficulty: "신", stats: snapshot };
  assert.equal((await call("/v3/live-stats", "PUT", envelope, auth)).status, 200);
  const response = await call(`/v3/live-stats?mapScriptSha256=${hash}&difficulty=${encodeURIComponent("신")}`);
  assert.deepEqual(await response.json(), snapshot);
  for (const invalid of [-0.1, 1.1, "unknown", {}]) {
    snapshot.goals.luffy_common.adherenceMean = invalid;
    assert.equal((await call("/v3/live-stats", "PUT", envelope, auth)).status, 400);
  }
});

test("strict packet validation rejects privacy, enums, versions, counts and byte overflow before DB", async () => {
  const mutations = [p => p.nickname = "private", p => p.schemaVersion = 2, p => p.consentVersion = 2,
    p => p.packetId = "bad", p => p.appVersion = "C:/private", p => p.difficulty = "arbitrary",
    p => p.packetId += "\n", p => p.mapScriptSha256 += "\n", p => p.profileVersion += "\n",
    p => p.mapVersion = "v".repeat(81), p => p.events[0].inventory = { "unit\n": 1 },
    p => p.events[0].count = Number.MAX_SAFE_INTEGER + 1,
    p => p.events[0].chat = "private", p => p.events[0].inventory = { "a b": 1 },
    p => p.events[0].rewardWisps = { fake: 1 }, p => p.events[0].resources = { mana: 1 },
    p => p.events[0].round = 66, p => p.events[0].sequence = 0, p => p.events[0].elapsedMs = -1,
    p => p.events[0].gambleFailures = 1.5, p => p.events[0].mode = "Other",
    p => p.events[0].evidence = "executed", p => p.events = [],
    p => p.events = Array.from({ length: 65 }, (_, i) => ({ ...p.events[0], sequence: i + 1 })),
    p => p.events.push({ ...p.events[0] }), p => p.events[0].inventory = Object.fromEntries(Array.from({ length: 513 }, (_, i) => ["u" + i, 1]))];
  for (const mutate of mutations) {
    const p = packet(); mutate(p);
    const r = await worker.fetch(new Request(origin + "/v3/gameplay", { method: "POST", body: JSON.stringify(p) }), {});
    assert.equal(r.status, 400, JSON.stringify(p).slice(0, 500));
    assert.equal(r.headers.get("X-Orand-Telemetry-Accepted"), "false");
  }
  for (const body of ["{", JSON.stringify(packet()).replace('"schemaVersion":3', '"schemaVersion":2,"schemaVersion":3'),
    JSON.stringify(packet()).replace('"elapsedMs":100', '"elapsedMs":1e999')]) {
    assert.equal((await worker.fetch(new Request(origin + "/v3/gameplay", { method: "POST", body }), {})).status, 400);
  }
  assert.equal((await worker.fetch(new Request(origin + "/v3/gameplay", { method: "POST", body: " ".repeat(512 * 1024 + 1) }), {})).status, 413);
});

test("SQLite atomic immutable packet/event identities, retries, conflict rollback and cohort fences", async (t) => {
  const { post, sqlite } = await setup(t);
  const p = packet();
  const accepted = await post(p);
  assert.equal(accepted.status, 200);
  assert.deepEqual(await accepted.json(), { schemaVersion: 3, packetId: p.packetId, accepted: true });
  assert.equal((await post(JSON.stringify(p, null, 2))).status, 200);
  assert.equal((await post({ ...p, appVersion: "2" })).status, 409);
  assert.equal((await post({ ...p, packetId: "3".repeat(32), chunkIndex: 1 })).status, 409);
  assert.equal((await post({ ...p, packetId: "3".repeat(32), chunkIndex: 1, events: [{ ...p.events[0], sequence: 2 }, { ...p.events[0], inventory: {} }] })).status, 400);
  const conflict = { ...p, packetId: "3".repeat(32), chunkIndex: 1, events: [{ ...p.events[0], inventory: {} }, { ...p.events[0], sequence: 2 }] };
  assert.equal((await post(conflict)).status, 409);
  assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_packets").get().n, 1);
  assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_events").get().n, 1);
  assert.equal((await post({ ...p, packetId: "4".repeat(32), chunkIndex: 1, difficulty: "normal", events: [{ ...p.events[0], sequence: 2 }] })).status, 409);
  assert.equal((await post({ ...p, packetId: "4".repeat(32), chunkIndex: 1, events: [{ ...p.events[0], sequence: 2 }] })).status, 200);
});

test("authenticated raw cursor feed is ordered, bounded, stable across expiry and excludes expired payloads", async (t) => {
  const { post, call, env, sqlite } = await setup(t);
  assert.equal((await call("/v3/gameplay")).status, 403);
  assert.equal((await call("/v3/gameplay", "GET", undefined, { Authorization: "Bearer dashboard-only" })).status, 403);
  for (const query of ["cursor=-1", "cursor=abc", "limit=0", "limit=1001", "limit=1.5", "limit=1&limit=2"])
    assert.equal((await call("/v3/gameplay?" + query, "GET", undefined, auth)).status, 400);
  const p = packet(); await post(p);
  await post(packet({ packetId: "3".repeat(32), matchId: "4".repeat(32) }));
  const first = await (await call("/v3/gameplay?limit=1", "GET", undefined, auth)).json();
  assert.deepEqual(first.packets, [p]); assert.equal(typeof first.nextCursor, "string");
  const second = await (await call("/v3/gameplay?limit=1&cursor=" + first.nextCursor, "GET", undefined, auth)).json();
  assert.equal(second.packets.length, 1); assert.equal(second.nextCursor, null);
  env.CLOCK = () => new Date("2026-10-10T00:00:00Z");
  await worker.scheduled({}, env, {});
  assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_raw").get().n, 0);
  assert.equal((await post(p)).status, 200);
  assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_raw").get().n, 0);
  assert.deepEqual((await (await call("/v3/gameplay", "GET", undefined, auth)).json()).packets, []);
});

test("public LiveStats reads only exact cohorts and authenticated PUT validates whole document", async (t) => {
  const { call } = await setup(t);
  const path = "/v3/live-stats?mapScriptSha256=" + hash + "&difficulty=" + encodeURIComponent("신");
  const empty = await (await call(path)).json();
  assert.equal(empty.schemaVersion, 1); assert.equal(empty.totalRecords, 0); assert.deepEqual(empty.weights, {});
  const body = { schemaVersion: 3, mapScriptSha256: hash, difficulty: "신", stats: stats() };
  assert.equal((await call("/v3/live-stats", "PUT", body)).status, 403);
  assert.equal((await call("/v3/live-stats", "PUT", body, auth)).status, 200);
  assert.deepEqual(await (await call(path)).json(), body.stats);
  assert.equal((await (await call(path.replace(hash, "b".repeat(64)))).json()).totalRecords, 0);
  assert.equal((await (await call(path.replace(encodeURIComponent("신"), "unknown"))).json()).totalRecords, 0);
  for (const mutate of [s => s.weights.luffy_common = 0.10001, s => s.goalWeights.luffy_common.luffy_common = -0.10001,
    s => s.goals.luffy_common.clears = 16, s => s.labeledRecords = 21,
    s => s.notes = "private", s => s.goals.luffy_common.chat = "private", s => s.weights["local/path"] = 0,
    s => s.totalRecords = -1, s => s.generatedAt = "not a date"]) {
    const bad = structuredClone(body); mutate(bad.stats);
    assert.equal((await call("/v3/live-stats", "PUT", bad, auth)).status, 400);
    assert.deepEqual(await (await call(path)).json(), body.stats);
  }
  assert.equal((await call("/v3/live-stats")).status, 400);
});

test("all six event variants preserve evidence and optional aggregate fields round-trip", async (t) => {
  const { post, call } = await setup(t);
  const { inventory, rewardWisps, resources, gambleFailures, ...common } = packet().events[0];
  const variants = [packet().events[0],
    { ...common, sequence: 2, kind: "recommendation", evidence: "recommendation", action: "Craft", targetUnitId: "luffy_common", goalUnitIds: [], selection: {} },
    { ...common, sequence: 3, kind: "craft", evidence: "inventory-matched", unitId: "rawcode:A00h", count: 1, consumed: { luffy_common: 1 } },
    { ...common, sequence: 4, kind: "selection", wispId: "e0IX", count: 1, outputs: {} },
    { ...common, sequence: 5, kind: "gamble", evidence: "unknown", gambleType: "high", count: 0, cost: {}, outputs: {} },
    { ...common, sequence: 6, kind: "outcome", outcome: "interrupted", outcomeSource: "appExit", inventory: { luffy_common: 1 }, goalUnitIds: ["rawcode:A00h"] }];
  assert.equal((await post(packet({ events: variants }))).status, 200);
  assert.deepEqual((await (await call("/v3/gameplay", "GET", undefined, auth)).json()).packets[0].events, variants);
  const s = stats();
  s.generatedAt = "2026-09-09T00:00:00Z"; s.difficulties = { "신": 20 };
  s.goals.luffy_common.failHeavyUnits = ["rawcode:A00h"];
  s.goals.luffy_common.adherenceMean = 0.5;
  const envelope = { schemaVersion: 3, mapScriptSha256: hash, difficulty: "신", stats: s };
  assert.equal((await call("/v3/live-stats", "PUT", envelope, auth)).status, 200);
  assert.deepEqual(await (await call("/v3/live-stats?mapScriptSha256=" + hash + "&difficulty=" + encodeURIComponent("신"))).json(), s);
  s.difficulties = { normal: 20 };
  assert.equal((await call("/v3/live-stats", "PUT", envelope, auth)).status, 400);
});

test("late storage failure rolls back packet, event and raw writes without acknowledgement", async (t) => {
  const { sqlite, post, env } = await setup(t);
  sqlite.exec("CREATE TRIGGER fixture_fault BEFORE INSERT ON gameplay_v3_raw BEGIN SELECT RAISE(ABORT, 'fixture_storage_fault'); END;");
  const failed = await post(packet());
  assert.equal(failed.status, 503);
  assert.equal(failed.headers.get("X-Orand-Telemetry-Accepted"), "false");
  for (const table of ["matches", "packets", "events", "raw"])
    assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_" + table).get().n, 0);
  sqlite.exec("DROP TRIGGER fixture_fault");
  assert.equal((await post(packet())).status, 200);
  env.GAMEPLAY_V3_ENABLED = "false";
  assert.equal((await post(packet())).status, 503);
  env.CLOCK = () => new Date("2026-10-09T00:00:00Z");
  await worker.scheduled({}, env, {});
  assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_raw").get().n, 0);
});

test("byte-bounded cursor pages do not drop packets and retained cursors work after deletion", async (t) => {
  const { sqlite, call, env } = await setup(t);
  // Populate synthetic maximum-size rows directly to exercise SQL result bounds,
  // independently of upload validation (which is covered through POST).
  const payload = JSON.stringify({ fixture: "x".repeat(512 * 1024 - 40) });
  for (let i = 1; i <= 12; i++) {
    const id = i.toString(16).padStart(32, "0");
    sqlite.prepare("INSERT INTO gameplay_v3_matches VALUES(?, ?)").run(id, hash);
    sqlite.prepare("INSERT INTO gameplay_v3_packets(packet_id, match_id, chunk_index, content_hash, received_at) VALUES(?, ?, 0, ?, ?)").run(id, id, hash, env.CLOCK().getTime());
    sqlite.prepare("INSERT INTO gameplay_v3_raw(packet_id, received_at, payload) VALUES(?, ?, ?)").run(id, env.CLOCK().getTime(), payload);
  }
  const response = await call("/v3/gameplay?limit=1000", "GET", undefined, auth);
  const body = await response.text();
  assert.ok(Buffer.byteLength(body) < 4 * 1024 * 1024 + 1024);
  const first = JSON.parse(body);
  assert.ok(first.packets.length < 12); assert.ok(first.nextCursor);
  const second = await (await call("/v3/gameplay?cursor=" + first.nextCursor, "GET", undefined, auth)).json();
  assert.equal(first.packets.length + second.packets.length, 12);
  assert.equal(second.nextCursor, null);
  env.CLOCK = () => new Date("2026-10-10T00:00:00Z");
  const expired = await (await call("/v3/gameplay?cursor=" + first.nextCursor, "GET", undefined, auth)).json();
  assert.deepEqual(expired, { packets: [], nextCursor: null });
  assert.equal(sqlite.prepare("SELECT count(*) AS n FROM gameplay_v3_raw").get().n, 0);
});

test("optional native gambleCounters round-trip only on observations with exact coherent bounded fields", async (t) => {
  const { post, call } = await setup(t);
  const p = packet({ profileVersion: "profile_" + "a".repeat(72) });
  p.events[0].gambleCounters = Object.fromEntries(["low", "middle", "high", "world", "absalom", "lumberWisp"].map(k => [k, { attempts: 3, successes: 1, failures: 2 }]));
  assert.equal((await post(p)).status, 200);
  const pulled = await (await call("/v3/gameplay", "GET", undefined, auth)).json();
  assert.deepEqual(pulled.packets[0], p);
  for (const mutate of [c => c.low.attempts = 4, c => c.low.successes = -1, c => c.low.failures = 0.5,
    c => c.low.attempts = Number.MAX_SAFE_INTEGER + 1, c => c.low.cost = 200,
    c => delete c.low.successes, c => c.medium = { attempts: 0, successes: 0, failures: 0 }]) {
    const bad = structuredClone(p); mutate(bad.events[0].gambleCounters);
    assert.equal((await post(bad)).status, 400);
  }
  const badKind = structuredClone(p);
  badKind.events[0] = { ...badKind.events[0], kind: "gamble", gambleType: "low", count: 1, cost: {}, outputs: {} };
  delete badKind.events[0].inventory; delete badKind.events[0].rewardWisps;
  delete badKind.events[0].resources; delete badKind.events[0].gambleFailures;
  assert.equal((await post(badKind)).status, 400);
});

test("request byte boundary is inclusive and malformed streams never reach storage", async (t) => {
  const { post } = await setup(t);
  const json = JSON.stringify(packet());
  const padding = 512 * 1024 - Buffer.byteLength(json);
  assert.equal((await post(json + " ".repeat(padding))).status, 200);
  assert.equal((await post(json + " ".repeat(padding + 1))).status, 413);
  const body = new ReadableStream({ start(controller) { controller.error(new Error("fixture stream failure")); } });
  const failed = await worker.fetch(new Request(origin + "/v3/gameplay", { method: "POST", body, duplex: "half" }), {});
  assert.equal(failed.status, 503);
});
