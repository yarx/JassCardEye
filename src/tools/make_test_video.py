"""Renders a test video for the simulator and the emulator: twelve French cards laid one by one onto a
pile on a green cloth, drawn from the card scans in data/cards.

The iOS simulator and the Android emulator have no camera, so both apps count a looping video there
(see "Testing in the simulator" in src/app/ios/README.md and "Testing in the emulator" in
src/app/android/README.md). A filmed pile is the honest test; this one is for the store screenshots,
which need a pile that is counted the same way on every run and in every language. Each card slides in
for 0.4 s and then lies still for 2.6 s, long enough for the stability rule at 3 frames on a slow
emulator. 1080 × 1080, H.264, 37 s.

    python3 src/tools/make_test_video.py output/test-video.mp4

Needs Pillow and ffmpeg on the path.
"""
from __future__ import annotations

import argparse
import random
import subprocess
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
CARDS = ROOT / "data/cards/french"
SIZE, FPS = 1080, 30
PILE = ["hearts/jack", "spades/ace", "hearts/9", "clubs/10", "diamonds/king", "hearts/ace",
        "spades/queen", "clubs/8", "diamonds/10", "hearts/king", "clubs/ace", "spades/10"]


def cloth(rng: random.Random) -> Image.Image:
    """Green felt: seeded noise, softened, so every run weaves the same cloth."""
    noise = Image.frombytes("L", (SIZE, SIZE), rng.randbytes(SIZE * SIZE)).filter(ImageFilter.GaussianBlur(0.8))
    channels = [noise.point(lambda v, base=base, gain=gain: int(base + v * gain))
                for base, gain in ((20, 0.06), (95, 0.12), (45, 0.07))]
    return Image.merge("RGB", channels).filter(ImageFilter.GaussianBlur(1))


def card(name: str, angle: float) -> Image.Image:
    image = Image.open(CARDS / f"{name}.jpg").convert("RGB")
    height = 600
    image = image.resize((round(image.width * height / image.height), height), Image.LANCZOS)
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius=26, fill=255)
    image.putalpha(mask)
    return image.rotate(angle, expand=True, resample=Image.BICUBIC)


def compose(ground: Image.Image, pile: list[tuple[Image.Image, int, int]]) -> Image.Image:
    frame = ground.copy()
    for image, x, y in pile:
        shadow = Image.new("RGBA", image.size, (0, 0, 0, 0))
        shadow.putalpha(image.getchannel("A").point(lambda a: int(a * 0.45)))
        shadow = shadow.filter(ImageFilter.GaussianBlur(10))
        frame.paste(shadow, (x + 10, y + 14), shadow)
        frame.paste(image, (x, y), image)
    return frame


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("output", type=Path, help="the .mp4 to write")
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)

    # Seeded, so every run lays the same pile at the same angles.
    rng = random.Random(4)
    ground = cloth(rng)
    ffmpeg = subprocess.Popen(
        ["ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{SIZE}x{SIZE}",
         "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", str(args.output)],
        stdin=subprocess.PIPE)
    assert ffmpeg.stdin is not None

    def emit(frame: Image.Image, count: int) -> None:
        data = frame.tobytes()
        for _ in range(count):
            ffmpeg.stdin.write(data)

    pile: list[tuple[Image.Image, int, int]] = []
    emit(compose(ground, pile), FPS)                        # a second of empty cloth
    for name in PILE:
        image = card(name, rng.uniform(-14, 14))
        x = (SIZE - image.width) // 2 + rng.randint(-50, 50)
        y = (SIZE - image.height) // 2 + rng.randint(-40, 40)
        start_x, start_y = x + rng.choice([-1, 1]) * 700, y + 500
        for step in range(1, 13):                           # sliding in from below
            t = step / 12
            emit(compose(ground, pile + [(image, round(start_x + (x - start_x) * t),
                                          round(start_y + (y - start_y) * t))]), 1)
        pile.append((image, x, y))
        emit(compose(ground, pile), round(FPS * 2.6))       # lying still until it is counted
    ffmpeg.stdin.close()
    return ffmpeg.wait()


if __name__ == "__main__":
    raise SystemExit(main())
