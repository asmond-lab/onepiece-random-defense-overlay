import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { pollDashboard } from "../src/poller.mjs";

const fixture = JSON.parse(
  await readFile(new URL("./fixtures/ok-snapshot.json", import.meta.url), "utf8"),
);

test("Given an allowed endpoint When polled Then the authenticated snapshot is parsed", async () => {
  let authorization = "";
  const snapshot = await pollDashboard({
    endpoint: "https://orand-telemetry.epic42121.workers.dev/v1/dashboard-snapshot",
    readKey: "secret-ref-value",
    allowedHosts: ["orand-telemetry.epic42121.workers.dev"],
    fetchImpl: async (_url, init) => {
      authorization = init.headers.Authorization;
      return Response.json(fixture);
    },
    now: () => new Date("2026-09-03T14:00:00.000Z"),
  });

  assert.equal(authorization, "Bearer secret-ref-value");
  assert.equal(snapshot.status, "ok");
});

test("Given a host outside the allowlist When polled Then no request is attempted", async () => {
  let requested = false;
  const snapshot = await pollDashboard({
    endpoint: "https://example.invalid/v1/dashboard-snapshot",
    readKey: "secret-ref-value",
    allowedHosts: ["orand-telemetry.epic42121.workers.dev"],
    fetchImpl: async () => {
      requested = true;
      return Response.json(fixture);
    },
    now: () => new Date("2026-09-03T14:00:00.000Z"),
  });

  assert.equal(requested, false);
  assert.equal(snapshot.error.code, "configuration_error");
});

test("Given an HTTP failure When polled Then a normalized connection snapshot is returned", async () => {
  const snapshot = await pollDashboard({
    endpoint: "https://orand-telemetry.epic42121.workers.dev/v1/dashboard-snapshot",
    readKey: "secret-ref-value",
    allowedHosts: ["orand-telemetry.epic42121.workers.dev"],
    fetchImpl: async () => new Response(null, { status: 503 }),
    now: () => new Date("2026-09-03T14:00:00.000Z"),
  });

  assert.equal(snapshot.status, "error");
  assert.equal(snapshot.error.code, "connection_error");
});
