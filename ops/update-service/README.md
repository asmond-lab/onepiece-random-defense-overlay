# Current deployment — normal-mode hotfix test.10 (2026-09-15)

Test feed now serves signed 1.0.2-test.10 (78,626,777 bytes; SHA256 `0eaf854593c0aff743a5f7c95bb294985eff9383962047fffdf7f4870fa10b76`). Existing test.9 updater and the packaged test.10 updater independently verified the public feed. The immutable EXE was downloaded with the production verifier before promotion. Stable/profile bodies are unchanged. Full evidence: `WORK-STATUS.md` and `tester-hotfix-test10/release/release-verification.json`. Never overwrite published versioned keys.

- Download: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.10/RandyPick.exe
- Test feed: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- The hotfix preserves last-known recommendations through transient hand-read failures, makes F1 the default no-repeat overlay key, keeps overlay/craft windows non-activating, and exposes explicit pending-update buttons without automatic installation.
- Public verification covered signed manifest byte identity, streamed length/digest/PE version, current and previous updater offers, same-version suppression, and unchanged stable/profile routes. It did not replace a live Warcraft III round.

---

## Previous deployment (preserved)

# Current deployment — 2026-09-15

The existing `orand-updates` Worker and private `orand-overlay-releases` bucket serve the signed **1.0.2-test.8** release through the test channel. The user explicitly approved publishing the verified local executable.

- Download: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.8/RandyPick.exe
- Test feed: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- SHA256: `e57c81ff78d309172bba99aff42143db5e31b106d3aa84d47aa8323b41b475fa` / 78,605,501 bytes
- The public download passed packaged signature, length, digest and executable-version checks before channel promotion. Previous public test.3 and current test.8 updater code accept the new feed; same-version offers are suppressed.
- Worker code/configuration, stable and profile channels remain unchanged. Never overwrite versioned keys.
- Announcement: [beta-1.0.2-test.8.md](../../docs/beta-1.0.2-test.8.md). Full evidence: [WORK-STATUS.md](../../WORK-STATUS.md).

## Historical implementation notes

The original pre-deployment design notes below are retained as history. Their statements that nothing is deployed and their old-only filename examples no longer describe the current deployment. Consult the current worker source and status above.

# Orand update service: Cloudflare Worker + private R2

Local implementation only. Nothing has been deployed, provisioned, uploaded, authenticated, or verified against Cloudflare. The names below are planned configuration, not existing-resource claims:

- Worker: `orand-updates`
- Private R2 bucket: `orand-overlay-releases`
- Worker binding: `RELEASES`

This directory is self-contained and does not modify the desktop client, other services, or MemoryDiagnostics. No build, external dependency installation, account access, keys, billing operations, or network calls are needed for its unit tests. No actual or fabricated release manifests are included.

## Local tests

Node 20+ with built-in Fetch/Streams APIs:

```powershell
Set-Location 'C:\Users\123\Desktop\dev\orand-overnight-overlay-20260907-111725\ops\update-service'
node --test test/worker.test.mjs
```

`npm test` is an equivalent convenience script. No `npm install` is needed. Tests use in-memory R2 mocks, not Cloudflare, Miniflare, Wrangler, or real release files. They cover exact mappings, GET/HEAD, versions, raw/encoded traversal rejection, method restrictions, errors, HTTP metadata isolation, caching, conditional requests, and direct passthrough of a stream advertising 73 MiB without allocating or reading that payload. This is not an integration/deployment test or a large-file network throughput test.

## HTTP contract

Only GET and HEAD are supported. R2 keys never have an initial slash.

| Exact path | R2 key | Cache-Control |
| --- | --- | --- |
| `/v1/channels/stable/win-x64` | `channels/stable/win-x64.json` | `public, max-age=60` |
| `/v1/channels/test/win-x64` | `channels/test/win-x64.json` | `public, max-age=60` |
| `/v1/profiles/win-x64` | `channels/profiles/win-x64.json` | `public, max-age=60` |
| `/downloads/{version}/OrandOverlay.exe` | `downloads/{version}/OrandOverlay.exe` | `public, max-age=31536000, immutable` |
| `/profiles/{version}/memory-profiles.json` | `profiles/{version}/memory-profiles.json` | `public, max-age=31536000, immutable` |

`version` is case-sensitive SemVer 2.0, at most 128 ASCII characters: three numeric components with no leading zeroes except zero, optional dot-separated prerelease identifiers, and optional build metadata. Numeric prerelease identifiers cannot have leading zeroes. Identifiers contain only ASCII alphanumerics and hyphens. There is no `v` prefix, `latest` alias, four-part version, underscore, whitespace, slash, or path decoding. Literal `+` for build metadata is allowed; percent-encoded `+` is not. This is a service contract, not a claim that existing desktop client versions already follow it.

All routes are exact and case-sensitive. Query strings (including empty `?`), fragments, percent encoding, backslashes, extra segments, trailing slashes, dot segments, and unsupported filenames/platforms/channels are rejected. There is no URL/redirect parameter, outbound fetch, directory listing, PUT/upload, delete, admin, or health endpoint. There is no GitHub redirect or fallback.

The router checks the raw URL supplied to the Worker without calling a URL parser or decoding it. A browser/proxy may normalize dot segments before the Worker receives the request; no origin handler can recover that lost spelling. If rejection of the original wire spelling is required at deployment, configure Cloudflare edge rules against the raw request URI to reject encoded paths, backslashes, and dot segments before normalization. Even after upstream normalization, only the five allowlisted route patterns can map to R2 objects.

### Response behavior

- Success: `200`, quoted R2 ETag, and full object `Content-Length`. JSON uses `application/json; charset=utf-8`; executables use `application/octet-stream`.
- Channel/profile manifests have `Content-Disposition: inline`. Versioned files have `attachment` with the fixed filename `OrandOverlay.exe` or `memory-profiles.json`. No request value is interpolated into a response header.
- HEAD uses R2 `head`, never `get`, and always returns an empty body, including on errors. It reports the same representation headers as GET.
- `If-None-Match` supports strong/weak tags, comma-separated lists, and `*` using weak comparison. Matches return bodyless `304` with ETag/cache/security headers and no Content-Length. Malformed or oversized conditions are ignored. Missing objects remain `404`, even for `*`.
- GET uses R2 `get` and passes its body stream directly into Response. A matching conditional request cancels that stream without buffering it. There is no whole-file `arrayBuffer()`, text/JSON parsing, or cache buffering. HEAD avoids body access entirely.
- Ranges are intentionally unsupported: a Range request receives a full `200`, not a false partial response. There is no `Accept-Ranges` advertisement or `Content-Range`. Conditional dates and other preconditions are not implemented; clients should use `If-None-Match`.
- Unknown/rejected routes or missing objects: `404`, `{"error":"not_found"}`.
- Unsupported methods: `405`, `Allow: GET, HEAD`, `{"error":"method_not_allowed"}`.
- R2/binding/metadata errors before response construction: `503`, `{"error":"service_unavailable"}`. No exception, account, bucket, or key details are exposed. A later stream failure aborts the transfer; an already-sent status cannot be changed to 503. Clients must treat incomplete downloads as failures.
- Errors are JSON with `Cache-Control: no-store`; HEAD suppresses the JSON body. All responses include `X-Content-Type-Options: nosniff`, restrictive CSP, and `Referrer-Policy: no-referrer`.
- Publisher-supplied HTTP/custom metadata is not forwarded. No CORS headers, credentials, cookies, or redirects are emitted. Browser cross-origin access is not enabled; this is intended for a desktop client.

`public` in Cache-Control allows caching of these public download responses. It does not make the R2 bucket itself public. A private bucket is storage access control, not end-user authentication: the configured Worker download routes are intentionally readable without client credentials.

## Signed manifest boundary

The three manifest objects must contain an opaque signed JSON envelope with exactly these contract fields:

| Field | Contract |
| --- | --- |
| `schemaVersion` | Integer `1` |
| `keyId` | Identifier selecting a public key already trusted/embedded by the client |
| `payload` | Standard Base64 encoding of the exact UTF-8 payload bytes |
| `signature` | Standard Base64 encoding of the raw IEEE P1363 ECDSA signature over the decoded payload bytes, using SHA-256 |

P1363 means fixed-width `r || s`, not ASN.1 DER. The signer and embedded client key must agree on the curve and signature width (for P-256, 64 signature bytes). Do not sign the Base64 text or reserialize the payload before verifying its signature.

The Worker stores no signing private key and performs no signing, envelope generation, parsing, or signature verification. It serves the stored bytes unchanged. It does not certify that an object is signed correctly. Unit-test text is explicitly opaque test data, not a manufactured release envelope.

A trusted release publisher prepares and validates real signed envelopes outside this service. Clients must verify schema, trusted `keyId`, Base64 encoding, and ECDSA signature using an embedded public key before trusting payload data or installing anything. They must not accept a public key downloaded beside the manifest as a new trust anchor. ETags and HTTPS do not replace signature verification.

The signed payload schema belongs to the client/publisher contract, not this transport Worker. That schema should bind the intended channel/platform, version, artifact path, SHA-256 digest, expected length, and any expiry/anti-rollback policy. Verify downloaded bytes against those authenticated values before execution or profile installation. Allow only the configured Cloudflare update origin and these versioned paths, and reject redirects/untrusted origins; do not trust arbitrary URLs merely because this Worker served an envelope containing them. This directory does not implement or assert completion of those client changes.

## Planned deployment, not executed

1. Separately provision the planned private bucket and review normal Cloudflare account prerequisites. Keep the R2 public development URL (`r2.dev`) disabled and do not attach public custom domains to the R2 bucket. Do not create public bucket access to bypass this Worker.
2. Use `wrangler.toml` to bind that bucket as `RELEASES`. It includes no account ID, credentials, secrets, or signing key. Worker R2 bindings are not permission-scoped to GET/HEAD by this file: read-only behavior is enforced by the audited handler, not a claim of a read-only IAM binding. Restrict who can deploy replacement Worker code.
3. Configure an approved HTTPS custom domain/route for the Worker in a separately reviewed deployment change. No domain is invented here. `workers_dev = false` and `preview_urls = false` intentionally avoid default public/preview endpoints; the checked-in config alone supplies no public route.
4. Using a separately installed, approved Wrangler CLI and already-authorized deployment environment, the later deployment command from this directory is `wrangler deploy --config .\wrangler.toml`. It has NOT been run as part of this implementation.
5. Review edge normalization/WAF rules as described above. Do not configure cache rules that override the 60-second mutable-manifest policy or cache error responses. Validate HEAD, 304, unknown-path rejection, and genuine signed downloads in a separately authorized integration test.

No deployment or client update base URL is claimed to exist yet. Clients should ultimately use the chosen Cloudflare Worker origin, not GitHub Releases, raw GitHub URLs, GitHub APIs, or GitHub-hosted manifests. GitHub may remain a source-code repository; it is not the client update path.

## Publisher separation

There is no publisher HTTP endpoint. Publishing is a separate trusted maintenance operation using Wrangler's R2 CLI, not a client capability or Worker secret. Grant publisher access only to the intended bucket where supported, keep deployment access separate where possible, and keep all signing private keys in the publisher's external signing environment. Never put credentials or private keys in this directory, Worker variables, source control, or client bundles.

The following is a future manual publishing outline only, not an executed script. `$Version`, `$ReleaseDir`, and `$SignedManifest` must refer to a genuine reviewed release and an already-created, verified signed envelope. Use a reviewed installed Wrangler version, not a network-installing `npx` invocation:

```powershell
# Upload a NEW versioned key. Never overwrite an already-published version.
wrangler r2 object put "orand-overlay-releases/downloads/$Version/RandyPick.exe" --file "$ReleaseDir/RandyPick.exe" --remote
# Profile assets are separate and are not deleted when an application version rotates.
# wrangler r2 object put "orand-overlay-releases/profiles/$ProfileVersion/memory-profiles.json" --file "$ReleaseDir/memory-profiles.json" --remote

# Only after verifying the public download digest, promote the channel pointer.
wrangler r2 object put 'orand-overlay-releases/channels/test/win-x64.json' --file "$SignedManifest" --remote
# wrangler r2 object put 'orand-overlay-releases/channels/stable/win-x64.json' --file "$SignedManifest" --remote
# wrangler r2 object put 'orand-overlay-releases/channels/profiles/win-x64.json' --file "$SignedProfileManifest" --remote

# After the live channel points at $Version, delete application objects older than
# current + previous 3. Keep those four executables and all profile objects.
# See docs/r2-release-retention.md.
wrangler r2 object delete "orand-overlay-releases/downloads/$TooOldVersion/RandyPick.exe" --remote
```

CLI `put` can overwrite objects; do not reuse a versioned download key. Corrections require a new version and a new signed channel pointer. Keep the live executable, the previous three application versions for rollback, and profile feed/objects. Delete only keys older than that window. Validate signatures, digest/size, and the live channel before deleting. Expect up to 60 seconds of manifest staleness. Operator policy (Korean): [r2-release-retention.md](../../docs/r2-release-retention.md).
