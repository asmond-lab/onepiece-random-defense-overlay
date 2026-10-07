import assert from "node:assert/strict";
import test from "node:test";
import worker from "../worker/src/index.mjs";
import { endpoint, packet, setup } from "./observations-fixture.mjs";

const request = body => new Request(endpoint, { method: "POST", body });
test("schema4 rejects unknown personal data and invalid fields before storage", async () => {
  const mutations = [p => p.nickname = "private", p => p.path = "private", p => delete p.sessionId,
    p => p.schemaVersion = 3, p => p.consentVersion = 3, p => p.source = "test", p => p.mapVersion = "2.314",
    p => p.gameVersion = "2.0.4.23745", p => p.appVersion = "C:/private", p => p.appVersion = "a".repeat(81),
    p => p.appVersion += "\n", p => p.packetId += "\n", p => p.sessionId = "bad", p => p.datasetFingerprint += "\n",
    p => p.events = [], p => p.events = null, p => p.events = [null], p => p.events[0].round = null,
    p => p.events[0].round = 0, p => p.events[0].round = 66, p => p.events[0].round = 1.5,
    p => p.events[0].durationMs = 86400001, p => p.events[0].ageMs = -1, p => p.events[0].gapMs = -1,
    p => p.events[0].sourceRevision = Number.MAX_SAFE_INTEGER + 1, p => p.events[0].sequence = 0,
    p => p.events[0].elapsedMs = -1, p => p.events[0].elapsedMs = Number.MAX_SAFE_INTEGER + 1,
    p => p.events[0].state = "expired", p => p.events[0].reasonCode = "private reason", p => p.events[0].lane = "private",
    p => p.events[0].stage = "Normal", p => p.events[0].targetUnitId = "path/private", p => p.events[0].chat = "private",
    p => p.events[0].kind = "outcome", p => delete p.events[0].inventory, p => p.events[0].inventory = [],
    p => p.events[0].inventory = { "unit\n": 1 }, p => p.events[0].inventory = { "path/private": 1 },
    p => p.events[0].inventory = { unit: 0 }, p => p.events[0].inventory = { unit: 10001 },
    p => p.events[0].inventory = { unit: 1.5 }, p => p.events[0].inventory = { unit: "1" },
    p => p.events[0].inventory = Object.fromEntries(Array.from({ length: 513 }, (_, i) => ["u" + i, 1])),
    p => p.events.push({ ...p.events[0] }),
    p => p.events = Array.from({ length: 65 }, (_, i) => ({ ...p.events[0], sequence: i + 1 })),
    p => p.events = [{ sequence: 1, elapsedMs: 0, kind: "recognition", state: "session-reset" }],
    p => p.events = [{ sequence: 1, elapsedMs: 0, kind: "recognition", state: "fresh", inventory: {} }],
    p => p.events = [{ sequence: 1, elapsedMs: 0, kind: "selection", state: "target-selected" }],
    p => p.events = [{ sequence: 1, elapsedMs: 0, kind: "selection", state: "fresh", targetUnitId: "unit" }],
    p => p.events = [{ sequence: 1, elapsedMs: 0, kind: "recommendation", state: "target-selected" }],
    p => p.events = [{ sequence: 1, elapsedMs: 0, kind: "session", state: "fresh" }]];
  for (const mutate of mutations) {
    const p = packet(); mutate(p);
    const result = await worker.fetch(request(JSON.stringify(p)), {});
    assert.equal(result.status, 400, mutate.toString());
    assert.equal(result.headers.get("X-Orand-Telemetry-Accepted"), "false");
  }
});

test("duplicate and escaped JSON keys, depth, invalid UTF8 and unsafe numbers fail before storage", async () => {
  const body = JSON.stringify(packet());
  const escapedSequence = '"' + String.fromCharCode(92) + 'u0073equence":2,"sequence":1';
  const cases = ["{", body.replace('"schemaVersion":4', '"schemaVersion":3,"schemaVersion":4'),
    body.replace('"sequence":1', escapedSequence), body.replace('"rawcode:hfoo":2', '"rawcode:hfoo":1,"rawcode:hfoo":2'),
    body.replace('"elapsedMs":100', '"elapsedMs":1e999'), '['.repeat(17) + '0' + ']'.repeat(17),
    new Uint8Array([123, 34, 255, 34, 58, 48, 125])];
  for (const value of cases) assert.equal((await worker.fetch(request(value), {})).status, 400);
});

test("unknown round is omitted and all diagnostic states, reasons and numeric boundaries are accepted", async t => {
  const { post } = await setup(t);
  const p = packet();
  p.events[0] = { sequence: 1, elapsedMs: 0, kind: "inventory", state: "fresh", inventory: {} };
  assert.equal((await post(p)).status, 200);
  const states = ["fresh", "expired", "read-failed", "rejected", "scanning", "paused", "auto-off", "game-unavailable"];
  const reasons = ["none", "freshness", "binding", "unavailable", "unsupported", "configuration", "cancelled", "read-error", "unknown"];
  p.packetId = "3".repeat(32);
  p.events = states.map((state, i) => ({ sequence: i + 1, elapsedMs: 100 * i, kind: "recognition", state, reasonCode: reasons[i], lane: "scan" }));
  p.events.push({ sequence: 9, elapsedMs: Number.MAX_SAFE_INTEGER, kind: "recognition", state: "fresh", reasonCode: "unknown",
    durationMs: 86400000, ageMs: 86400000, gapMs: Number.MAX_SAFE_INTEGER, sourceRevision: Number.MAX_SAFE_INTEGER });
  for (const state of ["session-start", "session-reset", "app-exit"])
    p.events.push({ sequence: p.events.length + 1, elapsedMs: 0, kind: "session", state });
  for (const stage of ["Rare", "Legend", "Upper", "Utility"])
    p.events.push({ sequence: p.events.length + 1, elapsedMs: 0, kind: "recommendation", state: "recommended", stage });
  assert.equal((await post(p)).status, 200);
  p.packetId = "4".repeat(32);
  p.events = Array.from({ length: 64 }, (_, i) => ({ sequence: i + 1, elapsedMs: i, kind: "recognition", state: "scanning" }));
  assert.equal((await post(p)).status, 200);
});

test("256KiB byte cap is inclusive and streaming overflow cancels before storage", async t => {
  const { post } = await setup(t);
  const body = JSON.stringify(packet());
  const padding = 256 * 1024 - Buffer.byteLength(body);
  assert.equal((await post(body + " ".repeat(padding))).status, 200);
  assert.equal((await post(body + " ".repeat(padding + 1))).status, 413);
  assert.equal((await worker.fetch(new Request(endpoint, { method: "POST", body, headers: { "Content-Length": String(256 * 1024 + 1) } }), {})).status, 413);
  let cancelled = false;
  const overflow = new ReadableStream({
    pull(controller) { controller.enqueue(new Uint8Array(65537)); },
    cancel() { cancelled = true; },
  });
  const response = await worker.fetch(new Request(endpoint, { method: "POST", body: overflow, duplex: "half" }), {});
  assert.equal(response.status, 413);
  assert.equal(cancelled, true);
  const failed = new ReadableStream({ start(controller) { controller.error(new Error("fixture stream failure")); } });
  assert.equal((await worker.fetch(new Request(endpoint, { method: "POST", body: failed, duplex: "half" }), {})).status, 503);
});
