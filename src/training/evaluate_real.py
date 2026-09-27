"""Counts what a detector would do on the real validation set, frame by frame, the way the apps decide.

    src/training/.venv/bin/python src/training/evaluate_real.py output/training/c/weights/best.pt
    python evaluate_real.py src/app/android/app/src/main/assets/models/JassCardEye-c.tflite   # needs ai-edge-litert

A `.pt` is read by Ultralytics; a `.tflite` - the model the Android app ships - through LiteRT and the
Android app's own decoding (`decode` in src/tools/compare_capture.py), with its labels file beside it.

Ultralytics' validation reports mAP - how well boxes and classes rank - but the question for a counting
app is simpler and harsher: on a frame, does the best detection above the apps' threshold name the right
card, the wrong one, or nothing? A wrong card and a card on a frame without one are the false alarms that
put a wrong number on the slate, which is why every change is judged by them first
(context/architecture/recognition.md); a miss only costs a frame, because the stability rule waits for
the next.

Every frame of data/real/val is asked once, as the apps ask: the whole square at 640, the best-scoring
detection of the deck in play (the deck is in the sample's name; card-free photos have none, so any card
counts against them), kept when its score reaches the threshold. Single frames only - the stability rule,
which needs several frames to agree, is not applied, so these are the raw errors it has to catch.

Prints a table and, with --json, writes the numbers for a comparison of several models.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP_THRESHOLD = 0.6          # both apps count with this (context/architecture/recognition.md)
SUITS = {"french": {"clubs", "diamonds", "hearts", "spades"}, "german": {"acorns", "roses", "bells", "shields"}}


def deck_of(stem: str) -> str | None:
    for deck in SUITS:
        if f"_{deck}_" in stem:
            return deck
    return None


def ultralytics_detections(weights: Path, device: str | None):
    """Every detection of a .pt model per image, as (class name, score)."""
    from ultralytics import YOLO

    model = YOLO(str(weights))
    names = model.names

    def detect(batch: list[Path]) -> list[list[tuple[str, float]]]:
        results = model.predict([str(p) for p in batch], imgsz=640, conf=0.05, verbose=False, device=device)
        return [[(names[int(b.cls)], float(b.conf)) for b in r.boxes] for r in results]
    return detect


def litert_detections(weights: Path):
    """The shipped LiteRT model, read and decoded as the Android app does it."""
    import sys

    import numpy as np
    from ai_edge_litert.interpreter import Interpreter
    from PIL import Image

    sys.path.insert(0, str(ROOT / "src" / "tools"))
    from compare_capture import SIZE, decode

    names = weights.with_suffix(".labels.txt").read_text().splitlines()
    interpreter = Interpreter(model_path=str(weights))
    interpreter.allocate_tensors()
    source, target = interpreter.get_input_details()[0], interpreter.get_output_details()[0]

    def detect(batch: list[Path]) -> list[list[tuple[str, float]]]:
        found = []
        for path in batch:
            pixels = np.asarray(Image.open(path).convert("RGB").resize((SIZE, SIZE), Image.BILINEAR), dtype=np.float32)[None] / 255.0
            if source["shape"][1] == 3:
                pixels = pixels.transpose(0, 3, 1, 2)
            interpreter.set_tensor(source["index"], pixels)
            interpreter.invoke()
            found.append([(r.label, r.score) for r in decode(interpreter.get_tensor(target["index"])[0], names, 1.0)])
        return found
    return detect


def evaluate(weights: Path, dataset: Path, threshold: float, device: str | None) -> dict:
    names = (dataset / "classes.txt").read_text().split()
    detect = litert_detections(weights) if weights.suffix == ".tflite" else ultralytics_detections(weights, device)
    images = sorted((dataset / "images").glob("*.jpg"))
    counts = {"positives": 0, "correct": 0, "wrong_card": 0, "missed": 0, "negatives": 0, "abstained": 0,
              "card_on_negative": 0}
    wrong: list[dict] = []
    for start in range(0, len(images), 32):
        batch = images[start:start + 32]
        for image, detections in zip(batch, detect(batch)):
            label = dataset / "detect" / "labels" / f"{image.stem}.txt"
            truth = names[int(label.read_text().split()[0])] if label.exists() else None
            deck = deck_of(image.stem)
            best = None
            for name, score in detections:
                if deck is not None and name.split("_", 1)[0] not in SUITS[deck]:
                    continue                                   # the other deck is not a card tonight
                if best is None or score > best[1]:
                    best = (name, score)
            said = best[0] if best is not None and best[1] >= threshold else None
            if truth is None:
                counts["negatives"] += 1
                if said is None:
                    counts["abstained"] += 1
                else:
                    counts["card_on_negative"] += 1
                    wrong.append({"image": image.stem, "truth": None, "said": said, "score": round(best[1], 3)})
            else:
                counts["positives"] += 1
                if said is None:
                    counts["missed"] += 1
                elif said == truth:
                    counts["correct"] += 1
                else:
                    counts["wrong_card"] += 1
                    wrong.append({"image": image.stem, "truth": truth, "said": said, "score": round(best[1], 3)})
    frames = counts["positives"] + counts["negatives"]
    counts["false_alarms"] = counts["wrong_card"] + counts["card_on_negative"]
    counts["false_alarm_rate"] = round(counts["false_alarms"] / max(1, frames), 4)
    counts["recall"] = round(counts["correct"] / max(1, counts["positives"]), 4)
    return {"weights": str(weights), "threshold": threshold, **counts, "false_alarm_frames": wrong}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("weights", type=Path, nargs="+", help="best.pt of each model to compare")
    parser.add_argument("--data", type=Path, default=ROOT / "data/real/val")
    parser.add_argument("--threshold", type=float, default=APP_THRESHOLD)
    parser.add_argument("--device", default=None, help="cpu, mps or a CUDA device; Ultralytics' choice by default")
    parser.add_argument("--json", type=Path, default=None, help="write all numbers there")
    args = parser.parse_args()

    results = [evaluate(w, args.data, args.threshold, args.device) for w in args.weights]
    header = f"{'model':<48} {'correct':>8} {'wrong':>6} {'missed':>7} {'on neg.':>8} {'false alarms':>13} {'recall':>7}"
    print(header)
    for r in results:
        name = Path(r["weights"]).name if r["weights"].endswith(".tflite") else Path(r["weights"]).parent.parent.name
        print(f"{name:<48} {r['correct']:>8} {r['wrong_card']:>6} {r['missed']:>7} "
              f"{r['card_on_negative']:>8} {r['false_alarms']:>6} ({r['false_alarm_rate']:.1%}) {r['recall']:>7.1%}")
    if results:
        print(f"{results[0]['positives']} frames with a card, {results[0]['negatives']} without; threshold {args.threshold}")
    if args.json:
        args.json.write_text(json.dumps(results, indent=1))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
