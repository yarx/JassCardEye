"""Check a JassCardEye dataset for internal consistency.

Catches the mistakes that silently poison training: a frame that is both a negative and carries a
card label, an image filed under two classes, a class in the label that disagrees with the crop
folder, or a labelled card that is cut off by the image border.

    python src/tools/check_dataset.py output/dataset data/real/val

Exits non-zero if any dataset fails, so it can guard a pipeline.
"""

from __future__ import annotations

import sys
from collections import Counter
from pathlib import Path

IMAGE_SUFFIXES = {".jpg", ".jpeg", ".png", ".bmp"}


def stems(pattern_root: Path, pattern: str) -> set[str]:
    return {p.stem for p in pattern_root.glob(pattern)} if pattern_root.exists() else set()


def check(root: Path) -> list[str]:
    """Returns a list of problems; empty means the dataset is sound."""
    problems: list[str] = []

    images = {p.stem for p in (root / "images").iterdir() if p.suffix.lower() in IMAGE_SUFFIXES}
    detect = stems(root / "detect" / "labels", "*.txt")
    locate = stems(root / "locate" / "labels", "*.txt")
    crops = stems(root / "classify_crop" / "train", "*/*")
    negatives = stems(root / "classify_full" / "train" / "none", "*")

    if not images:
        return [f"{root}: no images"]

    # Every image is either a labelled card or a negative - never both, never neither.
    for stem in sorted(negatives & detect):
        problems.append(f"negative also has a card label: {stem}")
    unassigned = images - detect - negatives
    for stem in sorted(unassigned)[:5]:
        problems.append(f"image is neither labelled nor a negative: {stem}")

    # An image must appear in exactly one classification folder.
    counts = Counter(p.stem for p in (root / "classify_full" / "train").glob("*/*"))
    for stem, n in sorted(counts.items()):
        if n > 1:
            problems.append(f"image filed under {n} classes: {stem}")

    for stem in sorted(detect - images)[:5]:
        problems.append(f"label without image: {stem}")
    for stem in sorted(detect - crops)[:5]:
        problems.append(f"labelled card without a crop: {stem}")

    # The class in the detect label must match the crop folder it was filed into.
    names = [n.strip() for n in (root / "classes.txt").read_text().splitlines() if n.strip()]
    folder = {p.stem: p.parent.name for p in (root / "classify_crop" / "train").glob("*/*")}
    for label in sorted((root / "detect" / "labels").glob("*.txt")):
        class_id = int(label.read_text().split()[0])
        if folder.get(label.stem) not in (None, names[class_id]):
            problems.append(
                f"class mismatch for {label.stem}: label says {names[class_id]}, "
                f"filed under {folder[label.stem]}"
            )

    # A labelled card must lie completely inside the frame - a cut-off card cannot be annotated.
    for label in sorted((root / "locate" / "labels").glob("*.txt")):
        values = [float(v) for v in label.read_text().split()[1:9]]
        if len(values) < 8:
            problems.append(f"malformed oriented box: {label.stem}")
            continue
        xs, ys = values[0::2], values[1::2]
        if min(min(xs), min(ys)) <= 0.0005 or max(max(xs), max(ys)) >= 0.9995:
            problems.append(f"labelled card touches the image border: {label.stem}")

    if locate and locate != detect:
        problems.append(f"detect and locate label sets differ by {len(detect ^ locate)} entries")

    return problems


def main(argv: list[str]) -> int:
    roots = [Path(a) for a in argv[1:]] or [Path("output/dataset")]
    failed = False

    for root in roots:
        if not root.exists():
            print(f"FAIL {root}: does not exist")
            failed = True
            continue

        problems = check(root)
        images = len(list((root / "images").iterdir()))
        negatives = len(stems(root / "classify_full" / "train" / "none", "*"))
        summary = f"{images} images, {images - negatives} labelled, {negatives} negative"

        if problems:
            failed = True
            print(f"FAIL {root} ({summary})")
            for p in problems[:20]:
                print(f"       {p}")
            if len(problems) > 20:
                print(f"       ... and {len(problems) - 20} more")
        else:
            print(f"OK   {root} ({summary})")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
