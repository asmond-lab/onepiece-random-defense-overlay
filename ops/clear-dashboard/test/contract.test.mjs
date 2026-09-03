import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { connectionErrorSnapshot, parseSnapshot } from "../src/snapshot.mjs";

const fixture = JSON.parse(
  await readFile(new URL("./fixtures/ok-snapshot.json", import.meta.url), "utf8"),
);

test("Given valid endpoint data When parsed Then the normalized contract is retained", () => {
  const snapshot = parseSnapshot(fixture);

  assert.deepEqual(snapshot, fixture);
});

test("Given an invalid endpoint shape When parsed Then a contract error is raised", () => {
  assert.throws(
    () => parseSnapshot({ ...fixture, windows: { "1h": { clearCount: -1 } } }),
    /snapshot contract/i,
  );
});

test("Given a connection failure When normalized Then no secret-bearing detail is exposed", () => {
  const snapshot = connectionErrorSnapshot(
    new Date("2026-09-03T14:00:00.000Z"),
    "Bearer private-value",
  );

  assert.equal(snapshot.status, "error");
  assert.equal(snapshot.error.code, "connection_error");
  assert.equal(JSON.stringify(snapshot).includes("private-value"), false);
});
