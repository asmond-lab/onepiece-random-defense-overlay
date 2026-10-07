# randypick-web server verification

| check | command | observed | PASS/FAIL |
|---|---|---|---|
| 1 unit tests | `node --test test/*.test.mjs` | exit 0; tests 6, pass 6, fail 0 | PASS |
| 2 config dry-run | `npx wrangler deploy --dry-run --outdir C:/Users/123/AppData/Local/Temp/randypick-dryrun-v` | exit 0; "Read 6 files from the assets directory", "--dry-run: exiting now."; outdir deleted | PASS |
| 3a GET / | `curl -s -i http://127.0.0.1:8791/` | `HTTP/1.1 200 OK`; `Content-Type: text/html; charset=utf-8`; has `Content-Security-Policy: default-src 'none'; ...` and `Strict-Transport-Security: max-age=31536000; includeSubDomains`; body contains 랜디픽 | PASS |
| 3b GET /download | `curl -s -i -L http://127.0.0.1:8791/download` | `HTTP/1.1 200 OK` (no redirect), text/html; body contains 준비 중 | PASS |
| 3c GET /styles.css | `curl -s -i http://127.0.0.1:8791/styles.css` | `HTTP/1.1 200 OK`; `Content-Type: text/css; charset=utf-8` | PASS |
| 3d GET /does-not-exist | `curl -s -i http://127.0.0.1:8791/does-not-exist` | `HTTP/1.1 404 Not Found`; text/html; body Korean (link text "홈으로 돌아가기", footer "랜디픽 RandyPick · 비공식 팬 도구") | PASS |
| 3e POST / | `curl -s -i -X POST http://127.0.0.1:8791/` | `HTTP/1.1 405 Method Not Allowed`; `Allow: GET, HEAD` | PASS |
| 3f GET /healthz | `curl -s -i http://127.0.0.1:8791/healthz` | `HTTP/1.1 200 OK`; `Content-Type: application/json`; `Content-Length: 41`; body begins `{"status"` (full body was not captured; server stopped before re-fetch; value "ok" is not directly observed, though unit test "/healthz returns JSON" passed) | PASS (partial evidence) |
| 3g Host www | `curl -s -i -H "Host: www.randypick.com" http://127.0.0.1:8791/` | NOT TESTABLE LOCALLY: observed `HTTP/1.1 200 OK` text/html, no Location (local dev rewrites host). Unit test "www redirects keeping path and query" passed | NOT TESTABLE LOCALLY |
| 3h ".exe" in body of / | search body of GET / | 0 matches | PASS |
| 4 shutdown | `taskkill /F /T /PID <dev>`; `taskkill /F /IM workerd.exe`; `netstat -ano \| findstr :8791` | no LISTENING entry on 8791 (only TIME_WAIT leftovers); workerd kill issued (tasklist check errored on a quoting issue, so workerd absence is not separately confirmed) | PASS |
