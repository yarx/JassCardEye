"""Derives the Android launcher icon and the Play Store graphics from the iOS icon.

The iOS icon is the one designed picture; Android gets the same picture, not a second drawing that
could drift from it. What differs is how a launcher shows an icon. iOS masks a full-bleed square to
its own rounded shape, so the art may run to the edges. An Android *adaptive* icon is a 108 dp layer
of which the launcher shows a centre of roughly 72 dp, cut to whatever shape it likes - a circle on
a Pixel, a squircle elsewhere - and moves the layer around a little when it animates. Only the
centre 66 dp is guaranteed to stay visible. So the art is scaled into that safe zone and the rest
of the layer is filled with the green of its own border, feathered into it; the detection brackets,
which sit near the corners of the iOS art, then survive a circular mask.

    python3 src/tools/make_android_icon.py

Writes, under src/app/android/:
  src/app/android/app/src/main/res/mipmap-<density>/ic_launcher_background.png   the art on its felt, 108 dp
  src/app/android/app/src/main/res/mipmap-<density>/ic_launcher_monochrome.png   a white silhouette for themed icons
  store/icon-512.png                                             Play Store icon, 512 x 512, full bleed
  store/feature-graphic.png                                      Play Store feature graphic, 1024 x 500

The layers are wired up in src/app/android/app/src/main/res/mipmap-anydpi-v26/ic_launcher.xml.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / "src" / "app" / "ios" / "Assets.xcassets" / "AppIcon.appiconset" / "icon-1024.png"
RES = REPO / "src" / "app" / "android" / "app" / "src" / "main" / "res"
STORE = REPO / "src" / "app" / "android" / "store"

LAYER_DP = 108          # an adaptive icon layer
SAFE_DP = 66            # the part every launcher mask keeps
DENSITIES = {"mdpi": 1.0, "hdpi": 1.5, "xhdpi": 2.0, "xxhdpi": 3.0, "xxxhdpi": 4.0}


def border_colour(art: Image.Image) -> tuple[int, int, int]:
    """The mean colour of the art's outermost ring - the green the layer is extended with."""
    rgb = art.convert("RGB")
    w, h = rgb.size
    pixels = [rgb.getpixel((x, y)) for x in range(0, w, 8) for y in (0, h - 1)]
    pixels += [rgb.getpixel((x, y)) for y in range(0, h, 8) for x in (0, w - 1)]
    return tuple(round(sum(p[i] for p in pixels) / len(pixels)) for i in range(3))


def feathered(art: Image.Image, feather: int) -> Image.Image:
    """The art with an alpha ramp over its outer edge, so it melts into the plain fill around it."""
    mask = Image.new("L", art.size, 0)
    ImageDraw.Draw(mask).rectangle([feather, feather, art.width - 1 - feather, art.height - 1 - feather], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(feather / 2))
    out = art.convert("RGBA")
    out.putalpha(mask)
    return out


def on_felt(art: Image.Image, size: tuple[int, int], art_side: int) -> Image.Image:
    """The art, scaled to `art_side` and centred on a canvas of the border green."""
    canvas = Image.new("RGBA", size, border_colour(art) + (255,))
    scaled = art.convert("RGB").resize((art_side, art_side), Image.LANCZOS)
    piece = feathered(scaled, max(2, art_side // 14))
    canvas.alpha_composite(piece, ((size[0] - art_side) // 2, (size[1] - art_side) // 2))
    return canvas.convert("RGB")


def silhouette(art: Image.Image) -> Image.Image:
    """White where the art has card or bracket, transparent elsewhere - what a themed icon tints."""
    rgb = art.convert("RGB")
    mask = Image.new("L", rgb.size, 0)
    src, dst = rgb.load(), mask.load()
    for y in range(rgb.height):
        for x in range(rgb.width):
            r, g, b = src[x, y]
            bracket = g > 150 and g > r + 60 and g > b + 30
            card = (0.299 * r + 0.587 * g + 0.114 * b) > 165 and abs(r - g) < 70
            if bracket or card:
                dst[x, y] = 255
    # Close the small gaps the print on the card leaves; the heart stays a heart-shaped hole.
    mask = mask.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.MinFilter(9))
    out = Image.new("RGBA", rgb.size, (255, 255, 255, 0))
    out.putalpha(mask)
    return out


def main() -> int:
    art = Image.open(SOURCE)
    # Everything is built once at a size above xxxhdpi and scaled down, which keeps the edges smooth.
    master_px = 1024 * LAYER_DP // SAFE_DP
    background = on_felt(art, (master_px, master_px), 1024)

    mono_master = Image.new("RGBA", (master_px, master_px), (255, 255, 255, 0))
    mono = silhouette(art)
    mono_master.alpha_composite(mono, ((master_px - 1024) // 2, (master_px - 1024) // 2))

    for name, factor in DENSITIES.items():
        px = round(LAYER_DP * factor)
        folder = RES / f"mipmap-{name}"
        folder.mkdir(parents=True, exist_ok=True)
        background.resize((px, px), Image.LANCZOS).save(folder / "ic_launcher_background.png", optimize=True)
        mono_master.resize((px, px), Image.LANCZOS).save(folder / "ic_launcher_monochrome.png", optimize=True)
        # The pre-Oreo bitmap is never used: minSdk is 29 and the adaptive icon always wins.
        legacy = folder / "ic_launcher.png"
        if legacy.exists():
            legacy.unlink()
        print(f"wrote mipmap-{name} ({px} px)")

    STORE.mkdir(parents=True, exist_ok=True)
    # Play wants the full square and rounds it itself, the same contract as the App Store.
    art.convert("RGB").resize((512, 512), Image.LANCZOS).save(STORE / "icon-512.png", optimize=True)
    on_felt(art, (1024, 500), 500).save(STORE / "feature-graphic.png", optimize=True)
    print("wrote store/icon-512.png and store/feature-graphic.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
