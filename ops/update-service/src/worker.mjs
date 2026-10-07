const MANIFESTS = new Map([
  ['/v1/channels/stable/win-x64', 'channels/stable/win-x64.json'],
  ['/v1/channels/test/win-x64', 'channels/test/win-x64.json'],
  ['/v1/profiles/win-x64', 'channels/profiles/win-x64.json'],
]);
const SECURITY = {
  'X-Content-Type-Options': 'nosniff',
  'Content-Security-Policy': "default-src 'none'; frame-ancestors 'none'",
  'Referrer-Policy': 'no-referrer',
};
const JSON_TYPE = 'application/json; charset=utf-8';
const APPLICATION_FILENAMES = new Set(['OrandOverlay.exe', 'RandyPick.exe']);

// A bounded SemVer 2.0 identifier. No path decoding or normalization is done.
function validVersion(version) {
  if (version.length > 128) return false;
  const match = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/.exec(version);
  return !!match && (!match[4] || match[4].split('.').every(part => !/^[0-9]+$/.test(part) || part === '0' || !part.startsWith('0')));
}

export function resolveRoute(rawUrl) {
  if (typeof rawUrl !== 'string' || rawUrl.length > 2048 || /[%\\\s?#]/.test(rawUrl)) return null;
  const match = /^https?:\/\/[^/]+(\/.*)$/.exec(rawUrl);
  if (!match) return null;
  const pathname = match[1];
  if (MANIFESTS.has(pathname)) return { key: MANIFESTS.get(pathname), manifest: true, filename: null, type: JSON_TYPE };
  const asset = /^\/(downloads|profiles)\/([^/]+)\/(OrandOverlay\.exe|RandyPick\.exe|memory-profiles\.json)$/.exec(pathname);
  if (!asset || !validVersion(asset[2])) return null;
  if (asset[1] === 'downloads' && !APPLICATION_FILENAMES.has(asset[3])) return null;
  if (asset[1] === 'profiles' && asset[3] !== 'memory-profiles.json') return null;
  return { key: pathname.slice(1), manifest: false, filename: asset[3], type: asset[1] === 'downloads' ? 'application/octet-stream' : JSON_TYPE };
}

// If-None-Match uses weak comparison for GET/HEAD; malformed lists are ignored.
export function matchesEtag(value, current) {
  if (!value || value.length > 8192) return false;
  if (value.trim() === '*') return true;
  const pattern = /[ \t]*(?:W\/)?("[\x21\x23-\x7e\x80-\xff]*")[ \t]*(,|$)/y;
  let position = 0;
  let matched = false;
  while (position < value.length) {
    pattern.lastIndex = position;
    const token = pattern.exec(value);
    if (!token) return false;
    matched ||= token[1] === current;
    position = pattern.lastIndex;
    if (!token[2]) return matched && position === value.length;
    if (position === value.length) return false;
  }
  return false;
}

function errorResponse(method, status, error) {
  return new Response(method === 'HEAD' ? null : JSON.stringify({ error }), {
    status,
    headers: { ...SECURITY, 'Content-Type': JSON_TYPE, 'Cache-Control': 'no-store', ...(status === 405 ? { Allow: 'GET, HEAD' } : {}) },
  });
}

export default {
  async fetch(request, env) {
    const method = request.method;
    if (method !== 'GET' && method !== 'HEAD') return errorResponse(method, 405, 'method_not_allowed');
    const route = resolveRoute(request.url);
    if (!route) return errorResponse(method, 404, 'not_found');
    try {
      const object = method === 'HEAD' ? await env.RELEASES.head(route.key) : await env.RELEASES.get(route.key);
      if (!object) return errorResponse(method, 404, 'not_found');
      // Do not trust publisher-supplied HTTP/custom metadata, especially redirects,
      // CORS, content types, cache policy, and Content-Disposition.
      if (typeof object.etag !== 'string' || !/^[\x21\x23-\x7e]+$/.test(object.etag)) throw new Error('Invalid object metadata');
      const etag = '"' + object.etag + '"';
      const headers = new Headers({
        ...SECURITY,
        'Content-Type': route.type,
        'Content-Disposition': route.filename ? 'attachment; filename="' + route.filename + '"' : 'inline',
        'Cache-Control': route.manifest ? 'public, max-age=60' : 'public, max-age=31536000, immutable',
        ETag: etag,
      });
      if (matchesEtag(request.headers.get('If-None-Match'), etag)) {
        if (object.body) await object.body.cancel();
        return new Response(null, { status: 304, headers });
      }
      if (!Number.isSafeInteger(object.size) || object.size < 0) throw new Error('Invalid object size');
      headers.set('Content-Length', String(object.size));
      // Direct stream passthrough. Never call text(), json(), arrayBuffer(), or
      // collect chunks: the executable can be 73 MB or larger.
      return new Response(method === 'HEAD' ? null : object.body, { status: 200, headers });
    } catch {
      return errorResponse(method, 503, 'service_unavailable');
    }
  },
};
