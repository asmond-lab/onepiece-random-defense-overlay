import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import worker from "../worker/src/index.mjs";

const endpoint = "https://telemetry.example/v1/dashboard-snapshot";
const uploadEndpoint = "https://telemetry.example/v2/aggregates";
const goodRecord = JSON.parse(
  await readFile(new URL("./fixtures/good-record.json", import.meta.url), "utf8"),
);

function databaseWith(rowsByMarker) {
  return {
    prepare(sql) {
      return {
        bind() {
          return this;
        },
        async all() {
          const marker = Object.keys(rowsByMarker).find((key) => sql.includes(key));
          return { results: marker === undefined ? [] : rowsByMarker[marker] };
        },
        async first() {
          const marker = Object.keys(rowsByMarker).find((key) => sql.includes(key));
          return marker === undefined ? null : rowsByMarker[marker][0] ?? null;
        },
      };
    },
  };
}

test("Given no dashboard key When requested Then the read endpoint is forbidden", async () => {
  const response = await worker.fetch(new Request(endpoint), {
    DASHBOARD_READ_KEY: "expected",
  });

  assert.equal(response.status, 403);
});

test("Given no clears When requested Then an empty snapshot is returned", async () => {
  const response = await worker.fetch(
    new Request(endpoint, { headers: { Authorization: "Bearer expected" } }),
    {
      DASHBOARD_READ_KEY: "expected",
      DB: databaseWith({}),
      CLOCK: () => new Date("2026-09-03T14:00:00.000Z"),
    },
  );
  const snapshot = await response.json();

  assert.equal(response.status, 200);
  assert.equal(snapshot.status, "empty");
  assert.equal(snapshot.windows["7d"].clearCount, 0);
  assert.equal(snapshot.recentClearAt, null);
});

test("Given recent rollups When requested Then windows and distributions are aggregated", async () => {
  const db = databaseWith({
    "dashboard:windows": [{ one_hour: 3, one_day: 18, seven_days: 72 }],
    "dashboard:difficulty": [{ key: "nightmare", clear_count: 40 }],
    "dashboard:goal": [{ key: "불멸", clear_count: 44 }],
    "dashboard:recent": [{ recent_clear_at: "2026-09-03T13:58:00.000Z" }],
  });
  const response = await worker.fetch(
    new Request(endpoint, { headers: { Authorization: "Bearer expected" } }),
    {
      DASHBOARD_READ_KEY: "expected",
      DB: db,
      CLOCK: () => new Date("2026-09-03T14:00:00.000Z"),
    },
  );
  const snapshot = await response.json();

  assert.equal(snapshot.status, "ok");
  assert.equal(snapshot.windows["1h"].clearCount, 3);
  assert.equal(snapshot.windows["24h"].clearCount, 18);
  assert.equal(snapshot.windows["7d"].clearCount, 72);
  assert.deepEqual(snapshot.distributions.byDifficulty, [
    { key: "nightmare", clearCount: 40 },
  ]);
  assert.equal(snapshot.dataDelaySeconds, 120);
});

test("Given an old latest clear When requested Then the snapshot is stale", async () => {
  const db = databaseWith({
    "dashboard:windows": [{ one_hour: 0, one_day: 1, seven_days: 4 }],
    "dashboard:recent": [{ recent_clear_at: "2026-09-03T11:00:00.000Z" }],
  });
  const response = await worker.fetch(
    new Request(endpoint, { headers: { Authorization: "Bearer expected" } }),
    {
      DASHBOARD_READ_KEY: "expected",
      DB: db,
      CLOCK: () => new Date("2026-09-03T14:00:00.000Z"),
    },
  );

  assert.equal((await response.json()).status, "stale");
});

test("Given a clear upload When accepted Then aggregate and hourly rollups are atomic", async () => {
  let batch = [];
  const db = {
    prepare(sql) {
      return {
        sql,
        values: [],
        bind(...values) {
          this.values = values;
          return this;
        },
        async run() {},
      };
    },
    async batch(statements) {
      batch = statements;
    },
  };
  const response = await worker.fetch(new Request(uploadEndpoint, {
    method: "POST",
    body: JSON.stringify(goodRecord),
  }), {
    DB: db,
    CLOCK: () => new Date("2026-09-03T13:58:00.000Z"),
  });

  assert.equal(response.status, 204);
  assert.equal(batch.length, 3);
  assert.deepEqual(batch[1].values, ["2026-09-03 13:00:00", "nightmare", "불멸"]);
});

test("Given only the dashboard key When raw aggregates are pulled Then access is denied", async () => {
  const response = await worker.fetch(new Request(uploadEndpoint, {
    headers: { Authorization: "Bearer dashboard-only" },
  }), {
    COLLECTOR_KEY: "collector-only",
    DASHBOARD_READ_KEY: "dashboard-only",
  });

  assert.equal(response.status, 403);
});
