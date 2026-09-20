"""Runs the two stages of variant B so the Dataset Tool can propose a label.

Labelling real photos by hand is four clicks and a class per photo. B₁ already finds an oriented
box - which is exactly the four corners a label consists of - and B₂ names the card on a rectified
crop, so the tool can place a proposal and leave the person with checking it instead of clicking it.

This script is deliberately thin: it loads two sets of weights and answers two questions.

    locate    what oriented boxes does B₁ see in this image, strongest first
    classify  how does B₂ score the 72 classes for this crop

Everything else - which box to take, how to order its corners, which way up the card is, whether a
confidence is good enough to show, which deck is being labelled - is the tool's decision and lives in
`src/tools/dataset/Viewer/Predict/`. Two reasons: those rules belong next to the person they serve,
and a rule that lives in one language can be checked by CI in that language.

The crops are rectified by the tool as well (`CardCrop.Rectify`), the same code that produced B₂'s
training crops. A second rectification here would be a second truth about what a card looks like.

    src/training/.venv/bin/python src/training/propose.py --serve \\
        --b1 output/runs/<run>/b1/weights/best.pt --b2 output/runs/<run>/b2/weights/best.pt

    # one shot, to see whether the weights answer at all
    src/training/.venv/bin/python src/training/propose.py --op locate --image photo.jpg --b1 … --b2 …

**Serve mode** speaks one JSON object per line, request on stdin and answer on stdout:

    {"op": "locate",   "image": "/tmp/…/square.jpg"}
    {"boxes": [{"corners": [[x, y], …], "confidence": 0.83}, …]}
    {"op": "classify", "image": "/tmp/…/crop.png"}
    {"classes": [{"name": "bells_8", "confidence": 0.97}, …]}
    {"error": "…"}                      # anything that went wrong, the process stays up

The first line it ever writes is `{"ready": true}`, once both models are loaded - that is the signal
the tool waits for. Ultralytics and torch write to stdout as they please, so stdout is swapped for
stderr before either is imported and the answers go to the real one. A stray progress line in the
middle of the protocol would otherwise look like a malformed answer.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]

# The image size both stages are trained at (the default of src/scripts/run_training.sh --imgsz).
# Predicting at another size would not fail, it would quietly measure something else.
IMGSZ = 640

# Boxes below this are never worth looking at: the tool's own gate is far higher (0.50), and this
# only keeps the answer from carrying dozens of near-zero boxes. It is a floor, not a policy.
MIN_BOX_CONFIDENCE = 0.10


def locate(model, image: str, device: str) -> dict:
    """The oriented boxes B₁ sees, strongest first, in the pixels of the image handed in."""
    result = model.predict(image, imgsz=IMGSZ, conf=MIN_BOX_CONFIDENCE, device=device, verbose=False)[0]
    obb = result.obb
    if obb is None or len(obb) == 0:
        return {"boxes": []}

    corners = obb.xyxyxyxy.cpu().numpy()   # (n, 4, 2), in order around the box, start unspecified
    scores = obb.conf.cpu().numpy()
    return {"boxes": [
        {"corners": [[float(x), float(y)] for x, y in corners[i]], "confidence": float(scores[i])}
        for i in reversed(scores.argsort())
    ]}


def classify(model, image: str, device: str) -> dict:
    """All 72 classes with B₂'s confidence, strongest first.

    All of them, not the top few: the tool picks the best card of the deck being labelled, and that
    one can sit anywhere in the list when the model is drawn to the other deck.
    """
    result = model.predict(image, imgsz=IMGSZ, device=device, verbose=False)[0]
    scores = result.probs.data.cpu().numpy()
    names = result.names
    return {"classes": [
        {"name": names[i], "confidence": float(scores[i])} for i in reversed(scores.argsort())
    ]}


def answer(models: dict, request: dict, device: str) -> dict:
    op = request.get("op")
    image = request.get("image")
    if op not in ("locate", "classify"):
        return {"error": f"unknown op {op!r}"}
    if not image or not Path(image).is_file():
        return {"error": f"no image at {image!r}"}
    if op == "locate":
        return locate(models["b1"], image, device)
    return classify(models["b2"], image, device)


def serve(models: dict, device: str, out) -> int:
    """One request per line until stdin closes - which is how the tool says it is done."""
    print(json.dumps({"ready": True}), file=out, flush=True)
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            result = answer(models, json.loads(line), device)
        except Exception as error:  # noqa: BLE001 - a bad request must not end the process
            result = {"error": f"{type(error).__name__}: {error}"}
        print(json.dumps(result), file=out, flush=True)
    return 0


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--b1", type=Path, required=True, help="weights of B₁ (locate, oriented box)")
    p.add_argument("--b2", type=Path, required=True, help="weights of B₂ (classify the crop)")
    p.add_argument("--serve", action="store_true", help="answer JSON requests on stdin")
    p.add_argument("--op", choices=["locate", "classify"], help="one shot: what to ask")
    p.add_argument("--image", type=Path, help="one shot: which image to ask about")
    # CPU by default: one image at a time is fast enough once the models are warm, and the tool's
    # proposal must not be the place where a device quirk shows up for the first time.
    p.add_argument("--device", default="cpu", help="cpu | mps | 0 - see src/training/train.py")
    args = p.parse_args()

    for weights in (args.b1, args.b2):
        if not weights.is_file():
            print(f"propose: no weights at {weights}", file=sys.stderr)
            return 2

    # Before ultralytics is imported: its logger binds to whatever sys.stdout is at import time, and
    # the protocol owns the real stdout.
    out = sys.stdout
    sys.stdout = sys.stderr

    from ultralytics import YOLO

    models = {"b1": YOLO(str(args.b1)), "b2": YOLO(str(args.b2))}

    if args.serve:
        return serve(models, args.device, out)

    if not args.op or not args.image:
        print("propose: either --serve, or --op and --image", file=sys.stderr)
        return 2
    print(json.dumps(answer(models, {"op": args.op, "image": str(args.image)}, args.device)), file=out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
