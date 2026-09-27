"""Renders a dataset of 3D counting frames, in the layout the 2D generator writes.

    python3 src/tools/render3d/make_dataset.py --count 3000 --out output/dataset3d

Each sequence is one card laid onto a pile, drawn from the seed: the deck (half the sequences French,
half German, like the 2D generator's run), how many cards lie on the pile, table, light, whether a hand
lays the card, and how the camera moves. A few frames of each sequence go into the dataset, chosen from
the seed as well; frames in the ambiguous band are neither rendered nor written. The result is
`images/`, `detect/labels/`, `locate/labels/` and `classes.txt`, plus `render3d.jsonl` with what every
frame was and how long it took; `rebuild` then derives the rest, exactly as for a generated dataset, so
`src/training/train.py --train-data output/dataset3d` trains on it unchanged.

Same seed and count, same scenes - but not the same pixels on another machine: CPU and GPU render
differently, which the experiment allows (see "3D counting sequences" in
context/architecture/data-pipeline.md).

Standard library only. Needs Blender 4.2 or newer: `--blender`, `$BLENDER`, the macOS app or `blender`
on the path.
"""
from __future__ import annotations

import argparse
import json
import os
import random
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import scene as S  # noqa: E402

BATCH = 20   # sequences per Blender process: one start-up, a fresh scene for each


def find_blender(explicit: str | None) -> str:
    for candidate in (explicit, os.environ.get("BLENDER"), "/Applications/Blender.app/Contents/MacOS/Blender",
                      shutil.which("blender")):
        if candidate and Path(candidate).exists():
            return candidate
    raise SystemExit("Blender not found: pass --blender, set $BLENDER, or put blender on the path")


def sequence_params(seed: int, index: int, frames_per_sequence: int) -> dict:
    rng = random.Random(seed * 1_000_003 + index)
    params = S.Params(
        deck="french" if index % 2 == 0 else "german",
        played=rng.randint(0, 34),
        laid=1,
        table="random",
        light="random",
        hand=rng.random() < 0.9,
        camera=rng.choices(S.CAMERA_PATHS, weights=[0.7, 0.15, 0.15])[0],
        seed=rng.randrange(1 << 30),
    )
    total = S.resolve(params)["frames"]
    frames = sorted(rng.sample(range(total), min(frames_per_sequence, total)))
    return {"params": params.__dict__, "frames": frames, "stem": f"s{index:06d}"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--count", type=int, required=True, help="images to write")
    parser.add_argument("--out", type=Path, default=ROOT / "output/dataset3d")
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--frames-per-sequence", type=int, default=10)
    parser.add_argument("--engine", choices=["cycles", "eevee"], default="cycles")
    parser.add_argument("--samples", type=int, default=32)
    parser.add_argument("--blender", default=None)
    parser.add_argument("--keep", action="store_true", help="add to an existing output folder instead of replacing it")
    args = parser.parse_args()

    blender = find_blender(args.blender)
    if args.out.exists() and not args.keep:
        shutil.rmtree(args.out)
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "classes.txt").write_text("".join(f"{s}_{r}\n" for s in S.SUITS for r in S.RANKS))

    started = time.perf_counter()
    index = 0
    written = 0
    while written < args.count:
        # About three quarters of the chosen frames make it in (the rest are ambiguous and skipped), so
        # the last batch is sized to what is still missing rather than rendering a full one for nothing.
        missing = args.count - written
        batch = max(1, min(BATCH, round(missing / (0.75 * args.frames_per_sequence)) + 1))
        sequences = [sequence_params(args.seed, index + k, args.frames_per_sequence) for k in range(batch)]
        index += batch
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as handle:
            json.dump({"cards_root": str(ROOT / "data/cards"), "output": str(args.out), "mode": "dataset",
                       "engine": args.engine, "samples": args.samples, "sequences": sequences}, handle)
        result = subprocess.run([blender, "--background", "--factory-startup", "--python",
                                 str(Path(__file__).resolve().parent / "render_table.py"), "--", handle.name],
                                capture_output=True, text=True)
        os.unlink(handle.name)
        if result.returncode != 0:
            print(result.stdout[-3000:], result.stderr[-3000:], sep="\n", file=sys.stderr)
            raise SystemExit(f"Blender failed with exit code {result.returncode}")
        written = len(list((args.out / "images").glob("*.jpg")))
        rate = written / (time.perf_counter() - started)
        print(f"make_dataset: {written}/{args.count} images, {rate:.2f} images/s", flush=True)

    # One batch overshoots: the last images go, so the dataset holds exactly the count asked for.
    for image in sorted((args.out / "images").glob("*.jpg"))[args.count:]:
        for label in (args.out / "detect/labels" / f"{image.stem}.txt", args.out / "locate/labels" / f"{image.stem}.txt"):
            label.unlink(missing_ok=True)
        image.unlink()

    summary = summarise(args.out)
    summary["wall_seconds"] = round(time.perf_counter() - started, 1)
    # Everything rendered, including what the overshoot removed: the rate of the renderer and its start-ups.
    summary["rendered_per_second"] = round(summary["rendered"] / summary["wall_seconds"], 3)
    (args.out / "render3d-summary.json").write_text(json.dumps(summary, indent=1))
    print(json.dumps(summary, indent=1))

    if shutil.which("dotnet"):
        subprocess.run(["dotnet", "run", "-c", "Release", "--project", str(ROOT / "src/tools/dataset/Cli"), "--",
                        "rebuild", "--dataset", str(args.out)], check=True)
    else:
        print("make_dataset: dotnet not found - run the CLI's rebuild before training", file=sys.stderr)
    return 0


def summarise(out: Path) -> dict:
    """What the dataset holds and how fast it rendered, from render3d.jsonl and the files themselves."""
    kinds: dict[str, int] = {}
    reasons: dict[str, int] = {}
    seconds: list[float] = []
    devices = set()
    for line in (out / "render3d.jsonl").read_text().splitlines():
        record = json.loads(line)
        devices.add(record["device"])
        seconds.extend(record["render_seconds"][1:])        # the first frame of a process compiles kernels
        for frame in record["frames"]:
            kinds[frame["kind"]] = kinds.get(frame["kind"], 0) + 1
            reasons[frame["reason"]] = reasons.get(frame["reason"], 0) + 1
    images = sorted((out / "images").glob("*.jpg"))
    positives = sum(1 for i in images if (out / "detect/labels" / f"{i.stem}.txt").exists())
    return {
        "rendered": len(seconds) + sum(1 for line in (out / "render3d.jsonl").read_text().splitlines()),
        "images": len(images),
        "positives": positives,
        "negatives": len(images) - positives,
        "frames_labelled": kinds,
        "reasons": reasons,
        "devices": sorted(devices),
        "render_seconds_per_frame": round(sum(seconds) / max(1, len(seconds)), 4),
        "render_frames_per_second": round(len(seconds) / max(1e-9, sum(seconds)), 3),
    }


if __name__ == "__main__":
    raise SystemExit(main())
