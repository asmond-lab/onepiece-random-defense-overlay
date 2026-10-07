import test from "node:test";
import assert from "node:assert/strict";
import worker from "../src/worker.mjs";

const env = (res) => ({ ASSETS: { fetch: async () => res() } });
const html = () => new Response("<h1>hi</h1>", { headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "public, max-age=600" } });

test("www redirects keeping path and query", async () => {
  const r = await worker.fetch(new Request("https://www.randypick.com/a/b?x=1"), env(html));
  assert.equal(r.status, 301);
  assert.equal(r.headers.get("Location"), "https://randypick.com/a/b?x=1");
  assert.ok(r.headers.get("Strict-Transport-Security"));
});

test("plain http redirects to https apex keeping path and query", async () => {
  for (const u of ["http://randypick.com/download?x=1", "http://www.randypick.com/download?x=1"]) {
    const r = await worker.fetch(new Request(u), env(html));
    assert.equal(r.status, 301);
    assert.equal(r.headers.get("Location"), "https://randypick.com/download?x=1");
  }
});

test("POST returns 405 with Allow", async () => {
  const r = await worker.fetch(new Request("https://randypick.com/", { method: "POST" }), env(html));
  assert.equal(r.status, 405);
  assert.equal(r.headers.get("Allow"), "GET, HEAD");
  assert.ok(r.headers.get("X-Frame-Options"));
});

test("/healthz returns JSON", async () => {
  const r = await worker.fetch(new Request("https://randypick.com/healthz"), env(html));
  assert.equal(r.status, 200);
  assert.equal(r.headers.get("Cache-Control"), "no-store");
  assert.deepEqual(await r.json(), { status: "ok", service: "randypick-web" });
  assert.ok(r.headers.get("Content-Security-Policy"));
});

test("HTML passthrough gets security headers and revalidate", async () => {
  const r = await worker.fetch(new Request("https://randypick.com/"), env(html));
  assert.equal(r.status, 200);
  assert.match(r.headers.get("Content-Security-Policy"), /default-src 'none'/);
  assert.match(r.headers.get("Strict-Transport-Security"), /max-age=31536000/);
  assert.equal(r.headers.get("Cache-Control"), "public, max-age=0, must-revalidate");
  assert.equal(await r.text(), "<h1>hi</h1>");
});

test("404 stays 404 with security headers", async () => {
  const r = await worker.fetch(new Request("https://randypick.com/nope"), env(() => new Response("nf", { status: 404, headers: { "Content-Type": "text/html" } })));
  assert.equal(r.status, 404);
  assert.equal(r.headers.get("X-Content-Type-Options"), "nosniff");
});

test("non-HTML keeps Cache-Control", async () => {
  const r = await worker.fetch(new Request("https://randypick.com/a.css"), env(() => new Response("b{}", { headers: { "Content-Type": "text/css", "Cache-Control": "public, max-age=31536000, immutable" } })));
  assert.equal(r.headers.get("Cache-Control"), "public, max-age=31536000, immutable");
  assert.ok(r.headers.get("Referrer-Policy"));
});
