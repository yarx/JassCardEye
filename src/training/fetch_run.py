"""Brings a training run back from Azure and turns it into models the app can use.

The runs happen in a pod and their results never touch this machine on their own. This is the
way back: list what has been trained, pull one run down, and hand its weights to the exporter.

    python src/training/fetch_run.py --list
    python src/training/fetch_run.py --run-id <run-id>
    python src/training/fetch_run.py --run-id <run-id> --variant c --export
    src/training/.venv-export-android/bin/python src/training/fetch_run.py \
        --run-id <run-id> --variant c --weights-only --export --format litert

Downloads land in `output/runs/<run id>/`, which mirrors the folder in the container exactly, so
anything that reads a run folder works on a fetched run unchanged.

Exporting goes through `src/training/export.py`, and so does the list of formats: its `FORMATS` table decides
the `--format` choices, the default model folder of each, and that `--half` is refused where a format
cannot use it (LiteRT). Only the export itself needs the export environment; `--list` runs anywhere.

**Provenance.** Exporting also writes `models.json` next to the models (`src/app/ios/Models/` for the iOS
app, `src/app/android/app/src/main/assets/models/` for the Android one): which run each bundled model came
from, at which commit, with which metrics. Without it a tester's report cannot be tied to a model,
and a number in the thesis cannot be tied to the weights that produced it - the app ships a file
whose origin nobody can name.

Authentication, in order of what it tries:

  1. `AZURE_STORAGE_SAS_TOKEN`, if it is set - the same token the pod uses.
  2. The account key, found by locating the account across the subscriptions `az` can see. This is
     the path that needs no setup: whoever owns the subscription can already read the key, while
     owning it grants no access to the *data* inside a container - a distinction that otherwise
     produces a baffling permission error on the first attempt.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "src" / "scripts"))
sys.path.insert(0, str(REPO / "src" / "training"))
import runstore  # noqa: E402
# Safe at the top, in any environment: export.py imports ultralytics (and with it coremltools or the
# LiteRT converter) only inside export_one, so --list and --help need none of that. What it gives here is
# the one table of formats, so the choices, the default folder and the --half rule cannot drift apart.
from export import DEFAULT_FORMAT, FORMATS, check_options, export_one, format_help, half_help  # noqa: E402

# The default is the project's own account, so `--list` works with no environment at all.
os.environ.setdefault("AZURE_STORAGE_ACCOUNT", "jasscardeye")
ACCOUNT = runstore.account()
CONTAINER = runstore.CONTAINER


def show(path: Path) -> str:
    """Short where it can be, absolute where it has to be - an export target may sit anywhere."""
    try:
        return str(path.relative_to(REPO))
    except ValueError:
        return str(path)


def list_runs(auth: list[str], limit: int) -> int:
    runs = runstore.run_ids(auth)[:limit]
    if not runs:
        print(f"No runs in {CONTAINER}.")
        return 0

    print(f"\n{'run':26} {'state':9} {'variants':14} {'images':>7}  best metric")
    print("-" * 78)
    for run_id in runs:
        manifest = runstore.read_json(run_id)
        if manifest is None:
            print(f"{run_id:26} {'?':9} {'-':14} {'-':>7}  (no manifest)")
            continue
        # The manifest already names the number that matters for each variant - which one that is
        # depends on the head, and that decision belongs where the record is written.
        headlines = [r.get("headline") for r in manifest.get("variants", {}).values()]
        best = next((h for h in headlines if h), "-")
        config = manifest.get("config", {})
        print(f"{run_id:26} {manifest.get('state', '?'):9} "
              f"{','.join(config.get('variants', [])):14} "
              f"{manifest.get('dataset', {}).get('count', '-'):>7}  {best}")
    print()
    return 0


def download(run_id: str, variants: list[str] | None, auth: list[str], weights_only: bool) -> Path:
    out = REPO / "output" / "runs" / run_id
    out.mkdir(parents=True, exist_ok=True)
    # One pattern per variant rather than the whole run, because a run with four variants is most of
    # a gigabyte and exporting one of them needs a single file out of it.
    patterns = []
    if variants:
        for variant in variants:
            patterns.append(f"{variant}/weights/best.pt" if weights_only else f"{variant}/*")
        patterns.append("manifest.json")
    else:
        patterns.append("*")

    for pattern in patterns:
        print(f"   fetching {pattern}")
        subprocess.run(
            ["az", "storage", "blob", "download-batch", "--account-name", ACCOUNT,
             "--source", CONTAINER, "--destination", str(out),
             "--pattern", f"{run_id}/{pattern}", "--overwrite", "--only-show-errors", *auth],
            check=True)

    # download-batch keeps the run id as a folder level; flatten it so the result mirrors what
    # artifacts/ looks like locally and every tool that reads a run folder just works.
    nested = out / run_id
    if nested.exists():
        for item in nested.iterdir():
            target = out / item.name
            if target.exists():
                shutil.rmtree(target) if target.is_dir() else target.unlink()
            item.rename(target)
        nested.rmdir()
    return out


def record_provenance(run_id: str, manifest: dict, exported: list[str], models_dir: Path) -> None:
    """Says which run every bundled model came from.

    The app can show it, and a figure in the thesis can be traced to the weights behind it. A model
    file on its own carries no such thing.
    """
    path = models_dir / "models.json"
    record = {}
    if path.exists():
        try:
            record = json.loads(path.read_text())
        except json.JSONDecodeError:
            record = {}
    record.setdefault("models", {})
    for variant in exported:
        source = manifest.get("variants", {}).get(variant, {})
        record["models"][variant] = {
            "run_id": run_id,
            "git_sha": manifest.get("git_sha"),
            "dataset_count": manifest.get("dataset", {}).get("count"),
            "dataset_seed": manifest.get("dataset", {}).get("seed"),
            "metrics": source.get("metrics", {}),
            "exported_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        }
    record["updated_at"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(record, indent=2) + "\n")
    print(f"   provenance written to {show(path)}")


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--list", action="store_true", help="show the runs in the container")
    p.add_argument("--limit", type=int, default=20, help="how many runs to list")
    p.add_argument("--run-id", help="the run to fetch")
    p.add_argument("--variant", nargs="+", help="only these variants (default: the whole run)")
    p.add_argument("--export", action="store_true",
                   help="also export into the app's model folder for --format (needs the export environment, see src/training/README.md)")
    p.add_argument("--weights-only", action="store_true",
                   help="with --variant: fetch best.pt alone, not the figures")
    p.add_argument("--format", choices=sorted(FORMATS), default=DEFAULT_FORMAT, help=format_help())
    p.add_argument("--out", type=Path, default=None,
                   help="where the model goes (default: the app's model folder for --format). Point "
                        "it elsewhere to try an export without touching what the app currently bundles")
    p.add_argument("--imgsz", type=int, default=640)
    p.add_argument("--half", action="store_true", help=half_help())
    args = p.parse_args()

    if not args.list and not args.run_id:
        p.error("nothing to do - pass --list or --run-id")
    # Before authenticating and downloading, so a combination the exporter would refuse fails at once.
    if args.export:
        check_options(args.format, args.half)

    auth = runstore.az_auth(announce=True)
    if args.list:
        return list_runs(auth, args.limit)

    manifest = runstore.read_json(args.run_id)
    if manifest is None:
        raise SystemExit(f"No manifest for {args.run_id} - is the run id right? Try --list.")
    if manifest.get("state") == "running":
        print(f"!  {args.run_id} is still running - what you get is a snapshot.")

    out = download(args.run_id, args.variant, auth, args.weights_only)
    print(f"   run in {show(out)}")

    if not args.export:
        print("\nNot exported (add --export). The weights are at "
              f"{show(out / '<variant>' / 'weights' / 'best.pt')}")
        return 0

    models_dir = args.out if args.out else FORMATS[args.format].out
    exported = []
    for variant in (args.variant or sorted(manifest.get("variants", {}))):
        weights = out / variant / "weights" / "best.pt"
        if not weights.exists():
            print(f"!  {variant}: no best.pt in this run - skipped")
            continue
        export_one(variant, weights, args.imgsz, args.half, models_dir, fmt=args.format)
        exported.append(variant)

    if exported:
        record_provenance(args.run_id, manifest, exported, models_dir)
        print("\n" + FORMATS[args.format].next_steps)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
