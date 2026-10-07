"""Scheduling entry-point tests; synthetic credentials and loopback HTTP only."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from unittest.mock import patch

from test_learning import HASH, population


class HourlyJobTests(unittest.TestCase):
    def setUp(self):
        self.job = importlib.import_module("run_hourly_job")

    def test_missing_configuration_cannot_collect(self):
        with patch.dict(os.environ, {}, clear=True), patch("subprocess.run") as child:
            with self.assertRaises(ValueError):
                self.job.Settings.from_environment()
            child.assert_not_called()

    def test_deadline_is_forwarded_and_timeout_is_reported_without_secrets(self):
        with tempfile.TemporaryDirectory(prefix=".gameplay-learning-artifacts-") as root:
            settings = self.job.Settings("https://fixture.invalid", Path(root))
            secret = "synthetic-secret-never-log"
            with patch("subprocess.run", side_effect=subprocess.TimeoutExpired(secret, 7)) as child:
                result = self.job.execute(settings, 7)
            self.assertEqual(124, result["exitCode"])
            self.assertEqual("timeout", result["status"])
            self.assertEqual(7, child.call_args.kwargs["timeout"])
            self.assertNotIn(secret, json.dumps(result))

    def test_overlapping_job_is_skipped_without_running_collector(self):
        with tempfile.TemporaryDirectory(prefix=".gameplay-learning-artifacts-") as root:
            settings = self.job.Settings("https://fixture.invalid", Path(root))
            with self.job.exclusive_run(settings.state_directory) as acquired:
                self.assertTrue(acquired)
                with patch("subprocess.run") as child:
                    self.assertEqual("already-running", self.job.execute(settings, 10)["status"])
                    child.assert_not_called()

    def test_child_failure_is_nonzero_and_never_prints_child_output(self):
        with tempfile.TemporaryDirectory(prefix=".gameplay-learning-artifacts-") as root:
            settings = self.job.Settings("https://fixture.invalid", Path(root))
            failure = subprocess.CompletedProcess([], 1, stdout="secret", stderr="private-token")
            with patch("subprocess.run", return_value=failure):
                result = self.job.execute(settings, 10)
            self.assertEqual(1, result["exitCode"])
            self.assertNotIn("private-token", json.dumps(result))
            self.assertNotIn("secret", json.dumps(result))
            status = json.loads((Path(root) / "job-status.json").read_text())
            self.assertEqual("collector-failed", status["status"])

    def test_powershell_job_decrypts_private_config_and_publishes_automatically(self):
        published = []
        failures = []
        secret = "synthetic-dpapi-collector-token"
        fixture = population()

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_args):
                return

            def respond(self, value):
                if self.headers.get("Authorization") != "Bearer " + secret:
                    failures.append("credential")
                body = json.dumps(value).encode()
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def do_GET(self):
                self.respond({"packets": fixture, "nextCursor": None})

            def do_PUT(self):
                published.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
                self.respond({"accepted": True})

        with tempfile.TemporaryDirectory(prefix=".gameplay-learning-artifacts-") as temporary:
            root = Path(temporary)
            server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            try:
                environment = dict(os.environ, FIXTURE_TOKEN=secret,
                    FIXTURE_ENDPOINT=f"http://127.0.0.1:{server.server_port}",
                    FIXTURE_STATE=str(root / "state"), FIXTURE_CONFIG=str(root / "private.clixml"),
                    FIXTURE_LAUNCHER=str(Path(__file__).with_name("run-hourly-job.ps1").resolve()),
                    FIXTURE_PYTHON=sys.executable, ORAND_COLLECTOR_TOKEN="must-be-overridden")
                script = """
                    $ErrorActionPreference = 'Stop'
                    $secure = ConvertTo-SecureString $env:FIXTURE_TOKEN -AsPlainText -Force
                    $credential = New-Object System.Management.Automation.PSCredential ('collector', $secure)
                    [pscustomobject]@{ Version = 1; Endpoint = $env:FIXTURE_ENDPOINT;
                        StateDirectory = $env:FIXTURE_STATE; Credential = $credential } |
                        Export-Clixml -LiteralPath $env:FIXTURE_CONFIG
                    & $env:FIXTURE_LAUNCHER -PythonPath $env:FIXTURE_PYTHON -ConfigPath $env:FIXTURE_CONFIG -MaxSeconds 30
                    exit $LASTEXITCODE
                """
                result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive",
                                         "-ExecutionPolicy", "RemoteSigned", "-Command", script],
                                        capture_output=True, text=True, timeout=40, env=environment)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertNotIn(secret, result.stdout + result.stderr)
                self.assertEqual([], failures)
                self.assertEqual(1, len(published))
                self.assertEqual(HASH, published[0]["mapScriptSha256"])
                self.assertEqual(.1, published[0]["stats"]["goalWeights"]["goal"]["support"])
                status = json.loads(result.stdout)
                self.assertEqual("completed", status["status"])
                self.assertEqual(1, status["published"])
                self.assertEqual(status, json.loads((root / "state" / "job-status.json").read_text()))
                self.assertNotIn(secret, (root / "private.clixml").read_text(encoding="utf-16"))
            finally:
                server.shutdown()
                server.server_close()
                thread.join(timeout=2)
                self.assertFalse(thread.is_alive())

    def test_collector_refuses_redirects_before_forwarding_credentials(self):
        from collect_telemetry import request_json
        received = []

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_args):
                return

            def do_GET(self):
                received.append(self.path)
                if self.path == "/initial":
                    self.send_response(302)
                    self.send_header("Location", "/unexpected")
                else:
                    self.send_response(200)
                self.send_header("Content-Length", "2")
                self.end_headers()
                self.wfile.write(b"{}")

        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with self.assertRaises(HTTPError):
                request_json(f"http://127.0.0.1:{server.server_port}/initial", "local-token-only")
            self.assertEqual(["/initial"], received)
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)
            self.assertFalse(thread.is_alive())

    def test_credentials_in_origin_are_rejected(self):
        environment = {"ORAND_GAMEPLAY_ENDPOINT": "https://user:secret@fixture.invalid",
                       "ORAND_GAMEPLAY_STATE_DIRECTORY": str(Path(tempfile.gettempdir()).resolve()),
                       "ORAND_COLLECTOR_TOKEN": "local-only"}
        with patch.dict(os.environ, environment, clear=True):
            with self.assertRaises(ValueError):
                self.job.Settings.from_environment()


if __name__ == "__main__":
    unittest.main()
