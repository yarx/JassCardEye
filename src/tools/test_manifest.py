"""Checks when a training run, and each of its variants, counts as finished.

That verdict decides more than the wording of a message: a release started without a run id ships
the newest run whose manifest says finished. It does not follow a variant's exit code - a variant
can abort while Python shuts down, after all of its results are written - but what the variant left
behind. `src/scripts/write_manifest.py` decides that, and this is what holds the rule to account.
Runs in CI, needs nothing but the standard library.

    python src/tools/test_manifest.py
"""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parents[2] / "src" / "scripts"
sys.path.insert(0, str(SCRIPTS))
from write_manifest import ABORTED_AFTER_COMPLETION, variant_record  # noqa: E402

EPOCHS = 20
FAILURES: list[str] = []


def variant(root: Path, name: str, *, exit_code: int = 0, epochs: int = EPOCHS, best: bool = True,
            metrics: dict | None = None) -> None:
    """A variant's folder as the run script leaves it: run.json, and best.pt once an epoch improved."""
    folder = root / name
    (folder / "weights").mkdir(parents=True, exist_ok=True)
    if best:
        (folder / "weights" / "best.pt").write_bytes(b"weights")
    if metrics is None:
        metrics = {"mAP50": 0.93, "mAP50_95": 0.91, "best_epoch": epochs,
                   "epochs_completed": epochs, "fitness": 0.92}
    (folder / "run.json").write_text(json.dumps(
        {"variant": name, "train_seconds": 3600, "exit_code": exit_code, "metrics": metrics}))


def check(what: str, record: dict, state: str, note: str | None = None) -> None:
    # An unreadable run.json carries the parser's message as its note, which is not worth pinning.
    ok = record.get("state") == state and (state == "unreadable" or record.get("note") == note)
    said = record.get("state", "?") + (f", {record['note']}" if record.get("note") else "")
    print(f"  {'ok  ' if ok else 'FAIL'} {what}: {said}")
    if not ok:
        FAILURES.append(f"{what}: expected {state}{', ' + note if note else ''}, got {said}")


def test_variants() -> None:
    print("a variant:")
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)

        variant(root, "c")
        check("every epoch, best.pt, exit 0", variant_record(root, "c", EPOCHS), "finished")

        variant(root, "b2", exit_code=134)
        check("every epoch, best.pt, then aborted while Python shut down",
              variant_record(root, "b2", EPOCHS), "finished", ABORTED_AFTER_COMPLETION)

        # The case best.pt and run.json alone would get wrong: Ultralytics writes best.pt after the first
        # epoch that improves, and the run script writes run.json after a crash as well.
        variant(root, "died", exit_code=137, epochs=12)
        check("died in epoch 12 of 20 - best.pt and run.json there all the same",
              variant_record(root, "died", EPOCHS), "failed")

        variant(root, "no-weights", best=False)
        check("every epoch in the metrics, but no best.pt", variant_record(root, "no-weights", EPOCHS), "failed")

        variant(root, "no-epoch", exit_code=1, best=False,
                metrics={"epochs_completed": 0, "note": "no results.csv at output/training/x/results.csv"})
        check("never finished an epoch", variant_record(root, "no-epoch", EPOCHS), "failed")

        variant(root, "unranked", metrics={"epochs_completed": EPOCHS, "note": "no rankable metric"})
        check("exit 0 and best.pt, but no metric that could be ranked",
              variant_record(root, "unranked", EPOCHS), "failed")

        variant(root, "nan", metrics={"mAP50": float("nan"), "mAP50_95": float("nan"), "best_epoch": 0,
                                      "epochs_completed": EPOCHS, "fitness": float("nan")})
        check("a fitness that is not a number", variant_record(root, "nan", EPOCHS), "failed")

        check("not trained yet", variant_record(root, "later", EPOCHS), "pending")

        (root / "cut-off").mkdir()
        (root / "cut-off" / "run.json").write_text("{\"variant\": ")
        check("run.json cut off", variant_record(root, "cut-off", EPOCHS), "unreadable")


def finish(root: Path, variants: str, *extra: str) -> subprocess.CompletedProcess:
    """A manifest for these variants, then the end of the run - through the script, as the run does it."""
    script = str(SCRIPTS / "write_manifest.py")
    subprocess.run([sys.executable, script, "--artifacts", str(root), "--init", "--run-id", "test",
                    "--variants", variants, "--size", "n", "--count", "500", "--seed", "1",
                    "--epochs", str(EPOCHS), "--imgsz", "640", "--batch", "64", "--git-sha", "0000000"],
                   check=True, capture_output=True)
    return subprocess.run([sys.executable, script, "--artifacts", str(root), "--finish", *extra],
                          capture_output=True, text=True)


def expect(what: str, root: Path, result: subprocess.CompletedProcess, printed: str) -> None:
    manifest = json.loads((root / "manifest.json").read_text())
    state = printed.split()[0]
    ok = (result.returncode == 0 and result.stdout.strip() == printed
          and manifest["state"] == state and manifest["finished_at"])
    print(f"  {'ok  ' if ok else 'FAIL'} {what}: {result.stdout.strip() or result.stderr.strip()}")
    if not ok:
        FAILURES.append(f"{what}: expected '{printed}', got '{result.stdout.strip()}' "
                        f"and state {manifest['state']}")


def test_runs() -> None:
    print("\na run, through write_manifest.py --finish (prints the state and the variants that did not finish):")
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        # Four complete variants, of which b2 aborted after completing.
        for name in ("c", "b1", "a"):
            variant(root, name)
        variant(root, "b2", exit_code=134)
        expect("four complete variants, one aborted afterwards", root, finish(root, "c,b1,b2,a"), "finished 0")

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        variant(root, "c")
        variant(root, "b1", exit_code=137, epochs=12)
        expect("one variant died in its twelfth epoch", root, finish(root, "c,b1"), "failed 1")

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        variant(root, "c")
        expect("one variant never ran", root, finish(root, "c,a"), "failed 1")

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        variant(root, "c")
        result = finish(root, "c", "--state", "finished")
        ok = result.returncode != 0 and "--finish decides the state itself" in result.stderr
        print(f"  {'ok  ' if ok else 'FAIL'} --finish refuses a --state beside it")
        if not ok:
            FAILURES.append("--finish accepted a --state beside it")


def main() -> int:
    test_variants()
    test_runs()
    if FAILURES:
        print("\nFAILED:")
        for failure in FAILURES:
            print(f"  {failure}")
        return 1
    print("\nall manifest checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
