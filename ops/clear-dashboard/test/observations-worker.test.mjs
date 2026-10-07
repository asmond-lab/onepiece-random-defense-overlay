import assert from "node:assert/strict";
import test from "node:test";
import worker from "../worker/src/index.mjs";

test("2.320 observed telemetry has a schema4 consent handshake", async () => {
  const response = await worker.fetch(new Request("https://fixture.invalid/v4/observations", { method: "OPTIONS" }), {});
  assert.equal(response.status, 204);
  for (const [name, value] of Object.entries({ Schema: "4", Accepted: "true", Enabled: "true" }))
    assert.equal(response.headers.get("X-Orand-Telemetry-" + name), value);
});

import { endpoint, packet, setup } from "./observations-fixture.mjs";

test("disabled collection and unsupported methods cannot read or acknowledge uploads", async t => {
  const { env, call, count } = await setup(t);
  for (const name of ["TELEMETRY_ENABLED", "OBSERVATIONS_V4_ENABLED"]) {
    env[name] = "false";
    const reply = await call("POST", "bad");
    assert.equal(reply.status, 503);
    assert.equal(reply.headers.get("X-Orand-Telemetry-Accepted"), "false");
    assert.equal(reply.headers.get("X-Orand-Telemetry-Enabled"), "false");
    delete env[name];
  }
  for (const method of ["GET", "PUT", "DELETE", "HEAD"])
    assert.equal((await call(method, undefined, { Authorization: "Bearer fixture" })).status, 405);
  assert.equal((await call("POST", packet(), {}, "?raw=true")).status, 400);
  assert.equal(count(), 0);
});

test("SQLite accepts all observed event types and isolates validation data from v3 learning", async t => {
  const { post, sqlite, env } = await setup(t);
  const p = packet();
  p.events.push(
    { sequence: 2, elapsedMs: 3100, kind: "recognition", state: "expired", reasonCode: "freshness", ageMs: 3100, lane: "presentation" },
    { sequence: 3, elapsedMs: 3200, kind: "recognition", state: "read-failed", reasonCode: "read-error", lane: "full" },
    { sequence: 4, elapsedMs: 3300, kind: "recognition", state: "fresh", gapMs: 200, lane: "scan" },
    { sequence: 5, elapsedMs: 3400, kind: "selection", state: "target-selected", targetUnitId: "rare", stage: "Rare" },
    { sequence: 6, elapsedMs: 3500, kind: "recommendation", state: "recommended", stage: "Legend", targetUnitId: "legend" },
    { sequence: 7, elapsedMs: 3600, kind: "session", state: "session-reset" });
  const reply = await post(p);
  assert.equal(reply.status, 200);
  assert.deepEqual(await reply.json(), { schemaVersion: 4, packetId: p.packetId, accepted: true });
  assert.equal(reply.headers.get("X-Orand-Telemetry-Schema"), "4");
  assert.deepEqual(JSON.parse(sqlite.prepare("SELECT payload FROM telemetry_v4_packets").get().payload), p);
  assert.equal((await post({ ...p, packetId: "3".repeat(32), source: "synthetic-validation" })).status, 200);
  assert.deepEqual(sqlite.prepare("SELECT source, count(*) n FROM telemetry_v4_packets GROUP BY source ORDER BY source").all().map(r => ({ ...r })),
    [{ source: "live", n: 1 }, { source: "synthetic-validation", n: 1 }]);
  await worker.scheduled({}, env);
  assert.equal(sqlite.prepare("SELECT count(*) n FROM gameplay_v3_raw").get().n, 0);
  assert.equal(sqlite.prepare("SELECT count(*) n FROM gameplay_v3_learning_sessions").get().n, 0);
});

test("packet retry is immutable and concurrent conflicting submissions produce one winner", async t => {
  const { post, sqlite, env, count } = await setup(t);
  const p = packet();
  const results = await Promise.all(Array.from({ length: 16 }, (_, i) => post(i % 2 ? p : { ...p, appVersion: "conflict" })));
  assert.equal(results.filter(r => r.status === 200).length, 8);
  assert.equal(results.filter(r => r.status === 409).length, 8);
  assert.equal(count(), 1);
  const row = sqlite.prepare("SELECT * FROM telemetry_v4_packets").get();
  const winner = JSON.parse(row.payload);
  env.CLOCK = () => new Date("2026-09-16T12:00:00Z");
  assert.equal((await post(JSON.stringify(winner, null, 2))).status, 200);
  const reordered = Object.fromEntries(Object.entries(winner).reverse());
  assert.equal((await post(reordered)).status, 200);
  assert.equal(sqlite.prepare("SELECT received_at FROM telemetry_v4_packets").get().received_at, row.received_at);
  for (const change of [{ source: "synthetic-validation" }, { sessionId: "4".repeat(32) }, { datasetFingerprint: "b".repeat(64) }])
    assert.equal((await post({ ...winner, ...change })).status, 409);
  assert.deepEqual(sqlite.prepare("SELECT * FROM telemetry_v4_packets").get(), row);
});

test("late SQLite storage failure rolls back insertion and returns no acceptance", async t => {
  const { post, sqlite, env, count } = await setup(t);
  assert.equal((await post(packet())).status, 200);
  env.CLOCK = () => new Date("2026-10-16T12:00:00Z");
  sqlite.exec("CREATE TRIGGER fixture_fault BEFORE DELETE ON telemetry_v4_packets BEGIN SELECT RAISE(ABORT, 'fixture_storage_fault'); END;");
  const failed = await post(packet({ packetId: "3".repeat(32) }));
  assert.equal(failed.status, 503);
  assert.equal(failed.headers.get("X-Orand-Telemetry-Accepted"), "false");
  assert.equal(count(), 1);
  sqlite.exec("DROP TRIGGER fixture_fault");
  assert.equal((await post(packet({ packetId: "3".repeat(32) }))).status, 200);
  assert.equal(count(), 1);
});

test("on-upload purge is bounded and scheduled purge expires both sources at thirty days", async t => {
  const { post, sqlite, env, count } = await setup(t);
  const start = env.CLOCK().getTime();
  for (let i = 1; i <= 102; i++)
    sqlite.prepare("INSERT INTO telemetry_v4_packets VALUES(?, ?, ?, ?, ?, ?, ?)")
      .run(i.toString(16).padStart(32, "0"), "2".repeat(32), "fixture", i % 2 ? "live" : "synthetic-validation", start, "b".repeat(64), "{}");
  env.CLOCK = () => new Date(start + 30 * 24 * 60 * 60 * 1000 - 1);
  await worker.scheduled({}, env);
  assert.equal(count(), 102);
  env.CLOCK = () => new Date(start + 30 * 24 * 60 * 60 * 1000);
  assert.equal((await post(packet())).status, 200);
  assert.equal(count(), 3);
  env.OBSERVATIONS_V4_ENABLED = "false";
  await worker.scheduled({}, env);
  assert.equal(count(), 1, "disabled service still expires retained observations");
  assert.equal(sqlite.prepare("SELECT packet_id FROM telemetry_v4_packets").get().packet_id, packet().packetId);
});
