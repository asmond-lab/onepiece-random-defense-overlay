"""Local HTTP and CLI verification. No external service or user state."""
import copy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
from contextlib import closing
import sqlite3
import subprocess
import sys
import tempfile
import threading
import unittest

from collect_telemetry import Collector
from test_learning import HASH, packet, population


class CollectorTests(unittest.TestCase):
    def test_conflict_rolls_back_raw_and_cursor_together(self):
        with tempfile.TemporaryDirectory(prefix=".gameplay-learning-artifacts-") as temporary:
            collector = Collector(Path(temporary))
            original = packet(1)
            collector.ingest({"packets": [original], "nextCursor": "1"}, 100)
            conflict = copy.deepcopy(original)
            conflict["events"][0]["inventory"] = {"other": 1}
            with self.assertRaises(ValueError):
                collector.ingest({"packets": [packet(2), conflict], "nextCursor": "2"}, 101)
            self.assertEqual("1", collector.cursor())
            self.assertEqual(1, collector.snapshots(101)[0]["stats"]["totalRecords"])

    def test_raw_retention_is_bounded_without_time_waits(self):
        with tempfile.TemporaryDirectory(prefix=".gameplay-learning-artifacts-") as temporary:
            collector = Collector(Path(temporary))
            collector.ingest({"packets": [packet(1)], "nextCursor": None}, 100)
            self.assertEqual([], collector.snapshots(100 + 30 * 86400 + 1))
            with closing(sqlite3.connect(collector.database)) as db, db:
                self.assertEqual(0, db.execute("SELECT count(*) FROM packets").fetchone()[0])

    def test_cli_mock_cursor_collection_and_explicit_publish(self):
        published = []
        failures = []
        fixture = population()

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_args):
                return

            def respond(self, body):
                encoded = json.dumps(body).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(encoded)))
                self.end_headers()
                self.wfile.write(encoded)

            def do_GET(self):
                if self.headers.get("Authorization") != "Bearer local-test-only":
                    failures.append("authorization")
                if "cursor=next" in self.path:
                    self.respond({"packets": fixture[20:], "nextCursor": None})
                else:
                    self.respond({"packets": fixture[:20], "nextCursor": "next"})

            def do_PUT(self):
                published.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
                self.respond({"accepted": True})

        with tempfile.TemporaryDirectory(prefix=".gameplay-learning-artifacts-") as temporary:
            server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            try:
                command = [sys.executable, str(Path(__file__).with_name("collect_telemetry.py")),
                           "--artifacts", temporary, "--endpoint", f"http://127.0.0.1:{server.server_port}",
                           "--collect", "--publish"]
                result = subprocess.run(command, capture_output=True, text=True, timeout=15,
                                        env=dict(os.environ, ORAND_COLLECTOR_TOKEN="local-test-only"))
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual([], failures)
                self.assertEqual(1, len(published))
                self.assertEqual(HASH, published[0]["mapScriptSha256"])
                self.assertEqual(.1, published[0]["stats"]["goalWeights"]["goal"]["support"])
                self.assertEqual({"cohorts": 1, "published": 1}, json.loads(result.stdout))
            finally:
                server.shutdown()
                server.server_close()
                thread.join(timeout=2)
                self.assertFalse(thread.is_alive())


if __name__ == "__main__":
    unittest.main()
