"""The run-level record: what one training run is, and how far it has got.

`write_run_json.py` describes a single variant, which is what the thesis cites. This describes the
run those variants belong to. The folder in object storage is the only place a run lives, so that
folder has to say for itself what it was - which commit, which dataset, which parameters, which
variants, and whether it is still going.

    write_manifest.py --artifacts DIR --init --run-id … --variants … --count … (once, at the start)
    write_manifest.py --artifacts DIR --state running --variant c --epoch 7    (as often as useful)
    write_manifest.py --artifacts DIR --finish                                  (once, at the end)

**One document, written once and then updated in place.** The configuration of a run does not
change while it runs, so passing it in again at every update would be thirteen arguments repeated at
every call site - and the moment that is inconvenient, a second smaller file appears beside it and
two things have to be read to know one thing. Updating in place means the epoch heartbeat and
the between-variant checkpoints write the same file, from the shell and from inside the training
loop alike.

`state` is what makes a partially uploaded folder readable rather than ambiguous: "running" with an
`updated_at` that stopped moving is a run that died, and "failed" means a variant did not leave the
results of a complete training, or the run stopped early - `variants` says which. It is also the
heartbeat the pod watchdog reads.

Runs inside the pod, where only the standard library is available.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import platform
import sys
import time
import urllib.parse
from pathlib import Path

# Kept in the variant's record when it finished and its process still exited non-zero afterwards.
ABORTED_AFTER_COMPLETION = "aborted after completion"


def now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def headline(metrics: dict) -> str | None:
    for key, label in (("mAP50", "mAP@50"), ("top1", "top-1")):
        if isinstance(metrics.get(key), (int, float)):
            return f"{label} {metrics[key]:.3f}"
    return None


def complete(artifacts: Path, variant: str, record: dict, epochs: int | None) -> bool:
    """Whether a variant left what a finished training leaves - whatever its process exited with.

    The exit code is not that test. A variant can train every epoch, write best.pt, run its final
    validation and then abort while Python shuts down (exit 134, "terminate called without an active
    exception") - with nothing missing. So a variant counts as finished when three things are there:

    - `weights/best.pt`, the weights a release ships;
    - metrics in run.json that could be ranked, so the numbers cited belong to those weights;
    - every configured epoch in those metrics. best.pt and run.json alone prove little: Ultralytics
      writes best.pt after the first epoch that improves, and the run script writes run.json after a
      crash as well, so a variant that died in its twelfth epoch has both.

    train.py sets no `patience`, so Ultralytics' default of 100 epochs never ends a run early. A run
    that did stop early on purpose would count as failed here - the loud direction.
    """
    metrics = record.get("metrics") or {}
    fitness = metrics.get("fitness")
    epochs_completed = metrics.get("epochs_completed")
    return ((artifacts / variant / "weights" / "best.pt").is_file()
            and isinstance(fitness, (int, float)) and math.isfinite(fitness)
            and isinstance(epochs, int) and isinstance(epochs_completed, int)
            and epochs_completed >= epochs)


def variant_record(artifacts: Path, variant: str, epochs: int | None) -> dict:
    """What is known about one variant, taken from the run.json the run script already writes.

    Deliberately read afresh on every update rather than carried along: two places reporting the
    same metric is two places that can disagree, and the one that is cited must win.
    """
    path = artifacts / variant / "run.json"
    if not path.exists():
        return {"state": "pending"}
    try:
        record = json.loads(path.read_text())
    except (OSError, json.JSONDecodeError) as error:
        return {"state": "unreadable", "note": str(error)}

    exit_code = record.get("exit_code")
    metrics = record.get("metrics", {})
    finished = complete(artifacts, variant, record, epochs)
    entry = {"state": "finished" if finished else "failed", "exit_code": exit_code}
    if finished and exit_code != 0:
        # Said, but not held against the variant: everything it was trained for is there.
        entry["note"] = ABORTED_AFTER_COMPLETION
    entry.update({
        "train_seconds": record.get("train_seconds"),
        # Which number says whether a variant is working depends on its head: a detector is judged
        # by mAP, a classifier by top-1 accuracy. That is the subject matter of the project, so it
        # is decided here, once, where the record is written - and every reader of the manifest gets
        # it without deciding again.
        "headline": headline(metrics),
        "metrics": metrics,
    })
    return entry


def verdict(variants: dict) -> str:
    """The state a run ends in: finished when every one of its variants is, by the rule above."""
    return "finished" if variants and all(v["state"] == "finished" for v in variants.values()) else "failed"


def machine() -> dict:
    return {
        "host": platform.node(),
        # RunPod passes this url-encoded, so it arrives as "NVIDIA+A100+80GB+PCIe". It is read by
        # people and quoted in the thesis, so it is decoded here rather than everywhere it is shown.
        "gpu": urllib.parse.unquote_plus(
            os.environ.get("RUNPOD_GPU_NAME") or os.environ.get("GPU_NAME") or "unknown"),
        "pod_id": os.environ.get("RUNPOD_POD_ID", "unknown"),
        "github_run_id": os.environ.get("GITHUB_RUN_ID", ""),
        # The pod's own ceiling, recorded so the watchdog can judge a run by the limit it was given
        # rather than by one global number that has to fit every run ever started.
        "max_run_seconds": int(os.environ.get("MAX_RUN_SECONDS", "0")) or None,
    }


def create(args) -> dict:
    return {
        "run_id": args.run_id,
        "state": "starting",
        "started_at": now(),
        "git_sha": args.git_sha,
        "dataset": {
            "count": args.count,
            "seed": args.seed,
            # The dataset is not stored anywhere: seed plus commit reproduce it bit for bit, which
            # is why there is no upload of forty gigabytes of images next to this file.
            "reproduce": f"src/scripts/run_training.sh --count {args.count} --seed {args.seed}",
            "generate_seconds": 0,
        },
        "config": {
            "variants": [v for v in args.variants.replace(",", " ").split() if v],
            "model_size": args.size,
            "epochs": args.epochs,
            "imgsz": args.imgsz,
            "batch": args.batch,
        },
    }


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--artifacts", type=Path, required=True,
                   help="the run folder; manifest.json and <variant>/run.json live here")

    first = p.add_argument_group("--init: the parameters of the run, given once")
    first.add_argument("--init", action="store_true")
    first.add_argument("--run-id")
    first.add_argument("--variants", help="space or comma separated, in training order")
    first.add_argument("--size")
    first.add_argument("--count", type=int)
    first.add_argument("--seed", type=int)
    first.add_argument("--epochs", type=int)
    first.add_argument("--imgsz", type=int)
    first.add_argument("--batch", type=int)
    first.add_argument("--git-sha")

    later = p.add_argument_group("updates: what changed since the last write")
    later.add_argument("--state", choices=["starting", "generating", "running", "finished", "failed"])
    later.add_argument("--finish", action="store_true",
                       help="the run is over: its state follows from its variants, and is printed "
                            "together with the number of variants that did not finish")
    later.add_argument("--variant", help="the variant being trained right now")
    later.add_argument("--epoch", type=int, help="progress inside that variant")
    later.add_argument("--fitness", type=float)
    later.add_argument("--generate-seconds", type=int)
    args = p.parse_args()

    if args.finish and args.state:
        p.error("--finish decides the state itself - leave out --state")

    path = args.artifacts / "manifest.json"
    if args.init:
        missing = [n for n in ("run_id", "variants", "size", "count", "seed",
                               "epochs", "imgsz", "batch", "git_sha") if getattr(args, n) is None]
        if missing:
            p.error("--init needs " + ", ".join("--" + n.replace("_", "-") for n in missing))
        record = create(args)
    elif path.exists():
        record = json.loads(path.read_text())
    else:
        p.error(f"no manifest at {path} - run with --init first")

    variants = {v: variant_record(args.artifacts, v, record["config"]["epochs"])
                for v in record["config"]["variants"]}
    if args.finish:
        record["state"] = verdict(variants)
    elif args.state:
        record["state"] = args.state
    if args.generate_seconds is not None:
        record["dataset"]["generate_seconds"] = args.generate_seconds
    record["current_variant"] = args.variant
    record["progress"] = None if args.epoch is None else {
        "epoch": args.epoch,
        "epochs": record["config"]["epochs"],
        "fitness": args.fitness,
    }
    record["updated_at"] = now()
    record["finished_at"] = record["updated_at"] if record["state"] in ("finished", "failed") else None
    record["machine"] = machine()
    record["variants"] = variants

    # Written beside the target and moved into place. The epoch heartbeat and the checkpoints write
    # this file from different processes, and a reader that caught it half-written would see a run
    # with no state at all - which is precisely what the watchdog is not allowed to misread.
    path.parent.mkdir(parents=True, exist_ok=True)
    staging = path.with_suffix(path.suffix + ".tmp")
    staging.write_text(json.dumps(record, indent=2) + "\n")
    os.replace(staging, path)

    if args.finish:
        print(record["state"], sum(1 for v in variants.values() if v["state"] != "finished"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
