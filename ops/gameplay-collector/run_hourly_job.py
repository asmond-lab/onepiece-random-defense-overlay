# /// script
# requires-python = ">=3.12"
# dependencies = []
# ///
"""Run: python run_hourly_job.py --run. Registration is a separate approved step.

The scheduled command always collects AND publishes through the existing learner.
Only fixed status fields are emitted; credentials and child output are never logged.
"""
import argparse
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import datetime, timezone
import errno
import json
import msvcrt
import os
from pathlib import Path
import subprocess
import sys
from typing import Iterator
from urllib.parse import urlsplit


class ConfigurationError(ValueError):
    """Non-secret configuration failure."""


@dataclass(frozen=True, slots=True)
class Settings:
    endpoint: str
    state_directory: Path

    @classmethod
    def from_environment(cls) -> "Settings":
        endpoint = os.environ.get("ORAND_GAMEPLAY_ENDPOINT", "")
        directory = os.environ.get("ORAND_GAMEPLAY_STATE_DIRECTORY", "")
        token = os.environ.get("ORAND_COLLECTOR_TOKEN", "")
        origin = urlsplit(endpoint)
        secure = origin.scheme == "https" or (origin.scheme == "http" and origin.hostname in ("127.0.0.1", "localhost", "::1"))
        if not secure or not origin.hostname or origin.username or origin.password or origin.query or origin.fragment or origin.path not in ("", "/"):
            raise ConfigurationError("endpoint-origin")
        if not directory or not Path(directory).is_absolute():
            raise ConfigurationError("absolute-state-directory")
        if not token.strip() or len(token) > 4096 or "\r" in token or "\n" in token:
            raise ConfigurationError("collector-credential")
        return cls(endpoint.rstrip("/"), Path(directory))


@contextmanager
def exclusive_run(directory: Path) -> Iterator[bool]:
    """A process-lifetime Windows lock; abrupt process exit also releases it."""
    directory.mkdir(parents=True, exist_ok=True)
    with (directory / "hourly-job.lock").open("a+b") as handle:
        if handle.tell() == 0:
            handle.write(b"0")
            handle.flush()
        handle.seek(0)
        try:
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as error:
            if error.errno not in (errno.EACCES, errno.EAGAIN, errno.EDEADLK):
                raise
            yield False
            return
        try:
            yield True
        finally:
            handle.seek(0)
            msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)


def execute(settings: Settings, maximum_seconds: int) -> dict[str, str | int]:
    """One bounded collection/publication run; no implicit registration or git."""
    with exclusive_run(settings.state_directory) as acquired:
        if not acquired:
            return {"status": "already-running", "exitCode": 0}
        command = [sys.executable, str(Path(__file__).with_name("collect_telemetry.py")),
                   "--collect", "--publish", "--endpoint", settings.endpoint,
                   "--artifacts", str(settings.state_directory)]
        status: dict[str, str | int]
        try:
            result = subprocess.run(command, capture_output=True, text=True,
                                    timeout=maximum_seconds, check=False)
            if result.returncode:
                status = {"status": "collector-failed", "exitCode": 1, "collectorExitCode": result.returncode}
            else:
                counts = json.loads(result.stdout)
                if not isinstance(counts, dict) or set(counts) != {"cohorts", "published"} or any(
                    type(value) is not int or not 0 <= value <= 2147483647 for value in counts.values()
                ):
                    raise ConfigurationError("collector-status")
                status = {"status": "completed", "exitCode": 0, "cohorts": counts["cohorts"], "published": counts["published"]}
        except subprocess.TimeoutExpired:
            # subprocess.run kills and waits for this direct collector process on timeout.
            status = {"status": "timeout", "exitCode": 124}
        except (json.JSONDecodeError, ConfigurationError):
            status = {"status": "invalid-collector-status", "exitCode": 65}
        except OSError:
            status = {"status": "collector-start-failed", "exitCode": 71}
        status["finishedAt"] = datetime.now(timezone.utc).isoformat()
        status["maximumSeconds"] = maximum_seconds
        temporary = settings.state_directory / "job-status.tmp"
        temporary.write_text(json.dumps(status), encoding="utf-8")
        temporary.replace(settings.state_directory / "job-status.json")
        return status


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", action="store_true", help="Explicit scheduled collect-and-publish authorization")
    parser.add_argument("--max-seconds", type=int, default=900, help="Collector deadline, 1..900 seconds (default 900)")
    args = parser.parse_args()
    if not args.run or not 1 <= args.max_seconds <= 900:
        parser.error("--run and a deadline from 1 through 900 seconds are required")
    try:
        status = execute(Settings.from_environment(), args.max_seconds)
    except (ValueError, OSError):
        # Do not interpolate exceptions: configuration values may contain credentials.
        status = {"status": "configuration-or-status-storage-failed", "exitCode": 78}
    print(json.dumps(status))
    code = status["exitCode"]
    return code if isinstance(code, int) else 1


if __name__ == "__main__":
    sys.exit(main())
