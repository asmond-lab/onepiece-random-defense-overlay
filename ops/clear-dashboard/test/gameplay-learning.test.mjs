import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { buildStats, reconstructSession } from "../worker/src/gameplay-learning.mjs";

test("every event kind participates in whole-match Bullet profile eligibility", () => {
  const kinds = [
    { kind: "observation", evidence: "native-observed", inventory: { common: 1 }, rewardWisps: {}, resources: {} },
    { kind: "recommendation", evidence: "recommendation", action: "Gather", targetUnitId: "rawcode:530h", goalUnitIds: ["rawcode:180h"] },
    { kind: "craft", evidence: "inventory-matched", unitId: "rare", count: 1, consumed: { common: 1 } },
    { kind: "selection", evidence: "inventory-matched", wispId: "e018", count: 1, outputs: { common: 1 } },
    { kind: "gamble", evidence: "inventory-matched", gambleType: "low", count: 1, cost: { gold: 100 }, outputs: {} },
    { kind: "outcome", evidence: "native-observed", outcome: "clear", outcomeSource: "mapSettlement", inventory: { rare: 1 }, goalUnitIds: ["rawcode:180h"] },
  ];
  const packet = { schemaVersion: 3, consentVersion: 3, packetId: "1".repeat(32), matchId: "2".repeat(32),
    chunkIndex: 0, appVersion: "1.0", mapVersion: "2.314", profileVersion: "1.0",
    mapScriptSha256: "a".repeat(64), difficulty: "신", events: kinds.map((e, i) => ({
      sequence: i + 1, recognitionRevision: i + 1, elapsedMs: i * 100, round: i + 1,
      completedStory: 0, mode: "Guide", guideNumber: 1, ...e })) };
  assert.equal(reconstructSession([packet]).bulletGuide, true);
  for (let i = 0; i < kinds.length; i++) {
    for (const change of [{ mode: "Normal" }, { guideNumber: 2 }]) {
      const mixed = structuredClone(packet);
      Object.assign(mixed.events[i], change);
      assert.equal(reconstructSession([mixed]).bulletGuide, false);
    }
  }
  const head = { ...packet, events: packet.events.slice(0, 3) };
  const tail = { ...packet, packetId: "3".repeat(32), chunkIndex: 1, events: packet.events.slice(3) };
  assert.equal(reconstructSession([tail]), null);
  assert.equal(reconstructSession([tail, head]).bulletGuide, true);
  head.events[0] = { ...head.events[0], mode: "Normal" };
  assert.equal(reconstructSession([tail, head]).bulletGuide, false);
});

test("Worker reconstruction and Wilson weights match the Python offline reference", async () => {
  const directory = fileURLToPath(new URL("../../gameplay-collector/", import.meta.url));
  const code = `import json
from test_learning import population
from telemetry_learning import aggregate_packets
p=population()
for x in p:
 x['events'][0]['rewardWisps']={'e0IX':1,'e01A':1}
 if x['events'][-1]['outcome']=='fail': x['events'][-1]['evidence']='observation-only'
print(json.dumps({'packets':p,'stats':next(iter(aggregate_packets(p).values()))}))`;
  const { stdout } = await promisify(execFile)("python", ["-c", code], { cwd: directory });
  const { packets, stats: expected } = JSON.parse(stdout);
  const sessions = packets.map(p => reconstructSession([p]));
  assert.ok(sessions.every(Boolean));
  const goals = [...new Set(sessions.map(s => s.goal))].map(goal => {
    const group = sessions.filter(s => s.goal === goal);
    const adherence = group.map(s => s.adherence).filter(n => n !== null);
    return { goal, plays: group.length, clears: group.filter(s => s.clear).length,
      adherence: adherence.length ? adherence.reduce((a, b) => a + b) / adherence.length : null };
  });
  const units = goals.flatMap(g => [...new Set(sessions.filter(s => s.goal === g.goal).flatMap(s => s.owned))].map(unit => ({
    goal: g.goal, unit, clears: sessions.filter(s => s.goal === g.goal && s.clear && s.owned.includes(unit)).length,
    fails: sessions.filter(s => s.goal === g.goal && !s.clear && s.owned.includes(unit)).length,
  })));
  assert.deepEqual(buildStats(goals, units, "신", expected.generatedAt), expected);
  const original = packets[0];
  const tail = { ...original, packetId: "f".repeat(32), chunkIndex: 1, events: original.events.slice(1) };
  const head = { ...original, events: original.events.slice(0, 1) };
  assert.deepEqual(reconstructSession([tail, head]), sessions[0]);
  assert.equal(reconstructSession([tail]), null);
  assert.equal(reconstructSession([{ ...original, events: original.events.map(e =>
    e.kind === "outcome" ? { ...e, outcomeSource: "clearRound" } : e) }]), null);
  assert.equal(reconstructSession([{ ...original, events: original.events.map(e =>
    e.kind === "outcome" ? { ...e, goalUnitIds: ["goal", "other"] } : e) }]), null);
});
