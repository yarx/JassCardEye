"""Export trained weights for the iOS/macOS app and for the Android app.

Two formats from the same best.pt, so both apps run the same weights:

  coreml (default)  the iOS/macOS app. One .mlpackage runs unchanged on iOS and macOS,
                    accelerated by the Neural Engine on both. For the box heads (detect,
                    obb) NMS is requested, so the app receives ready-made boxes; the
                    classify heads have no boxes to suppress, so it is left off there.
                    A model with NMS is stored in FP16 whether or not --half is given -
                    Ultralytics does that; --half asks for FP16 for the others as well.
  litert            the Android app (LiteRT, formerly TensorFlow Lite). No NMS: the app
                    decodes the raw tensor itself, the way iOS already does for the
                    oriented-box locator of variant B. The class names are written next
                    to the model as JassCardEye-<variant>.labels.txt, in index order.
                    Always FP32, so --half is refused rather than silently ignored.
                    Needs its own environment - see src/training/requirements-export-android.txt.

    python src/training/export.py --variant c
    python src/training/export.py --variant a b1 b2 c        # every variant the app can select
    python src/training/export.py --weights output/training/c/weights/best.pt --variant c
    src/training/.venv-export-android/bin/python src/training/export.py --format litert --variant c

Everything that differs between the formats - where the models go, how they are written, whether
--half means anything, what to do next - lives in the one table FORMATS below, which
src/training/fetch_run.py uses as well. A third format is one more entry there, not another branch.

Both apps select a variant at runtime and expect the models under fixed names:
C -> JassCardEye-c, A -> JassCardEye-a, and the two-stage B -> JassCardEye-b1
(locate) plus JassCardEye-b2 (classify the crop). The class names travel with
each model - Core ML metadata, the LiteRT .labels.txt - and the apps read them
from there. The detector's names come from classes.txt in class-ID order; the
classifiers (A, B2) number their classes alphabetically by folder, so for them
the names match classes.txt and the indices do not.
"""

from __future__ import annotations

import argparse
import shutil
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

# Nothing heavy is imported at module level: ultralytics (and through it coremltools or the LiteRT
# converter) is imported inside export_one, so fetch_run.py can read FORMATS in any environment.

REPO = Path(__file__).resolve().parents[2]

# Which head each app-facing variant carries, and therefore whether Core ML gets NMS baked in. Only
# Core ML asks: the LiteRT export never carries NMS. Ultralytics itself still drops it for obb, which
# is why iOS decodes B1's raw tensor too.
TASK_NEEDS_NMS = {"detect": True, "obb": True, "classify": False, "segment": True, "pose": True}


def export_coreml(model, variant: str, imgsz: int, half: bool, out_dir: Path) -> Path:
    # NMS depends on the head, not on a flag we pass by hand - a classifier has no boxes to suppress.
    nms = TASK_NEEDS_NMS.get(model.task, False)
    exported = Path(model.export(format="coreml", imgsz=imgsz, nms=nms, half=half))

    target = out_dir / f"JassCardEye-{variant}.mlpackage"
    if target.exists():
        shutil.rmtree(target)
    shutil.move(str(exported), target)
    print(f"exported {model.task:8} -> {target.name}")
    return target


def export_litert(model, variant: str, imgsz: int, half: bool, out_dir: Path) -> Path:
    # FP32 and no NMS. LiteRT runs an FP32 model in FP16 on the GPU by itself, and the app reads one
    # top card, so suppression is its own few lines rather than a graph the converter has to carry.
    # The output tensors are documented where they are decoded, in the app's CardRecognizer.kt. `half`
    # is never true here: the table says this format does not support it, and export_one refuses it.
    exported = Path(model.export(format="litert", imgsz=imgsz, nms=False))
    target = out_dir / f"JassCardEye-{variant}.tflite"
    shutil.move(str(exported), target)
    # The class names in class-ID order, one per line. The .tflite carries them too, appended as a
    # zip entry, but a plain file next to it is what the app reads and what a person can check.
    names = model.names
    labels = out_dir / f"JassCardEye-{variant}.labels.txt"
    labels.write_text("".join(f"{names[i]}\n" for i in range(len(names))))
    print(f"exported {model.task:8} -> {target.name} (+ {labels.name}, {len(names)} classes)")
    return target


@dataclass(frozen=True)
class Format:
    """Everything that makes one export format different from another."""

    app: str                 # who consumes it, for --help
    out: Path                # the folder that app bundles - where models go when no --out is given
    export: Callable[..., Path]   # (model, variant, imgsz, half, out_dir) -> the written model
    half: bool               # whether --half means anything; a format without it refuses the flag
    next_steps: str          # printed once every variant is exported


FORMATS = {
    "coreml": Format(
        app="the iOS/macOS app",
        out=REPO / "src" / "app" / "ios" / "Models",
        export=export_coreml,
        half=True,
        next_steps="The models are in the iOS app's Models folder; run `xcodegen generate` in "
                   "src/app/ios so the project lists them, and Xcode compiles them into the app "
                   "bundle. The app shows a model picker as soon as more than one variant is bundled."),
    "litert": Format(
        app="the Android app",
        out=REPO / "src" / "app" / "android" / "app" / "src" / "main" / "assets" / "models",
        export=export_litert,
        half=False,
        next_steps="The models are in the Android app's assets; ./gradlew assembleDebug bundles them. "
                   "The app shows a model picker as soon as more than one variant is bundled."),
}
DEFAULT_FORMAT = "coreml"


def check_options(fmt: str, half: bool) -> Format:
    """The format's entry, or a clear exit when the options ask for something it cannot do.

    Called before anything is copied, downloaded or imported, so a wrong combination costs nothing.
    """
    entry = FORMATS[fmt]
    if half and not entry.half:
        supported = ", ".join(name for name, f in sorted(FORMATS.items()) if f.half)
        raise SystemExit(f"--half is not supported with --format {fmt} (only with: {supported}). "
                         "Leave it off; that format is exported in full precision.")
    return entry


def format_help() -> str:
    return "; ".join(f"{name} for {f.app}" + (" (default)" if name == DEFAULT_FORMAT else "")
                     for name, f in sorted(FORMATS.items()))


def out_help() -> str:
    return ", ".join(f"{f.out.relative_to(REPO)} for {name}" for name, f in sorted(FORMATS.items()))


def half_help() -> str:
    return ("fp16 weights (half the size, minimal accuracy cost); only with --format "
            + " or ".join(name for name, f in sorted(FORMATS.items()) if f.half))


def export_one(variant: str, weights: Path, imgsz: int, half: bool, out_dir: Path,
               fmt: str = DEFAULT_FORMAT) -> Path:
    entry = check_options(fmt, half)
    if not weights.exists():
        raise SystemExit(f"Weights not found: {weights} - train first (src/training/train.py)")

    # Work on a copy: best.pt may be rewritten mid-export by a still-running training.
    stage = REPO / "output" / "export"
    stage.mkdir(parents=True, exist_ok=True)
    staged = stage / f"{variant}-{weights.name}"
    shutil.copy2(weights, staged)

    from ultralytics import YOLO

    model = YOLO(str(staged))
    out_dir.mkdir(parents=True, exist_ok=True)
    return entry.export(model, variant, imgsz, half, out_dir)


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--variant", nargs="+", default=["c"],
                   help="one or more run names under output/training (default: c)")
    p.add_argument("--weights", default=None,
                   help="explicit .pt path; only valid with a single --variant, overrides its default path")
    p.add_argument("--imgsz", type=int, default=640)
    p.add_argument("--half", action="store_true", help=half_help())
    p.add_argument("--format", choices=sorted(FORMATS), default=DEFAULT_FORMAT, help=format_help())
    p.add_argument("--out", default=None,
                   help=f"where to place the model (default: {out_help()})")
    args = p.parse_args()

    entry = check_options(args.format, args.half)
    if args.weights and len(args.variant) != 1:
        raise SystemExit("--weights can only be used with a single --variant.")

    out_dir = Path(args.out) if args.out else entry.out
    for variant in args.variant:
        weights = Path(args.weights) if args.weights \
            else REPO / "output" / "training" / variant / "weights" / "best.pt"
        export_one(variant, weights, args.imgsz, args.half, out_dir, fmt=args.format)

    print("\n" + entry.next_steps)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
