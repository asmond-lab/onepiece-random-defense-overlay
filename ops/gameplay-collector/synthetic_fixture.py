"""Local-only fixture entry point for the C# end-to-end consumer test."""
import json
from pathlib import Path
import subprocess
import sys

from test_learning import HASH, packet, population


def main() -> None:
    root = Path(sys.argv[1])
    root.mkdir(parents=True, exist_ok=True)
    valid = population()
    invalid = [packet(i + 100, i < 20, i < 18, map_hash="b" * 64) for i in range(40)]
    for fragment in invalid:
        fragment["events"][-1]["outcomeSource"] = "clearRound"
    input_file = root / "synthetic-page.json"
    input_file.write_text(json.dumps({"packets": valid + invalid + valid, "nextCursor": None}), encoding="utf-8")
    result = subprocess.run([sys.executable, str(Path(__file__).with_name("collect_telemetry.py")),
                             "--input", str(input_file), "--artifacts", str(root)],
                            capture_output=True, text=True, timeout=15)
    if result.returncode:
        raise RuntimeError(result.stderr)
    assert json.loads(result.stdout) == {"cohorts": 1, "published": 0}
    assert not (root / ("b" * 64 + "-신.json")).exists()
    envelope = json.loads((root / (HASH + "-신.json")).read_text(encoding="utf-8"))
    print(json.dumps(envelope["stats"]))


if __name__ == "__main__":
    main()
