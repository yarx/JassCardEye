"""Checks the scenes of the 3D counting sequences without Blender.

    python3 src/tools/render3d/test_scene.py

What a seed means is decided in scene.py, so the promises the 3D frames rest on can be held to account
here, on any runner: the same seed gives the same scene, a pile never mixes the decks or repeats a card,
the laid card ends up lying flat on top of the pile at the right height, the hand holds it by its edge
and has left it when it lies still, and the camera stays in the range the 2D generator draws from.
Exits non-zero on the first broken promise.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import scene as S  # noqa: E402

failures: list[str] = []


def check(ok: bool, what: str) -> None:
    if not ok:
        failures.append(what)


def main() -> int:
    for seed in range(40):
        for deck in S.DECKS:
            params = S.Params(deck=deck, played=seed % 30, laid=1 + seed % 3, seed=seed,
                              camera=S.CAMERA_PATHS[seed % 3], hand=seed % 5 != 0)
            a, b = S.resolve(params), S.resolve(params)
            check(json.dumps(a, sort_keys=True) == json.dumps(b, sort_keys=True), f"seed {seed}: the same seed gave two scenes")

            labels = [c["label"] for c in a["pile"]] + [c["label"] for c in a["laid"]]
            check(len(labels) == len(set(labels)), f"seed {seed}: a card lies twice")
            check(all(l.split("_")[0] in S.DECKS[deck] for l in labels), f"seed {seed}: a card of the other deck")
            check(all(0 <= S.class_id(l) < 72 for l in labels), f"seed {seed}: a class id out of range")

            camera = a["camera"]
            check(0 <= math.degrees(camera["tilt"]) <= 60, f"seed {seed}: tilt out of 0-60°")
            check(45 <= math.degrees(camera["fov"]) <= 55, f"seed {seed}: field of view out of 45-55°")
            check(1.8 * S.CARD_H <= camera["distance"] <= 4.2 * S.CARD_H, f"seed {seed}: distance out of range")

            end = a["duration"]
            for j, card in enumerate(a["laid"]):
                (x, y, z), (pitch, _, yaw) = S.laid_card_pose(card, end)
                check(abs(z - S.CARD_T * (params.played + j + 0.5)) < 1e-9, f"seed {seed}: laid card at the wrong height")
                check(abs(pitch) < 1e-9, f"seed {seed}: laid card not flat at the end")
                check(S.laid_card_resting(card, end), f"seed {seed}: laid card still moving at the end")
                (sx, sy, sz), _ = S.laid_card_pose(card, card["start"])
                check(math.hypot(sx, sy) > 0.25 and sz > 0.1, f"seed {seed}: laid card does not start off the pile")

                # The hand holds the card by its edge while carrying it, and is gone once the card lies.
                carry = card["start"] + 0.4 * S.LAY_SECONDS
                grip = S.hand_pose(card, carry)
                (cx, cy, _), _ = S.laid_card_pose(card, carry)
                check(grip is not None, f"seed {seed}: no hand while the card is carried")
                if grip is not None:
                    reach = math.hypot(grip[0][0] - cx, grip[0][1] - cy)
                    check(S.CARD_W / 2 - 1e-6 <= reach <= math.hypot(S.CARD_W, S.CARD_H) / 2 + 1e-6,
                          f"seed {seed}: the hand does not hold the card by its edge ({reach:.4f} m)")
                check(S.hand_pose(card, card["start"] + S.LAY_SECONDS + 0.01) is None,
                      f"seed {seed}: the hand is still there after its card")

            # The camera shakes by millimetres, not by centimetres.
            p0, _ = S.camera_pose(camera, 0.0, end)
            for k in range(1, 20):
                p1, _ = S.camera_pose(camera, k / 30, end)
                step = math.dist(p0, p1)
                limit = 0.02 if camera["path"] != "sweep" else 0.02 + camera["distance"] * abs(camera["sweep"]) / 30
                check(step < limit, f"seed {seed}: the camera jumps {step * 1000:.1f} mm in a frame")
                p0 = p1

    for bad in (S.Params(deck="spanish"), S.Params(played=36, laid=1), S.Params(table="marble")):
        try:
            bad.validated()
            failures.append(f"{bad} was accepted")
        except ValueError:
            pass

    for failure in failures[:20]:
        print(f"FAIL: {failure}")
    if failures:
        print(f"{len(failures)} failure(s)")
        return 1
    print("OK: scenes of the 3D counting sequences hold")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
