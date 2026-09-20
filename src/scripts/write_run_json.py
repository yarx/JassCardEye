"""What one variant of a run achieved: its metrics, how long it took, and how it ended.

Deliberately nothing else. Which commit, which seed, how many images, which GPU - those describe the
*run*, and the run has its own record in manifest.json one folder up. Writing them here as well
would give every variant another copy of the same facts, and a copy is a thing that can be wrong on
its own.

For the thesis the two together are the chain, and they sit in the same folder: manifest.json says
which code and which data, this says what came out. Seed plus commit recreate the exact images,
because the generator is deterministic.

Runs inside the pod, where only the standard library is available.
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path

# Ultralytics pads its header names, and which columns exist depends on the task: detection runs
# report mAP, classification runs top-1 accuracy.
METRIC_COLUMNS = {
    "mAP50": "metrics/mAP50(B)",
    "mAP50_95": "metrics/mAP50-95(B)",
    "precision": "metrics/precision(B)",
    "recall": "metrics/recall(B)",
    "top1": "metrics/accuracy_top1",
    "top5": "metrics/accuracy_top5",
}


def fitness(values: dict[str, float]) -> float | None:
    """The score an epoch is ranked by: detection 0.1*mAP50 + 0.9*mAP50-95, classification top-1.

    For the box heads that is exactly Ultralytics' own fitness, by which best.pt is chosen, so the
    epoch reported here is the epoch whose weights ship. Ranking a detector by a single metric
    instead would describe one epoch while the shipped weights come from another.

    For the classifiers Ultralytics ranks by the mean of top-1 and top-5, while this takes top-1
    alone, so for variants a and b2 the epoch reported here can differ from the one in best.pt.
    """
    if values.get("mAP50") is not None and values.get("mAP50_95") is not None:
        return 0.1 * values["mAP50"] + 0.9 * values["mAP50_95"]
    return values.get("top1")


def read_metrics(results: Path) -> dict:
    """Metrics of the best epoch, not the last - best.pt holds exactly those weights."""
    if not results.exists():
        return {"epochs_completed": 0, "note": f"no results.csv at {results}"}

    with results.open() as handle:
        rows = [row for row in csv.DictReader(handle) if row.get("epoch")]
    if not rows:
        return {"epochs_completed": 0, "note": "results.csv is empty"}

    header = {name.strip(): name for name in rows[0]}

    def column(key: str) -> list[float] | None:
        raw = METRIC_COLUMNS[key]
        if raw not in header:
            return None
        values = []
        for row in rows:
            try:
                values.append(float(row[header[raw]]))
            except (TypeError, ValueError):
                values.append(float("nan"))
        return values

    columns = {key: values for key, values in
               ((key, column(key)) for key in METRIC_COLUMNS) if values}
    if not columns:
        return {"epochs_completed": len(rows), "note": "no known metric column"}

    scores = [fitness({key: values[i] for key, values in columns.items()})
              for i in range(len(rows))]
    if all(score is None for score in scores):
        return {"epochs_completed": len(rows), "note": "no rankable metric"}

    best_index = max(range(len(rows)), key=lambda i: scores[i] if scores[i] is not None else -1)
    metrics = {key: values[best_index] for key, values in columns.items()}
    metrics["best_epoch"] = int(float(rows[best_index][header["epoch"]]))
    metrics["epochs_completed"] = len(rows)
    metrics["fitness"] = scores[best_index]
    return metrics


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--results", type=Path, required=True)
    p.add_argument("--variant", required=True)
    p.add_argument("--train-seconds", type=int, required=True)
    p.add_argument("--exit-code", type=int, required=True)
    args = p.parse_args()

    record = {
        "variant": args.variant,
        "train_seconds": args.train_seconds,
        "exit_code": args.exit_code,
        "metrics": read_metrics(args.results),
    }

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(record, indent=2) + "\n")
    print(json.dumps(record, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
