import test from 'node:test';
import assert from 'node:assert/strict';
import worker, { resolveRoute, matchesEtag } from '../src/worker.mjs';

const ORIGIN = 'https://updates.example.invalid';
const bytes = new TextEncoder().encode('opaque test bytes, not a release manifest');
const routes = [
  ['/v1/channels/stable/win-x64', 'channels/stable/win-x64.json', true, null],
  ['/v1/channels/test/win-x64', 'channels/test/win-x64.json', true, null],
  ['/v1/profiles/win-x64', 'channels/profiles/win-x64.json', true, null],
  ['/downloads/1.2.3/OrandOverlay.exe', 'downloads/1.2.3/OrandOverlay.exe', false, 'OrandOverlay.exe'],
  ['/downloads/1.2.3/RandyPick.exe', 'downloads/1.2.3/RandyPick.exe', false, 'RandyPick.exe'],
  ['/downloads/1.2.3-test.20260914.1/RandyPick.exe', 'downloads/1.2.3-test.20260914.1/RandyPick.exe', false, 'RandyPick.exe'],
  ['/downloads/1.2.3-test.20260914.1/OrandOverlay.exe', 'downloads/1.2.3-test.20260914.1/OrandOverlay.exe', false, 'OrandOverlay.exe'],
  ['/profiles/1.2.3/memory-profiles.json', 'profiles/1.2.3/memory-profiles.json', false, 'memory-profiles.json'],
];
function metadata() {
  return { etag: 'object-etag', size: bytes.length, httpMetadata: {
    contentType: 'text/html', cacheControl: 'private', contentDisposition: 'bad',
  }, customMetadata: { Location: 'https://untrusted.example.invalid', 'Access-Control-Allow-Origin': '*' },
  writeHttpMetadata() { throw new Error('Do not trust publisher HTTP metadata'); } };
}
function fixture(options = {}) {
  const calls = [];
  let canceled = false;
  const bucket = {
    async head(key) { calls.push(['head', key]); if (options.fail) throw new Error('secret account details'); return options.missing ? null : metadata(); },
    async get(key) { calls.push(['get', key]); if (options.fail) throw new Error('secret account details'); return options.missing ? null : { ...metadata(), body: new ReadableStream({
      start(controller) { controller.enqueue(bytes); },
      pull(controller) { controller.close(); },
      cancel() { canceled = true; },
    }), text() { throw new Error('Must not buffer'); }, arrayBuffer() { throw new Error('Must not buffer'); }, json() { throw new Error('Must not parse opaque data'); } }; },
  };
  for (const name of ['put', 'delete', 'list', 'createMultipartUpload']) bucket[name] = () => { throw new Error('Forbidden mutation/list'); };
  return { env: { RELEASES: bucket }, calls, get canceled() { return canceled; } };
}
function request(path, method = 'GET', headers = {}) {
  return new Request(ORIGIN + path, { method, headers });
}
function secure(response) {
  assert.equal(response.headers.get('X-Content-Type-Options'), 'nosniff');
  assert.equal(response.headers.get('Referrer-Policy'), 'no-referrer');
  assert.match(response.headers.get('Content-Security-Policy'), /default-src 'none'/);
  for (const header of ['Access-Control-Allow-Origin', 'Access-Control-Allow-Credentials', 'Location', 'Set-Cookie']) assert.equal(response.headers.get(header), null);
}

for (const [path, key, manifest, filename] of routes) {
  for (const method of ['GET', 'HEAD']) {
    test(method + ' ' + path + ' maps only to the exact R2 key', async () => {
      const f = fixture();
      const response = await worker.fetch(request(path, method), f.env);
      assert.equal(response.status, 200);
      assert.deepEqual(f.calls, [[method === 'GET' ? 'get' : 'head', key]]);
      assert.equal(response.headers.get('Cache-Control'), manifest ? 'public, max-age=60' : 'public, max-age=31536000, immutable');
      assert.equal(response.headers.get('Content-Type'), ['OrandOverlay.exe', 'RandyPick.exe'].includes(filename) ? 'application/octet-stream' : 'application/json; charset=utf-8');
      assert.equal(response.headers.get('Content-Disposition'), filename ? 'attachment; filename="' + filename + '"' : 'inline');
      assert.equal(response.headers.get('ETag'), '"object-etag"');
      assert.equal(response.headers.get('Content-Length'), String(bytes.length));
      secure(response);
      if (method === 'HEAD') { assert.equal(response.body, null); assert.equal(await response.text(), ''); }
      else assert.deepEqual(new Uint8Array(await response.arrayBuffer()), bytes);
    });
  }
}

const invalidPaths = [
  '/profiles/1.2.3/RandyPick.exe', '/downloads/1.2.3/randypick.exe',
  '/downloads/1.2.3/Randypick.exe', '/downloads/1.2.3/RandyPick.EXE',
  '/downloads/1.2.3/RandyPick.dll', '/downloads/1.2.3/RandyPick.exe.bak',
  '/downloads/1.2.3/RandyPick.exe/OrandOverlay.exe', '/downloads/1.2.3/RandyPick.exe/extra',
  '/downloads/1.2.3/../1.2.3/RandyPick.exe', '/downloads/%2e%2e/RandyPick.exe',
  '/downloads/1.2.3%2f..%2f/RandyPick.exe', '/downloads/1.2.3/RandyPick.exe%00',
  '/downloads/1.2.3/RandyPick.exe?redirect=https://evil.invalid',
  '/', '/health', '/admin', '/downloads', '/downloads/', '/channels/stable/win-x64.json',
  '/v1/channels/beta/win-x64', '/v1/channels/stable/win-arm64', '/v1/channels/STABLE/win-x64',
  '/v1/channels/stable/win-x64/', '/v1/profiles/win-x64.json',
  '/downloads/1.2.3/memory-profiles.json', '/profiles/1.2.3/OrandOverlay.exe',
  '/downloads/1.2.3/Other.exe', '/downloads/1.2.3/orandoverlay.exe',
  '/downloads/../OrandOverlay.exe', '/downloads/./OrandOverlay.exe',
  '/downloads/1.2.3/../1.2.3/OrandOverlay.exe',
  '/downloads/%2e%2e/OrandOverlay.exe', '/downloads/%252e%252e/OrandOverlay.exe',
  '/downloads/1.2.3%2f..%2f/OrandOverlay.exe', '/downloads/1.2.3%5c/OrandOverlay.exe',
  '/%761/channels/stable/win-x64', '/v1//channels/stable/win-x64',
  '/downloads/1.2.3/OrandOverlay.exe%00', '/downloads/1.2.3/OrandOverlay.exe%0d%0aLocation:evil',
  '/downloads/1.2.3/OrandOverlay.exe/extra', '/downloads/1.2.3/OrandOverlay.exe;evil',
  '/downloads/1.2.3/OrandOverlay.exe?redirect=https://evil.invalid',
  '/v1/channels/stable/win-x64?url=https://evil.invalid', '/v1/profiles/win-x64?',
  '/v1/profiles/win-x64#fragment', '/downloads/1.2.3\\OrandOverlay.exe',
];
for (const path of invalidPaths) {
  test('reject raw route ' + path, async () => {
    const f = fixture();
    // Preserve raw malicious paths that WHATWG Request would normalize first.
    const response = await worker.fetch({ url: ORIGIN + path, method: 'GET', headers: new Headers() }, f.env);
    assert.equal(response.status, 404);
    assert.deepEqual(f.calls, []);
    assert.deepEqual(await response.json(), { error: 'not_found' });
    assert.equal(response.headers.get('Cache-Control'), 'no-store');
    secure(response);
  });
}
for (const version of ['1', '1.2', 'v1.2.3', '01.2.3', '1.02.3', '1.2.03', '1.2.3.4', '-1.2.3', 'latest', '1.2.3-', '1.2.3+', '1.2.3-alpha..1', '1.2.3-alpha_1', '1.2.3-01', '1.2.3-rc.01', '1.2.3+build..a', '1.2.3-' + 'a'.repeat(123)]) {
  for (const filename of ['OrandOverlay.exe', 'RandyPick.exe'])
    test('reject version ' + version + ' for ' + filename, () => assert.equal(resolveRoute(ORIGIN + '/downloads/' + version + '/' + filename), null));
}
for (const version of ['0.0.0', '1.2.3', '1.2.3-test.20260914.1', '1.2.3-rc.1', '1.2.3-alpha-2.0', '1.2.3+build.001', '1.2.3-rc.1+build.7']) {
  for (const filename of ['OrandOverlay.exe', 'RandyPick.exe'])
    test('accept version ' + version + ' for ' + filename, () => assert.equal(resolveRoute(ORIGIN + '/downloads/' + version + '/' + filename).key, 'downloads/' + version + '/' + filename));
}
for (const method of ['POST', 'PUT', 'DELETE', 'PATCH', 'OPTIONS', 'TRACE']) {
  test('reject method ' + method + ' before storage access', async () => {
    const f = fixture();
    const response = await worker.fetch({ url: ORIGIN + routes[0][0], method, headers: new Headers() }, f.env);
    assert.equal(response.status, 405);
    assert.equal(response.headers.get('Allow'), 'GET, HEAD');
    assert.deepEqual(await response.json(), { error: 'method_not_allowed' });
    assert.deepEqual(f.calls, []);
    secure(response);
  });
}
for (const method of ['GET', 'HEAD']) {
  for (const [options, status, error] of [[{ missing: true }, 404, 'not_found'], [{ fail: true }, 503, 'service_unavailable']]) {
    test(method + ' handles ' + status + ' without leaking storage details', async () => {
      const response = await worker.fetch(request(routes[0][0], method, { 'If-None-Match': '*' }), fixture(options).env);
      assert.equal(response.status, status);
      assert.equal(response.headers.get('Cache-Control'), 'no-store');
      assert.equal(response.headers.get('Content-Type'), 'application/json; charset=utf-8');
      if (method === 'HEAD') assert.equal(response.body, null);
      else assert.deepEqual(await response.json(), { error });
      secure(response);
    });
  }
  for (const condition of ['"object-etag"', 'W/"object-etag"', '"other", W/"object-etag"', '*']) {
    test(method + ' conditional 304 for ' + condition, async () => {
      const f = fixture();
      const response = await worker.fetch(request(routes[0][0], method, { 'If-None-Match': condition }), f.env);
      assert.equal(response.status, 304);
      assert.equal(response.body, null);
      assert.equal(response.headers.get('Content-Length'), null);
      assert.equal(response.headers.get('ETag'), '"object-etag"');
      assert.equal(response.headers.get('Cache-Control'), 'public, max-age=60');
      if (method === 'GET') assert.equal(f.canceled, true);
      secure(response);
    });
  }
  test(method + ' ETag mismatch serves the representation', async () => {
    const response = await worker.fetch(request(routes[0][0], method, { 'If-None-Match': '"other"' }), fixture().env);
    assert.equal(response.status, 200);
    if (response.body) await response.body.cancel();
  });
}
test('conditional parser rejects malformed lists and supports quoted commas', () => {
  for (const value of ['object-etag', '"object-etag",', '"object-etag" "other"', '*, "object-etag"', '"object-etag", broken', 'w/"object-etag"', '"object-etag"junk', 'x'.repeat(8193)]) assert.equal(matchesEtag(value, '"object-etag"'), false, value);
  assert.equal(matchesEtag(' "a,b", W/"object-etag" ', '"object-etag"'), true);
  assert.equal(matchesEtag('W/"a,b"', '"a,b"'), true);
});
test('73 MiB executable body is passed through without a read or allocation', async () => {
  let pulls = 0;
  const body = new ReadableStream({ pull() { pulls++; throw new Error('Premature body read'); } }, { highWaterMark: 0 });
  const response = await worker.fetch(request('/downloads/1.2.3/OrandOverlay.exe'), { RELEASES: {
    async get(key) { assert.equal(key, 'downloads/1.2.3/OrandOverlay.exe'); return { etag: 'large-object', size: 73 * 1024 * 1024, body }; },
  } });
  assert.equal(response.status, 200);
  assert.equal(response.body, body);
  assert.equal(pulls, 0);
  assert.equal(response.headers.get('Content-Length'), String(73 * 1024 * 1024));
  await response.body.cancel();
});
test('Range requests safely get a full 200, never an incorrect partial response', async () => {
  const response = await worker.fetch(request(routes[3][0], 'GET', { Range: 'bytes=0-9' }), fixture().env);
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('Content-Range'), null);
  assert.equal(response.headers.get('Accept-Ranges'), null);
  await response.body.cancel();
});
test('missing R2 binding is a generic 503', async () => {
  const response = await worker.fetch(request(routes[0][0]), {});
  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), { error: 'service_unavailable' });
});
test('invalid storage metadata is a generic 503', async () => {
  const response = await worker.fetch(request(routes[0][0], 'HEAD'), { RELEASES: { async head() { return { etag: 'bad\r\nheader', size: 1 }; } } });
  assert.equal(response.status, 503);
  assert.equal(response.body, null);
});
test('invalid URL values do not resolve', () => {
  for (const url of [null, '', 'file:///v1/profiles/win-x64', 'https://updates.example.invalid', ORIGIN + '/' + 'x'.repeat(2100)]) assert.equal(resolveRoute(url), null);
});

for (const filename of ['OrandOverlay.exe', 'RandyPick.exe']) {
  test('test-channel offer preserves exact immutable asset identity: ' + filename, async () => {
    const version = '1.2.3-test.20260914.1';
    const assetPath = '/downloads/' + version + '/' + filename;
    // The service transports signed envelopes opaquely; the client owns signature
    // and assetName/path agreement checks. Never reinterpret or redirect offers.
    const payload = { schemaVersion: 1, kind: 'application', channel: 'test', version,
      platform: 'win-x64', minimumUpdaterVersion: 1, publishedAtUtc: '2026-09-14T00:00:00Z',
      asset: { path: assetPath, size: bytes.length, sha256: 'a'.repeat(64) } };
    const envelope = new TextEncoder().encode(JSON.stringify({ schemaVersion: 1,
      keyId: 'test-fixture', payload: Buffer.from(JSON.stringify(payload)).toString('base64'),
      signature: Buffer.alloc(64).toString('base64') }));
    const calls = [];
    const env = { RELEASES: { async get(key) {
      calls.push(key);
      const data = key === 'channels/test/win-x64.json' ? envelope : key === assetPath.slice(1) ? bytes : null;
      return data && { etag: 'offer', size: data.length, body: new ReadableStream({ start(c) { c.enqueue(data); c.close(); } }) };
    } } };
    const offer = await worker.fetch(request('/v1/channels/test/win-x64'), env);
    assert.equal(offer.status, 200); secure(offer);
    assert.deepEqual(new Uint8Array(await offer.arrayBuffer()), envelope);
    const download = await worker.fetch(request(assetPath), env);
    assert.equal(download.status, 200); secure(download);
    assert.equal(download.headers.get('Content-Disposition'), 'attachment; filename="' + filename + '"');
    assert.equal(download.headers.get('Cache-Control'), 'public, max-age=31536000, immutable');
    assert.deepEqual(new Uint8Array(await download.arrayBuffer()), bytes);
    const other = filename === 'RandyPick.exe' ? 'OrandOverlay.exe' : 'RandyPick.exe';
    const missing = await worker.fetch(request('/downloads/' + version + '/' + other), env);
    assert.equal(missing.status, 404); secure(missing);
    assert.deepEqual(calls, ['channels/test/win-x64.json', assetPath.slice(1), 'downloads/' + version + '/' + other]);
  });
}
