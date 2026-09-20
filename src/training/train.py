"""Train one of the three JassCardEye approaches.

The dataset layout is produced by JassCardEye.Dataset.Cli; see context/architecture/data-pipeline.md.
By default the synthetic dataset is used for training and the hand-labelled real photos for
validation - measuring on real frames is the only number that says anything about the domain gap.

    python src/training/train.py --variant c            # detector, 72 classes
    python src/training/train.py --variant b1           # oriented box, 1 class
    python src/training/train.py --variant b2           # classify the rectified crop
    python src/training/train.py --variant a            # classify the whole image

Runs on CUDA, Apple MPS or CPU - whichever is available.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]

# task     : Ultralytics task
# folder   : subfolder of the dataset holding this variant
# model    : pretrained weights, {size} filled from --size
# kind     : "yaml" -> detection-style data.yaml, "dir" -> classification folder
VARIANTS = {
    "c":  dict(task="detect",   folder="detect",        model="yolo11{size}.pt",     kind="yaml"),
    "b1": dict(task="obb",      folder="locate",        model="yolo11{size}-obb.pt", kind="yaml"),
    "b2": dict(task="classify", folder="classify_crop", model="yolo11{size}-cls.pt", kind="dir"),
    "a":  dict(task="classify", folder="classify_full", model="yolo11{size}-cls.pt", kind="dir"),
}


def install_progress(model, variant: str, run_id: str, artifacts: Path) -> None:
    """Hands every epoch to src/scripts/report_epoch.sh, and knows nothing else about it.

    What happens to a progress signal - which file it lands in, whether it is uploaded, who gets a
    message - belongs to the pipeline, not to the script that trains a model. This call is the whole
    seam between the two, and it is deliberately the only thing here that knows the pipeline exists.
    """
    def report(trainer) -> None:
        try:
            epoch = int(getattr(trainer, "epoch", 0)) + 1
            fitness = getattr(trainer, "fitness", None)
            subprocess.run(
                [str(REPO / "src" / "scripts" / "report_epoch.sh"), str(artifacts), run_id, variant,
                 str(epoch), "" if fitness is None else str(float(fitness))],
                check=False, env={**os.environ, "PYTHON": sys.executable},
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except Exception as error:  # noqa: BLE001 - a report may not end a training
            print(f"!  progress report failed: {error}")

    model.add_callback("on_fit_epoch_end", report)


def pick_device(requested: str) -> str:
    """CUDA if present, else Apple MPS, else CPU - so the same command works everywhere."""
    if requested != "auto":
        return requested
    import torch

    if torch.cuda.is_available():
        return "0"
    if torch.backends.mps.is_available():
        return "mps"
    return "cpu"


def link_or_copy_file(link: Path, source: Path) -> None:
    """Hard-link a file, or copy it where that is not possible (different volume, odd file system).

    Deliberately not a symlink: Ultralytics resolves those before deriving the class from the parent
    folder, which would throw the class away.
    """
    if link.exists() or link.is_symlink():
        link.unlink()
    try:
        link.hardlink_to(source)
    except (OSError, NotImplementedError):
        shutil.copy2(source, link)


def read_class_names(dataset: Path) -> list[str]:
    names = [n.strip() for n in (dataset / "classes.txt").read_text().splitlines() if n.strip()]
    if not names:
        raise SystemExit(f"classes.txt in {dataset} is empty")
    return names


def ensure_image_links(images: Path) -> None:
    """Recreates a task's images/ folder as links into the dataset's shared images/.

    These folders are derived, so they are not versioned - but rebuilding them needs nothing but the
    file names. Doing it here means a dataset whose links are missing, such as a fresh checkout of
    data/real/val, trains without running the .NET rebuild first. The pipeline runs that rebuild
    anyway (src/scripts/run_training.sh), so this is for training by hand.
    """
    shared = images.parent.parent / "images"
    if not shared.exists():
        raise SystemExit(f"{shared} is missing - is {images.parent.parent} really a dataset?")

    images.mkdir(parents=True, exist_ok=True)
    for source in sorted(shared.iterdir()):
        if source.is_file():
            link_or_copy_file(images / source.name, source)


def build_detection_yaml(spec: dict, train_ds: Path, val_ds: Path, stage: Path) -> Path:
    """Writes a data.yaml pointing train and val at two different datasets."""
    train_images = (train_ds / spec["folder"] / "images").resolve()
    val_images = (val_ds / spec["folder"] / "images").resolve()
    for images in (train_images, val_images):
        if not images.exists():
            ensure_image_links(images)

    names = ["card"] if spec["task"] == "obb" else read_class_names(train_ds)
    inline = ", ".join(f"'{n}'" for n in names)
    yaml = stage / "data.yaml"
    yaml.parent.mkdir(parents=True, exist_ok=True)
    # Absolute paths keep this file independent of where it is read from.
    yaml.write_text(
        "# Auto-generated by src/training/train.py\n"
        f"train: {train_images.as_posix()}\n"
        f"val: {val_images.as_posix()}\n"
        f"nc: {len(names)}\n"
        f"names: [{inline}]\n"
    )
    return yaml


def build_classification_dir(spec: dict, train_ds: Path, val_ds: Path, stage: Path) -> Path:
    """Stages <root>/train and <root>/val side by side, as Ultralytics classification expects.

    Both splits get the *same* set of class folders - the union of the two, empty ones included.
    Ultralytics derives the class indices from the training folders alone, so a class that only
    occurs in validation would otherwise be indexed out of range. It numbers them alphabetically by
    folder name, so a classifier's indices are not the class IDs of classes.txt - only its names are.
    """
    train_src = (train_ds / spec["folder"] / "train").resolve()
    val_src = (val_ds / spec["folder"] / "train").resolve()
    for src in (train_src, val_src):
        if not src.exists():
            raise SystemExit(
                f"{src} is missing - run:\n"
                f"  dotnet run --project src/tools/dataset/Cli -- rebuild --dataset {src.parent.parent}"
            )

    train_classes = {d.name for d in train_src.iterdir() if d.is_dir() and any(d.iterdir())}
    val_classes = {d.name for d in val_src.iterdir() if d.is_dir() and any(d.iterdir())}
    classes = sorted(train_classes | val_classes)

    missing = sorted(val_classes - train_classes)
    if missing:
        print(
            f"! The training data has no examples of {len(missing)} class(es) that occur in "
            f"validation: {', '.join(missing[:6])}{' …' if len(missing) > 6 else ''}\n"
            f"  Generate more images so every class is covered, otherwise the reported accuracy "
            f"is meaningless."
        )

    for split, src in (("train", train_src), ("val", val_src)):
        root = stage / split
        if root.exists():
            shutil.rmtree(root)
        for name in classes:
            (root / name).mkdir(parents=True)
            source_dir = src / name
            if not source_dir.exists():
                continue
            for file in source_dir.iterdir():
                if file.is_file():
                    link_or_copy_file(root / name / file.name, file)

    return stage


def release(model) -> None:
    """Shuts the training's data loaders down while the interpreter is still whole.

    When `train()` returns, Ultralytics is done: best.pt, results.csv and the final validation are
    written, and it has already collected garbage and emptied the CUDA cache. What it leaves behind
    is the trainer on the model, which still holds the training and validation loaders - their
    worker processes and, on CUDA, the thread that pins memory. Left alone, those are torn down while
    Python shuts down, in whatever order that happens to take. That is the likeliest cause of a
    "terminate called without an active exception" at exactly this point, exit 134 after a complete
    variant - likely, not proven, since it cannot be made to happen on purpose. Dropping the loaders
    here lets their own clean-up stop the workers first.
    """
    import gc

    import torch

    trainer = model.trainer
    if trainer is not None:
        if getattr(trainer, "validator", None) is not None:
            trainer.validator.dataloader = None
        trainer.train_loader = trainer.test_loader = None
    model.trainer = None
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.empty_cache()


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--variant", required=True, choices=sorted(VARIANTS), help="which approach to train")
    p.add_argument("--train-data", default="output/dataset", help="dataset used for training")
    p.add_argument("--val-data", default="data/real/val", help="dataset used for validation")
    p.add_argument("--size", default="n", choices=["n", "s", "m", "l"], help="model size")
    p.add_argument("--model", default=None, help="explicit weights, overrides --size")
    # 20, the same as src/scripts/run_training.sh and the train workflow. A pipeline run always
    # passes --epochs, so this default is only ever seen by someone calling train.py by hand -
    # which is exactly when a silent disagreement with the rest of the pipeline does harm.
    p.add_argument("--epochs", type=int, default=20)
    p.add_argument("--imgsz", type=int, default=640)
    p.add_argument("--batch", type=int, default=16)
    p.add_argument("--device", default="auto", help="auto | cpu | mps | 0")
    p.add_argument(
        "--close-mosaic",
        type=int,
        default=None,
        help="epochs at the END of training that run without mosaic augmentation (Ultralytics "
             "default: 10). This counts epochs, not a fraction, so on a short run the default "
             "silently disables mosaic for the whole training - with --epochs 10 use about 2-3",
    )
    p.add_argument(
        "--warmup-epochs",
        type=float,
        default=None,
        help="epochs spent ramping the learning rate up (Ultralytics default: 3). Also an absolute "
             "count: 3 of 10 epochs is 30 %% warmup, 3 of 20 only 15 %% - on a short run use ~1.5",
    )
    p.add_argument(
        "--workers",
        type=int,
        default=None,
        help="dataloader processes; Ultralytics uses 0 on MPS, which makes image decoding the "
             "bottleneck - try 8 there",
    )
    p.add_argument("--name", default=None, help="run name (default: the variant)")
    p.add_argument("--run-id", default=None,
                   help="the run this variant belongs to; enables the per-epoch progress report")
    p.add_argument("--artifacts", type=Path, default=None,
                   help="the run folder holding manifest.json (with --run-id)")
    p.add_argument("--smoke", action="store_true", help="tiny run just to prove the pipeline works")
    args = p.parse_args()

    spec = VARIANTS[args.variant]
    train_ds = (REPO / args.train_data).resolve() if not Path(args.train_data).is_absolute() else Path(args.train_data)
    val_ds = (REPO / args.val_data).resolve() if not Path(args.val_data).is_absolute() else Path(args.val_data)

    if not train_ds.exists():
        raise SystemExit(f"Training dataset not found: {train_ds}")
    if not val_ds.exists():
        print(f"! Validation dataset {val_ds} not found - falling back to the training data.")
        val_ds = train_ds

    if args.smoke:
        args.epochs, args.imgsz, args.batch = 1, 160, 4

    stage = REPO / "output" / "training" / "_stage" / args.variant
    data = (build_detection_yaml if spec["kind"] == "yaml" else build_classification_dir)(
        spec, train_ds, val_ds, stage
    )

    device = pick_device(args.device)
    weights = args.model or spec["model"].format(size=args.size)

    print(f"variant {args.variant}  task {spec['task']}  weights {weights}  device {device}")
    print(f"  train {train_ds}")
    print(f"  val   {val_ds}")

    from ultralytics import YOLO

    # Several Ultralytics defaults are absolute epoch counts, not fractions, so they mean something
    # different on a short run than on a long one. Passed through only when set explicitly.
    extra = {} if args.workers is None else {"workers": args.workers}
    if args.close_mosaic is not None:
        extra["close_mosaic"] = args.close_mosaic
    if args.warmup_epochs is not None:
        extra["warmup_epochs"] = args.warmup_epochs

    model = YOLO(weights)
    if args.run_id and args.artifacts:
        install_progress(model, args.variant, args.run_id, args.artifacts)
    model.train(
        data=str(data),
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        device=device,
        **extra,
        project=str(REPO / "output" / "training"),
        name=args.name or args.variant,
        exist_ok=True,
        # Cards are point-symmetric, but a mirrored card is a different card - never flip.
        fliplr=0.0,
        flipud=0.0,
        # Ultralytics 8.3.155 cannot plot classification training batches; skip plots for that task.
        plots=spec["task"] != "classify",
    )
    release(model)
    return 0


if __name__ == "__main__":
    sys.exit(main())
