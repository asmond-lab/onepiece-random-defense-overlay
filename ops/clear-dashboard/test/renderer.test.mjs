import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { renderDiscordPayload } from "../src/discord-renderer.mjs";
import { connectionErrorSnapshot } from "../src/snapshot.mjs";

const fixture = JSON.parse(
  await readFile(new URL("./fixtures/ok-snapshot.json", import.meta.url), "utf8"),
);

test("Given a healthy snapshot When rendered Then Components v2 structure is emitted", () => {
  const payload = renderDiscordPayload(fixture);

  assert.equal(payload.flags, 32768);
  assert.equal(payload.components.length, 1);
  assert.equal(payload.components[0].type, 17);
  assert.deepEqual(
    payload.components[0].components.map((component) => component.type),
    [10, 14, 10, 14, 10, 14, 10],
  );
  assert.equal("content" in payload, false);
  assert.equal("embeds" in payload, false);
});

test("Given an empty snapshot When rendered Then the empty state remains explicit", () => {
  const payload = renderDiscordPayload({
    ...fixture,
    status: "empty",
    windows: {
      "1h": { clearCount: 0 },
      "24h": { clearCount: 0 },
      "7d": { clearCount: 0 },
    },
    distributions: { byDifficulty: [], byGoal: [] },
    recentClearAt: null,
    dataDelaySeconds: null,
  });

  assert.match(payload.components[0].components[0].content, /수집 대기/);
});

test("Given stale data When rendered Then the delayed state and delay are visible", () => {
  const payload = renderDiscordPayload({
    ...fixture,
    status: "stale",
    dataDelaySeconds: 7200,
  });

  assert.match(payload.components[0].components[0].content, /지연/);
  assert.match(payload.components[0].components.at(-1).content, /2시간/);
});

test("Given a connection error When rendered Then an operational error card is emitted", () => {
  const payload = renderDiscordPayload(
    connectionErrorSnapshot(new Date("2026-09-03T14:00:00.000Z"), "ignored"),
  );

  assert.match(payload.components[0].components[0].content, /연결 오류/);
  assert.match(payload.components[0].components.at(-1).content, /connection_error/);
});
