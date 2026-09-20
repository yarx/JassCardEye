"""Checks that a run's images really are planned the way the generator claims.

Two things are decided by an image's index rather than drawn, and both have to be exact rather than
right on average.

The camera tilt sweeps the whole range evenly. Flat viewing angles are the hardest case for the
model, and an angle drawn at random would make them the rarest in the data. So the angle is not
drawn but derived from the image index, which makes the spread exact - but only if the index
permutation is really a permutation. It is easy to get that subtly wrong: a fixed prime step is
congruent to 1 for some counts, which turns the "permutation" into the identity and leaves a run
sorted by angle.

Each deck gets exactly half the run, with an even sweep of its own. Jass is played with one deck or
the other, so a pile is never mixed and the model has to learn two full decks. Deck and tilt come
from the same permuted position, which is what keeps one deck from ending up with more flat frames
than the other - precisely the imbalance the sweep exists to remove.

Everything below is checked by mapping each angle back to its place in the sweep rather than by
comparing angles to expected values. Image i of a run of n is meant to sit at

    tilt = min + (position + ½) × (max - min) / n,   position a permutation of 0 … n-1

so the positions can be recovered by inverting that. Then one statement covers what would otherwise
be four separate assertions with four tolerances: **the recovered positions are exactly 0 … n-1,
each once.** That pins the spacing, both ends, the half-step at each end and the absence of
duplicates at the same time - and the deck split becomes a statement about the parity of those
positions, which needs no tolerance at all.

Rendering two hundred thousand images to find any of this out costs twenty minutes. Asking the
generator for its plan costs milliseconds, so that is what this does.

    python src/tools/check_run_plan.py [count ...]
"""

from __future__ import annotations

import subprocess
import sys
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
# Small, awkward and realistic counts. 12 is one a fixed prime step gets wrong; 300 is what
# CI generates; 200000 is a standard run. Odd counts are in on purpose: a deck cannot get exactly
# half of an odd run, and the check has to say so in the right way rather than fail.
COUNTS = [1, 2, 12, 97, 300, 500, 3000, 200000]
# How far a recovered position may sit from a whole number. The generator computes the angle in
# float and prints six decimals, which at 200 000 images is worth about 0.04 of a position; half a
# position would be a real defect, so this leaves an order of magnitude either way.
GRID_TOLERANCE = 0.2
RANGE = (0.0, 0.0)  # filled from the generator itself, see plan_summary()
DECKS: list[str] = []


# Runs the plan command of the dataset CLI (src/tools/dataset/Cli). It expects a Release build to
# exist (--no-build), which CI makes in an earlier step.
def cli_plan(count: int, *extra: str) -> str:
    result = subprocess.run(
        ["dotnet", "run", "--project", str(REPO / "src" / "tools" / "dataset" / "Cli"),
         "-c", "Release", "--no-build", "--", "plan", "--count", str(count), *extra],
        capture_output=True, text=True, check=True)
    return result.stdout


def plan(count: int) -> list[tuple[float, str]]:
    rows = [line.split() for line in cli_plan(count).splitlines() if line.strip()]
    return [(float(tilt), deck) for tilt, deck in rows]


def plan_summary() -> tuple[tuple[float, float], list[str]]:
    """Asked for rather than known.

    Writing the range and the deck names out here would put the values this checks in the same file
    as the check, so a changed ceiling or a third deck would fail for the wrong reason - and the
    obvious fix would be to edit them until it passes again.
    """
    fields = dict(line.split(maxsplit=1) for line in cli_plan(1, "--summary").strip().splitlines())
    return (float(fields["min"]), float(fields["max"])), fields["decks"].split()


def check(count: int, failures: list[str]) -> None:
    def fail(message: str) -> None:
        failures.append(f"count {count}: {message}")

    low, high = RANGE
    rows = plan(count)
    if len(rows) != count:
        fail(f"got {len(rows)} images, expected {count}")
        return

    # Invert tilt = low + (position + ½) × (high - low) / count.
    step = (high - low) / count
    exact = [(tilt - low) / step - 0.5 for tilt, _ in rows]
    positions = [round(p) for p in exact]

    off_grid = [(i, rows[i][0], p) for i, p in enumerate(exact)
                if abs(p - round(p)) > GRID_TOLERANCE]
    if off_grid:
        i, tilt, p = off_grid[0]
        fail(f"{len(off_grid)} angle(s) do not sit on the sweep; image {i} is {tilt:.6f}°, "
             f"which is position {p:.4f} - the sweep is offset or unevenly spaced")
        return

    if sorted(positions) != list(range(count)):
        seen = Counter(positions)
        repeated = [p for p, n in seen.items() if n > 1]
        missing = [p for p in range(count) if p not in seen]
        fail(f"the positions are not a permutation of 0…{count - 1}: "
             f"{len(repeated)} repeated, {len(missing)} never used")
        return

    decks = [deck for _, deck in rows]
    unknown = set(decks) - set(DECKS)
    if unknown:
        fail(f"unknown deck(s) {sorted(unknown)}, the generator names {DECKS}")
        return

    # With the positions pinned, the deck split is a statement about their parity: each deck takes
    # every second position, so it holds half the run and every second step of the sweep at once.
    parities = {deck: {positions[i] % 2 for i, d in enumerate(decks) if d == deck}
                for deck in DECKS}
    for deck, seen in parities.items():
        if len(seen) > 1:
            fail(f"deck {deck} takes both even and odd positions, so the two decks interleave "
                 f"unevenly and one gets more flat frames than the other")
    if len({next(iter(s)) for s in parities.values() if s}) < len([s for s in parities.values() if s]):
        fail("two decks take the same positions")

    counts = Counter(decks)
    share = count / len(DECKS)
    for deck in DECKS:
        if abs(counts[deck] - share) > 1:  # an odd run cannot split evenly
            fail(f"deck {deck} holds {counts[deck]} images, expected {share:.0f}")

    # The run must not be sorted by angle, nor grouped by deck: what anyone looks at first is the
    # first few hundred images, and those have to span the range and show both decks.
    #
    # The prefix is a tenth of the run rather than a flat 500, because a flat 500 is the whole run
    # once the count is small - and then a perfectly sorted run passes for spanning the range.
    if count >= 100:
        prefix = rows[: max(2, min(500, count // 10))]
        span = [t for t, _ in prefix]
        if max(span) - min(span) < (high - low) * 0.75:
            fail(f"the first {len(prefix)} images only span "
                 f"{min(span):.1f}–{max(span):.1f}° - the run is close to sorted by angle")
        seen = Counter(d for _, d in prefix)
        if len(seen) < len(DECKS):
            fail(f"the first {len(prefix)} images only show {sorted(seen)} - "
                 f"the run is grouped by deck")


def main() -> int:
    global RANGE, DECKS
    RANGE, DECKS = plan_summary()
    print(f"as the generator reports it: tilt {RANGE[0]:g}–{RANGE[1]:g}°, decks {', '.join(DECKS)}")
    counts = [int(a) for a in sys.argv[1:]] or COUNTS
    failures: list[str] = []
    for count in counts:
        check(count, failures)
        print(f"  {'FAIL' if any(f.startswith(f'count {count}:') for f in failures) else 'ok  '} "
              f"count {count}")
    if failures:
        print("\nFAILED:")
        for failure in failures:
            print(f"  {failure}")
        return 1
    print("\nevery count checked: the tilt sweeps evenly and each deck holds half of it")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
