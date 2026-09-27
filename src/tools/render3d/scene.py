"""The scene of a counting sequence, resolved from its parameters and a seed.

Pure Python and no Blender: what a seed means - which cards, where they lie, how the hand moves, where
the light stands, how the camera shakes - is decided here, and `render_table.py` only builds and
renders it. The same seed therefore gives the same scene on every machine, whatever the renderer then
makes of it on a CPU or a GPU (see "3D counting sequences" in context/architecture/data-pipeline.md).

Units are metres; the table is the plane z = 0, the pile lies around the origin. Time is in seconds
from the start of the sequence.
"""
from __future__ import annotations

import math
import random
from dataclasses import asdict, dataclass

# A Jass card: 57 × 89 mm and 0.3 mm thick, like the cards the 2D generator draws.
CARD_W, CARD_H, CARD_T = 0.057, 0.089, 0.0003
CORNER_RADIUS = 0.0035

# The class order of JassCardEye.Dataset.Cards: class id = suit × 9 + rank.
SUITS = ["clubs", "diamonds", "hearts", "spades", "acorns", "roses", "bells", "shields"]
RANKS = ["6", "7", "8", "9", "10", "jack", "queen", "king", "ace"]
DECKS = {"french": SUITS[:4], "german": SUITS[4:]}

TABLES = ["felt", "solid", "speckle", "checker", "tartan"]
LIGHTS = ["lamp", "diffuse", "hdri"]
CAMERA_PATHS = ["handheld", "steady", "sweep"]

# How long laying one card takes, and its phases as fractions of that: the hand comes in carrying the
# card, lowers it over the pile, lets go, and leaves while the card settles.
LAY_SECONDS = 1.6
APPROACH, LOWER, RELEASE = 0.34, 0.50, 0.58
SETTLE_SECONDS = 0.12
LEAD_SECONDS, TAIL_SECONDS = 0.2, 0.4


def class_id(label: str) -> int:
    suit, rank = label.split("_", 1)
    return SUITS.index(suit) * len(RANKS) + RANKS.index(rank)


@dataclass(frozen=True)
class Params:
    """What the Dataset Tool asks for; "random" draws the value from the seed."""
    deck: str = "french"
    played: int = 8                # cards already on the pile when the sequence starts
    laid: int = 1                  # cards laid onto it during the sequence
    table: str = "random"
    light: str = "random"
    hand: bool = True
    camera: str = "handheld"
    seed: int = 1
    fps: int = 30
    size: int = 640

    def validated(self) -> "Params":
        if self.deck not in DECKS:
            raise ValueError(f"deck must be one of {', '.join(DECKS)}")
        if self.played < 0 or self.laid < 0 or self.played + self.laid > 36:
            raise ValueError("played + laid must stay within the 36 cards of a deck")
        for value, allowed, name in ((self.table, TABLES, "table"), (self.light, LIGHTS, "light"),
                                     (self.camera, CAMERA_PATHS, "camera")):
            if value != "random" and value not in allowed:
                raise ValueError(f"{name} must be random or one of {', '.join(allowed)}")
        return self


def smoothstep(t: float) -> float:
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def lerp(a, b, t: float):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def resolve(params: Params) -> dict:
    """The whole sequence as plain data: everything `render_table.py` needs, nothing it has to draw."""
    p = params.validated()
    rng = random.Random(p.seed)
    suits = DECKS[p.deck]
    labels = [f"{s}_{r}" for s in suits for r in RANKS]
    rng.shuffle(labels)

    # The pile: scattered around the centre and turned freely, like the 2D generator's heap.
    pile = []
    for i in range(p.played):
        radius = 0.55 * CARD_H * math.sqrt(rng.random())
        angle = rng.uniform(0, 2 * math.pi)
        pile.append({"label": labels[i], "x": radius * math.cos(angle), "y": radius * math.sin(angle),
                     "rotation": rng.uniform(0, 2 * math.pi)})

    camera = _camera(p, rng)
    duration = LEAD_SECONDS + p.laid * LAY_SECONDS + TAIL_SECONDS

    laid = []
    for j in range(p.laid):
        start = LEAD_SECONDS + j * LAY_SECONDS
        # The hand comes from the player's side of the table, which is roughly where the phone is.
        entry = camera["azimuth"] + rng.uniform(-1.6, 1.6)
        target_radius = 0.30 * CARD_H * math.sqrt(rng.random())
        target_angle = rng.uniform(0, 2 * math.pi)
        laid.append({
            "label": labels[p.played + j],
            "start": start,
            "entry": entry,
            "x": target_radius * math.cos(target_angle),
            "y": target_radius * math.sin(target_angle),
            "rotation": rng.uniform(0, 2 * math.pi),
            "carry_turn": rng.uniform(-0.45, 0.45),      # how far it still turns before it lands
            "carry_pitch": math.radians(rng.uniform(10, 24)),
            "drop_height": rng.uniform(0.008, 0.02),     # let go this far above the pile
            "slide": (rng.uniform(-0.006, 0.006), rng.uniform(-0.006, 0.006)),
            "z": CARD_T * (p.played + j + 0.5),
        })

    return {
        "params": asdict(p),
        "duration": duration,
        "frames": round(duration * p.fps),
        "pile": pile,
        "laid": laid,
        "table": _table(p, rng),
        "light": _light(p, rng),
        "hand": _hand(rng) if p.hand else None,
        "camera": camera,
        "exposure": rng.uniform(-0.35, 0.3),
    }


def _camera(p: Params, rng: random.Random) -> dict:
    path = p.camera
    # The same ranges as the 2D generator: tilt 0-60° from the vertical, field of view 45-55°, and a
    # distance of 1.8-4.2 card heights, so a card covers about as much of the frame in both.
    shake = {"handheld": 1.0, "steady": 0.12, "sweep": 1.0}[path]
    waves = [(rng.uniform(0.4, 2.8), rng.uniform(0, 2 * math.pi)) for _ in range(12)]
    return {
        "path": path,
        "tilt": math.radians(rng.uniform(0, 60)),
        "azimuth": rng.uniform(0, 2 * math.pi),
        "roll": rng.uniform(-math.pi, math.pi),
        "distance": CARD_H * rng.uniform(1.8, 4.2),
        "fov": math.radians(rng.uniform(45, 55)),
        "target": (rng.uniform(-0.08, 0.08) * CARD_H, rng.uniform(-0.08, 0.08) * CARD_H),
        "shake_position": 0.004 * shake,                # metres
        "shake_rotation": math.radians(0.8) * shake,
        "sweep": math.radians(rng.uniform(-14, 14)) if path == "sweep" else 0.0,
        "waves": waves,
    }


def _table(p: Params, rng: random.Random) -> dict:
    kind = p.table if p.table != "random" else rng.choice(TABLES)
    # Cloth colours: the greens, blues, reds and browns of Jass mats and tablecloths, and some greys.
    hue = rng.choice([rng.uniform(0.25, 0.42), rng.uniform(0.55, 0.66), rng.uniform(-0.03, 0.04),
                      rng.uniform(0.06, 0.11), rng.uniform(0, 1)]) % 1.0
    saturation = rng.uniform(0.0, 0.15) if rng.random() < 0.2 else rng.uniform(0.35, 0.85)
    value = rng.uniform(0.12, 0.6)
    second = ((hue + rng.uniform(-0.15, 0.15)) % 1.0, saturation * rng.uniform(0.5, 1.0),
              min(1.0, value * rng.uniform(1.3, 2.2)))
    return {"kind": kind, "color": (hue, saturation, value), "second": second,
            "scale": rng.uniform(18, 70), "rotation": rng.uniform(0, math.pi)}


def _light(p: Params, rng: random.Random) -> dict:
    kind = p.light if p.light != "random" else rng.choices(LIGHTS, weights=[0.5, 0.25, 0.25])[0]
    angle = rng.uniform(0, 2 * math.pi)
    reach = rng.uniform(0.0, 1.1)
    return {
        "kind": kind,
        # A lamp above the table, off to one side: small, so its shadows are hard.
        "position": (reach * math.cos(angle), reach * math.sin(angle), rng.uniform(0.7, 1.6)),
        "radius": rng.uniform(0.01, 0.06),
        "power": rng.uniform(60, 160),
        "temperature": rng.uniform(2700, 5600),
        # A lamp leaves the room dim around its pool of light; diffuse light is the room itself, a big
        # soft source and bright surroundings; an HDRI brings its own. Calibrated by eye and by the mean
        # brightness of rendered frames, so that none of the three washes the cards out.
        "ambient": {"lamp": rng.uniform(0.05, 0.25), "diffuse": rng.uniform(0.2, 0.45), "hdri": rng.uniform(0.6, 1.2)}[kind],
        "hdri": rng.choice(["interior.exr", "courtyard.exr", "city.exr", "forest.exr", "studio.exr",
                            "sunrise.exr", "sunset.exr", "night.exr"]),
        "hdri_rotation": rng.uniform(0, 2 * math.pi),
    }


def _hand(rng: random.Random) -> dict:
    # Skin from light to dark, as linear sRGB.
    tone = rng.uniform(0, 1)
    light, dark = (0.80, 0.58, 0.47), (0.24, 0.14, 0.09)
    return {"skin": lerp(light, dark, tone), "size": rng.uniform(0.9, 1.12)}


# --- Poses over time -------------------------------------------------------------------------------

def laid_card_pose(card: dict, t: float) -> tuple[tuple[float, float, float], tuple[float, float, float]]:
    """Where a laid card is at time t: position and (pitch, 0, yaw). Before its turn it waits in the
    hand's starting place, outside the frame."""
    local = (t - card["start"]) / LAY_SECONDS
    if local < 0:
        local = 0.0
    rest = (card["x"] + card["slide"][0], card["y"] + card["slide"][1], card["z"])
    above = (card["x"], card["y"], card["z"] + 0.045)
    low = (card["x"], card["y"], card["z"] + card["drop_height"])
    start = (card["x"] + 0.32 * math.cos(card["entry"]), card["y"] + 0.32 * math.sin(card["entry"]),
             card["z"] + 0.11)
    yaw_final = card["rotation"]

    if local < APPROACH:
        s = smoothstep(local / APPROACH)
        position = lerp(start, above, s)
        pitch = card["carry_pitch"]
        yaw = yaw_final + card["carry_turn"] * (1 - 0.6 * s)
    elif local < LOWER:
        s = smoothstep((local - APPROACH) / (LOWER - APPROACH))
        position = lerp(above, low, s)
        pitch = card["carry_pitch"] * (1 - 0.7 * s)
        yaw = yaw_final + card["carry_turn"] * 0.4 * (1 - s)
    elif local < RELEASE:
        position, pitch, yaw = low, card["carry_pitch"] * 0.3, yaw_final
    else:
        # Let go: it falls the last centimetre and slides a little before it lies still.
        fall = (t - card["start"] - RELEASE * LAY_SECONDS) / SETTLE_SECONDS
        s = min(1.0, max(0.0, fall))
        position = lerp(low, rest, s * s if s < 1 else 1.0)
        pitch = card["carry_pitch"] * 0.3 * (1 - s)
        yaw = yaw_final
    return position, (pitch, 0.0, yaw)


def laid_card_resting(card: dict, t: float) -> bool:
    return t >= card["start"] + RELEASE * LAY_SECONDS + SETTLE_SECONDS


def hand_pose(card: dict, t: float) -> tuple[tuple[float, float, float], float, float] | None:
    """The grip of the hand that lays `card`: position, heading and pitch; None while it is away."""
    local = (t - card["start"]) / LAY_SECONDS
    if local < 0 or local > 1:
        return None
    heading = card["entry"] + math.pi                     # the hand points from its side towards the pile
    (x, y, z), (pitch, _, yaw) = laid_card_pose(card, min(t, card["start"] + RELEASE * LAY_SECONDS))
    # The fingers hold the card at the edge that faces the hand: how far that edge lies from the centre
    # depends on how the card is turned against the hand.
    towards = heading + math.pi - yaw
    dx, dy = abs(math.cos(towards)), abs(math.sin(towards))
    reach = min(CARD_W / 2 / dx if dx > 1e-6 else math.inf, CARD_H / 2 / dy if dy > 1e-6 else math.inf)
    grip = (x - reach * math.cos(heading), y - reach * math.sin(heading), z + reach * math.sin(pitch))
    if local <= RELEASE:
        return grip, heading, pitch
    # After letting go the hand lifts and leaves the way it came.
    s = smoothstep((local - RELEASE) / (1 - RELEASE))
    away = (grip[0] + 0.34 * math.cos(card["entry"]), grip[1] + 0.34 * math.sin(card["entry"]), grip[2] + 0.1)
    return lerp(grip, away, s), heading, pitch * (1 - s)


def camera_pose(camera: dict, t: float, duration: float) -> tuple[tuple[float, float, float], tuple[float, float, float]]:
    """Camera position and the point it looks at, with the shake of a phone held over the table."""
    azimuth = camera["azimuth"] + camera["sweep"] * (t / max(duration, 1e-6) - 0.5)
    tilt, distance = camera["tilt"], camera["distance"]
    tx, ty = camera["target"]
    waves = camera["waves"]

    def wave(i: int) -> float:
        # Three waves per axis, of different speeds, so the shake does not repeat within a sequence.
        return sum(math.sin(2 * math.pi * f * t + phase) for f, phase in waves[3 * i:3 * i + 3]) / 3

    shake = camera["shake_position"]
    position = (tx + distance * math.sin(tilt) * math.cos(azimuth) + shake * wave(0),
                ty + distance * math.sin(tilt) * math.sin(azimuth) + shake * wave(1),
                distance * math.cos(tilt) + shake * wave(2))
    turn = camera["shake_rotation"] * distance
    target = (tx + turn * wave(3), ty + turn * wave(1), 0.0)
    return position, target
