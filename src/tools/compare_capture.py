"""Runs one capture through the iOS model (Core ML) or the Android model (LiteRT) and prints what it saw.

The same capture file has to give the same detection in both apps. The captures carry the
analysed square itself (see "Capture a situation" in src/app/ios/README.md), so a file saved on either phone
is exactly what that app's model was given, and its name says what the app made of it. This script
feeds the file to either model the way the apps do - the whole square scaled to 640 - and prints the top
results, so the two can be set side by side. Each backend needs its own export environment, which is
why it is two runs rather than one:

    src/training/.venv-export/bin/python src/tools/compare_capture.py --backend coreml capture_….jpg
    src/training/.venv-export-android/bin/python src/tools/compare_capture.py --backend litert capture_….jpg

How each output is read:

  detector (c)      Core ML: the exported pipeline's own NMS, with no thresholds passed - the iOS app
                    never overrides them either, so its defaults are what the app gets (per-class, IoU
                    0.7, confidence 0.25). LiteRT: the raw tensor decoded to those same rules, as the
                    Android app does.
  locator (b1)      the single most confident oriented box and its angle, as both apps' locators take
                    it (argmax over confidence, floor 0.25) instead of running NMS. Core ML's export of
                    it is raw in both apps too; its box is in 640-pixel space and is normalised here.
  classifier (a, b2) the top classes. b2 is fed the whole square, not stage one's crop.

The layout is recognised from the output's shape against the number of labels, not from the variant
name, so a renamed or retrained model is read correctly or refused with a clear message.
Core ML runs only on macOS.
"""

from __future__ import annotations

import argparse
import ast
from pathlib import Path
from typing import NamedTuple

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
IOS_MODELS = REPO / "src" / "app" / "ios" / "Models"
ANDROID_MODELS = REPO / "src" / "app" / "android" / "app" / "src" / "main" / "assets" / "models"
SIZE = 640

# The detector's suppression. These mirror the defaults of the NMS stage in the exported Core ML pipeline
# (its spec: pickTop.perClass = true, iouThreshold 0.7, confidenceThreshold 0.25), which the iOS app never
# overrides; the Android app decodes the LiteRT tensor with the same numbers. Core ML is not given them -
# they are named here only so the LiteRT side can do by hand what that stage does.
CANDIDATE_FLOOR = 0.25
IOU_THRESHOLD = 0.7
MAX_DETECTIONS = 20
# The locator's floor, the same in iOS's CardLocator and Android's TwoStageRecognizer.
LOCATE_FLOOR = 0.25
# How many results are printed; enough to see the runner-up, which is where two backends would part ways.
SHOW = 3


class Result(NamedTuple):
    label: str
    score: float
    box: list[float] | None = None      # cx, cy, w, h normalised to the square
    angle: float | None = None          # radians, oriented boxes only


def square(path: Path) -> Image.Image:
    return Image.open(path).convert("RGB").resize((SIZE, SIZE), Image.BILINEAR)


def label(names: list[str], index: int) -> str:
    # Core ML's NMS stage pads its class columns (to 80 for Xcode's preview), so an index can lie past the
    # label list; it never wins on a real score, but it must not crash the report either.
    return names[index] if index < len(names) else f"class{index}"


def rounded(box) -> list[float]:
    return [round(float(v), 3) for v in box]


def overlap(a: np.ndarray, b: np.ndarray) -> float:
    """IoU of two centre-size boxes."""
    ax0, ay0, ax1, ay1 = a[0] - a[2] / 2, a[1] - a[3] / 2, a[0] + a[2] / 2, a[1] + a[3] / 2
    bx0, by0, bx1, by1 = b[0] - b[2] / 2, b[1] - b[3] / 2, b[0] + b[2] / 2, b[1] + b[3] / 2
    inter = max(0.0, min(ax1, bx1) - max(ax0, bx0)) * max(0.0, min(ay1, by1) - max(ay0, by0))
    return inter / (a[2] * a[3] + b[2] * b[3] - inter + 1e-9)


def classify(probabilities: np.ndarray, names: list[str]) -> list[Result]:
    return [Result(label(names, int(i)), float(probabilities[i])) for i in np.argsort(-probabilities)]


def decode(out: np.ndarray, names: list[str], scale: float) -> list[Result]:
    """Reads a raw [channels, anchors] box tensor, whichever of the two box layouts it has.

    `scale` brings the box into the normalised square: LiteRT reports it normalised already (1), Core ML's
    raw export in the 640-pixel model space (1 / 640).
    """
    channels, classes = out.shape[0], len(names)
    if channels == 4 + classes:
        # Detector: cx, cy, w, h, then one score per class.
        boxes, scores = out[:4].T * scale, out[4:].T
        best, confidence = scores.argmax(1), scores.max(1)
        kept: list[int] = []
        for i in np.argsort(-confidence):
            if confidence[i] <= CANDIDATE_FLOOR or len(kept) == MAX_DETECTIONS:
                break
            # Per class, like Core ML's stage: a box only suppresses boxes of its own class.
            if all(best[k] != best[i] or overlap(boxes[i], boxes[k]) <= IOU_THRESHOLD for k in kept):
                kept.append(int(i))
        return [Result(label(names, int(best[k])), float(confidence[k]), rounded(boxes[k])) for k in kept]
    if channels == 4 + classes + 1:
        # Oriented locator: cx, cy, w, h, the class scores, and the angle last. The angle is not a score,
        # which is what reading every row after the box as one would get wrong.
        scores, angle = out[4:-1], out[-1]
        confidence = scores.max(0)
        i = int(confidence.argmax())
        if confidence[i] <= LOCATE_FLOOR:
            return []
        return [Result(label(names, int(scores[:, i].argmax())), float(confidence[i]),
                       rounded(out[:4, i] * scale), float(angle[i]))]
    raise SystemExit(f"Unrecognised output: {channels} channels for {classes} labels - expected "
                     f"{4 + classes} (detector) or {4 + classes + 1} (oriented locator).")


def coreml(image: Image.Image, variant: str) -> list[Result]:
    import coremltools as ct

    # CPU only: on a Mac the GPU path of coremltools aborts on the classifiers (an MPSGraph assertion),
    # and the numbers of interest are the model's, not the accelerator's.
    model = ct.models.MLModel(str(IOS_MODELS / f"JassCardEye-{variant}.mlpackage"),
                              compute_units=ct.ComputeUnit.CPU_ONLY)
    # Only the image. The detector pipeline also has iouThreshold/confidenceThreshold inputs; leaving them
    # out makes it use its own defaults, exactly as the iOS app (via Vision) does.
    inputs = model.get_spec().description.input
    out = model.predict({spec.name: image for spec in inputs if spec.type.WhichOneof("Type") == "imageType"})
    table = ast.literal_eval(model.user_defined_metadata.get("names", "{}"))
    names = [table[i] for i in range(len(table))]

    if "confidence" in out and "coordinates" in out:     # the NMS stage's finished boxes, normalised
        rows = sorted(zip(np.array(out["confidence"]), np.array(out["coordinates"])), key=lambda r: -r[0].max())
        return [Result(label(names, int(c.argmax())), float(c.max()), rounded(xy)) for c, xy in rows]
    probabilities = next((v for v in out.values() if isinstance(v, dict)), None)
    if probabilities is not None:                         # a classifier: label -> probability
        return [Result(k, float(p)) for k, p in sorted(probabilities.items(), key=lambda kv: -kv[1])]
    raw = np.array(next(iter(out.values())))              # a raw box tensor, [1, channels, anchors]
    return decode(raw[0], names, 1.0 / SIZE)


def litert(image: Image.Image, variant: str) -> list[Result]:
    from ai_edge_litert.interpreter import Interpreter

    names = (ANDROID_MODELS / f"JassCardEye-{variant}.labels.txt").read_text().splitlines()
    interpreter = Interpreter(model_path=str(ANDROID_MODELS / f"JassCardEye-{variant}.tflite"))
    interpreter.allocate_tensors()
    source, target = interpreter.get_input_details()[0], interpreter.get_output_details()[0]
    pixels = np.asarray(image, dtype=np.float32)[None] / 255.0
    if source["shape"][1] == 3:
        pixels = pixels.transpose(0, 3, 1, 2)          # the export is NCHW, like the PyTorch model
    interpreter.set_tensor(source["index"], pixels)
    interpreter.invoke()
    out = interpreter.get_tensor(target["index"])[0]
    if out.ndim == 1:                                  # a classifier: one probability per class
        return classify(out, names)
    return decode(out, names, 1.0)


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("capture", type=Path)
    p.add_argument("--backend", choices=["coreml", "litert"], required=True)
    p.add_argument("--variant", default="c")
    args = p.parse_args()

    run = coreml if args.backend == "coreml" else litert
    results = run(square(args.capture), args.variant)
    if not results:
        print(f"{args.backend:7} nothing above the floor")
    for r in results[:SHOW]:
        print(f"{args.backend:7} {r.label:14} {r.score:.3f}"
              + (f"  cx,cy,w,h={r.box}" if r.box else "")
              + (f"  angle={r.angle:.3f} rad" if r.angle is not None else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
