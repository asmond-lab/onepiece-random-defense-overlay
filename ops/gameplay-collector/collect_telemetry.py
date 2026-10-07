# /// script
# requires-python = ">=3.12"
# dependencies = []
# ///
"""Run: python collect_telemetry.py --help. Network requires explicit flags.

SQLite atomically persists raw packets and cursor; retention is 30 days. A failed
page never advances the cursor. No git, deploy, or implicit publish operations.
"""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
from contextlib import closing
import sqlite3
import urllib.parse
import urllib.request

from gameplay_wire import Json, json_array, json_object, load_json, parse_packet, require, text
from telemetry_learning import aggregate_packets


class Collector:
    def __init__(self, root: Path) -> None:
        root.mkdir(parents=True, exist_ok=True)
        self.root = root
        self.database = root / "collector.sqlite3"
        with closing(sqlite3.connect(self.database)) as db, db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS packets(id TEXT PRIMARY KEY, payload TEXT NOT NULL,
                    received REAL NOT NULL);
                CREATE TABLE IF NOT EXISTS state(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """)

    def ingest(self, value: Json, now: float) -> None:
        page = json_object(value)
        require(set(page) == {"packets", "nextCursor"}, "page fields")
        cursor = page["nextCursor"]
        require(cursor is None or isinstance(cursor, str) and 0 < len(cursor) <= 512, "cursor")
        packets = [parse_packet(p) for p in json_array(page["packets"])]
        with closing(sqlite3.connect(self.database)) as db, db:
            db.execute("DELETE FROM packets WHERE received < ?", (now - 30 * 86400,))
            for packet in packets:
                prior = db.execute("SELECT payload FROM packets WHERE id = ?", (packet.packet_id,)).fetchone()
                require(prior is None or prior[0] == packet.canonical, "conflicting packet")
                db.execute("INSERT OR IGNORE INTO packets VALUES (?, ?, ?)",
                           (packet.packet_id, packet.canonical, now))
            # Validate cross-packet identities before committing either raw data or cursor.
            aggregate_packets(load_json(row[0]) for row in db.execute("SELECT payload FROM packets"))
            if cursor is not None:
                db.execute("INSERT OR REPLACE INTO state VALUES ('cursor', ?)", (cursor,))

    def cursor(self) -> str | None:
        with closing(sqlite3.connect(self.database)) as db, db:
            row = db.execute("SELECT value FROM state WHERE key = 'cursor'").fetchone()
            return row[0] if row else None

    def snapshots(self, now: float) -> list[dict[str, Json]]:
        with closing(sqlite3.connect(self.database)) as db, db:
            db.execute("DELETE FROM packets WHERE received < ?", (now - 30 * 86400,))
            stats = aggregate_packets(load_json(row[0]) for row in db.execute("SELECT payload FROM packets"))
        envelopes = [dict(schemaVersion=3, mapScriptSha256=h, difficulty=d, stats=s)
                     for (h, d), s in stats.items()]
        for envelope in envelopes:
            path = self.root / f"{envelope['mapScriptSha256']}-{envelope['difficulty']}.json"
            temporary = path.with_suffix(".tmp")
            temporary.write_text(json.dumps(envelope, allow_nan=False), encoding="utf-8")
            temporary.replace(path)
        return envelopes


class NoCredentialRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl) -> None:
        """Bearer requests must stay on the explicitly configured service origin."""
        return None


def request_json(url: str, token: str, payload: Json = None) -> Json:
    data = None if payload is None else json.dumps(payload, allow_nan=False).encode()
    request = urllib.request.Request(url, data=data, method="GET" if data is None else "PUT",
                                     headers={"Authorization": f"Bearer {token}",
                                              "Content-Type": "application/json",
                                              "User-Agent": "OrandOverlay-GameplayCollector/3"})
    with urllib.request.build_opener(NoCredentialRedirects()).open(request, timeout=30) as response:
        body = response.read(32 * 1024 * 1024 + 1)
        require(len(body) <= 32 * 1024 * 1024, "response too large")
        return load_json(body)


def collect(collector: Collector, endpoint: str, token: str) -> None:
    seen: set[str] = set()
    cursor = collector.cursor()
    while True:
        query = urllib.parse.urlencode({"limit": 50, **({"cursor": cursor} if cursor else {})})
        page = request_json(endpoint.rstrip("/") + "/v3/gameplay?" + query, token)
        next_value = json_object(page).get("nextCursor")
        next_cursor = None if next_value is None else text(next_value)
        require(next_cursor is None or next_cursor not in seen and next_cursor != cursor, "cursor did not advance")
        collector.ingest(page, datetime.now(timezone.utc).timestamp())
        if next_cursor is None:
            return
        seen.add(next_cursor)
        cursor = next_cursor


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts", type=Path, default=Path(".gameplay-learning-artifacts"))
    parser.add_argument("--input", type=Path, help="Local raw page JSON; no network")
    parser.add_argument("--collect", action="store_true", help="Explicitly authorize raw collection")
    parser.add_argument("--publish", action="store_true", help="Explicitly authorize aggregate PUT")
    parser.add_argument("--endpoint", help="Service origin; no default remote target")
    parser.add_argument("--token-env", default="ORAND_COLLECTOR_TOKEN")
    args = parser.parse_args()
    token = os.environ.get(args.token_env, "")
    if args.collect or args.publish:
        if not args.endpoint or not token:
            parser.error("--endpoint and collector token are required")
        origin = urllib.parse.urlsplit(args.endpoint)
        if origin.scheme != "https" and not (origin.scheme == "http" and origin.hostname in ("localhost", "127.0.0.1", "::1")):
            parser.error("HTTPS required except loopback mock HTTP")
    collector = Collector(args.artifacts)
    now = datetime.now(timezone.utc).timestamp()
    if args.input:
        collector.ingest(load_json(args.input.read_text(encoding="utf-8")), now)
    if args.collect:
        collect(collector, args.endpoint, token)
    snapshots = collector.snapshots(now)
    if args.publish:
        for snapshot in snapshots:
            request_json(args.endpoint.rstrip("/") + "/v3/live-stats", token, snapshot)
    print(json.dumps({"cohorts": len(snapshots), "published": len(snapshots) if args.publish else 0}))


if __name__ == "__main__":
    main()
