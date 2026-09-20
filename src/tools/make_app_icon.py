"""Draws a generated app icon.

A small fanned pile on the green of a Jass mat, with the detection brackets the live view draws
around the card it has found. The icon that ships is a designed picture of the same idea; this
drawing is the fallback, so the script refuses to overwrite an existing file unless it is passed
--force.

    python3 src/tools/make_app_icon.py

Writes src/app/ios/Assets.xcassets/AppIcon.appiconset/icon-1024.png (1024x1024, opaque, no alpha channel -
the App Store rejects both an alpha channel and pre-rounded corners; iOS masks the icon itself).
Everything is drawn at 4x and downsampled, which is what gives the edges their smoothness.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "src" / "app" / "ios" / "Assets.xcassets" / "AppIcon.appiconset" / "icon-1024.png"

SIZE = 1024
SCALE = 4                      # supersampling factor
S = SIZE * SCALE

FELT_DARK = (18, 74, 52)       # Jass mat green, the corners
FELT_LIGHT = (46, 122, 88)     # …and the lit centre
CARD = (252, 251, 247)         # card stock, not pure white
CARD_EDGE = (206, 202, 192)
SHADOW = (8, 40, 28)
RED = (200, 36, 44)            # the red of a Jass suit
BRACKET = (46, 214, 106)       # the detection box of the live view


def felt(image: Image.Image) -> None:
    """A radial wash from a lit centre to darker corners - a table under a lamp."""
    draw = ImageDraw.Draw(image)
    cx, cy = S * 0.5, S * 0.44
    far = math.hypot(S * 0.5, S * 0.5)
    for i in range(220, -1, -1):
        t = i / 220
        radius = far * t
        mix = t ** 1.35
        colour = tuple(round(a + (b - a) * mix) for a, b in zip(FELT_LIGHT, FELT_DARK))
        draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], fill=colour)


def card_polygon(cx: float, cy: float, w: float, h: float, angle: float) -> list[tuple[float, float]]:
    """The four corners of a card, rotated about its centre."""
    a = math.radians(angle)
    cos, sin = math.cos(a), math.sin(a)
    return [
        (cx + (dx * cos - dy * sin), cy + (dx * sin + dy * cos))
        for dx, dy in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2))
    ]


def rounded_card(size: tuple[int, int], radius: int, fill, outline=None, width: int = 0) -> Image.Image:
    """A card face as its own image, so it can be rotated with a soft edge."""
    card = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(card)
    draw.rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius=radius,
                           fill=fill, outline=outline, width=width)
    return card


def heart(draw: ImageDraw.ImageDraw, cx: float, cy: float, size: float, colour) -> None:
    """A Jass heart: two lobes over a point, drawn as a filled polygon."""
    points = []
    for step in range(721):
        t = math.radians(step * 0.5)
        x = 16 * math.sin(t) ** 3
        y = -(13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t))
        points.append((cx + x * size / 32, cy + y * size / 32))
    draw.polygon(points, fill=colour)


def place(base: Image.Image, face: Image.Image, centre: tuple[float, float], angle: float) -> None:
    """Rotates a card face and pastes it, with a soft drop shadow under it."""
    rotated = face.rotate(angle, resample=Image.BICUBIC, expand=True)

    shadow = Image.new("RGBA", rotated.size, (0, 0, 0, 0))
    shadow.paste(Image.new("RGBA", rotated.size, SHADOW + (150,)), (0, 0), rotated)
    shadow = shadow.filter(ImageFilter.GaussianBlur(14 * SCALE))
    sx = round(centre[0] - rotated.width / 2)
    sy = round(centre[1] - rotated.height / 2)
    base.alpha_composite(shadow, (sx + 5 * SCALE, sy + 11 * SCALE))
    base.alpha_composite(rotated, (sx, sy))


def brackets(draw: ImageDraw.ImageDraw, corners: list[tuple[float, float]], arm: float, width: float) -> None:
    """The four corner brackets of the detection box, following the card's own rotation."""
    count = len(corners)
    for i, (x, y) in enumerate(corners):
        for neighbour in (corners[(i - 1) % count], corners[(i + 1) % count]):
            dx, dy = neighbour[0] - x, neighbour[1] - y
            length = math.hypot(dx, dy)
            draw.line([(x, y), (x + dx / length * arm, y + dy / length * arm)],
                      fill=BRACKET, width=round(width), joint="curve")
        draw.ellipse([x - width / 2, y - width / 2, x + width / 2, y + width / 2], fill=BRACKET)


def build() -> Image.Image:
    base = Image.new("RGBA", (S, S), FELT_DARK + (255,))
    felt(base)

    # A card is 57x88 mm; the icon keeps that ratio so the shapes read as cards. Kept small enough
    # that the whole group sits inside the circle iOS masks the icon to, with room for the brackets.
    w, h = round(S * 0.275), round(S * 0.275 * 88 / 57)
    radius = round(w * 0.11)
    edge = max(1, round(2 * SCALE))
    cx, cy = S * 0.5, S * 0.5

    plain = rounded_card((w, h), radius, CARD, outline=CARD_EDGE, width=edge)

    # Two cards under the top one. A narrow fan is enough to read as a pile at 40 px, where a wide
    # one turns into a white blob.
    place(base, plain, (cx - w * 0.30, cy + h * 0.015), 10)
    place(base, plain, (cx + w * 0.30, cy + h * 0.010), -9)

    # The top card, square to the frame: this is the one that gets recognised, and a straight card
    # under a straight box is what the live view actually shows.
    top = rounded_card((w, h), radius, CARD, outline=CARD_EDGE, width=edge)
    heart(ImageDraw.Draw(top), w * 0.5, h * 0.48, w * 0.66, RED)
    place(base, top, (cx, cy), 0)

    # The detection box, drawn where the live view would draw it: clear of the card, so it survives
    # being scaled down to the home screen.
    half_w, half_h = w * 0.96, h * 0.62
    corners = [(cx - half_w, cy - half_h), (cx + half_w, cy - half_h),
               (cx + half_w, cy + half_h), (cx - half_w, cy + half_h)]
    brackets(ImageDraw.Draw(base), corners, arm=w * 0.40, width=14 * SCALE)

    return base.convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)


if __name__ == "__main__":
    import sys

    # The shipped icon is a designed picture, not this drawing. This script is the fallback, and it
    # does not get to overwrite the designed icon by accident.
    if OUT.exists() and "--force" not in sys.argv:
        print(f"{OUT.relative_to(REPO)} already exists and is the icon that ships.")
        print("Pass --force to replace it with this drawing.")
        raise SystemExit(1)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    build().save(OUT, "PNG")
    print(f"wrote {OUT.relative_to(REPO)}")
