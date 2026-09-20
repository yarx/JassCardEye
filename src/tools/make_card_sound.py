"""Makes the sound the iOS app plays when a card is counted: src/app/ios/Sounds/card-tick.caf.

A soft wooden tick rather than a beep. It plays up to 36 times in half a minute, so it has to stay
pleasant on the tenth repetition: no sharp attack (a 3 ms fade-in takes the click off the onset), a
low, round body instead of a bright ping, and gone within a tenth of a second.

The body is a wood block's few damped resonances - a fundamental and two quieter, faster-decaying
partials - with a whisper of filtered noise for the contact of the card. Synthesised instead of
recorded so it has no licence attached and can be tuned here and rebuilt:

    python3 src/tools/make_card_sound.py

Needs numpy, and afconvert (part of macOS) for the CAF container. The Android app plays the same
tick as src/app/android/app/src/main/res/raw/card_tick.ogg, which is converted from this file by
hand; the script does not write it.
"""
import subprocess
import tempfile
import wave
from pathlib import Path

import numpy as np

RATE = 44_100
LENGTH_S = 0.11
PEAK_DBFS = -6.0  # headroom: the in-app slider scales it down from here, never up

# (frequency Hz, decay time constant s, relative amplitude)
MODES = [
    (740.0, 0.024, 1.00),
    (1_870.0, 0.011, 0.38),
    (3_050.0, 0.006, 0.14),
]


def tick() -> np.ndarray:
    t = np.arange(int(RATE * LENGTH_S)) / RATE
    body = sum(a * np.exp(-t / tau) * np.sin(2 * np.pi * f * t) for f, tau, a in MODES)

    # Contact noise: 4 ms of noise, smoothed so it has no hiss, well below the body.
    rng = np.random.default_rng(7)
    noise = rng.standard_normal(t.size) * np.exp(-t / 0.004)
    kernel = np.hanning(9)
    noise = np.convolve(noise, kernel / kernel.sum(), mode="same") * 0.12

    signal = body + noise
    # Raised-cosine fade-in over 3 ms: the attack a wooden tick has, not the click of a hard edge.
    fade_in = int(RATE * 0.003)
    signal[:fade_in] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, fade_in))
    # And a 10 ms fade-out, so the end of the buffer is silence rather than a cut.
    fade_out = int(RATE * 0.010)
    signal[-fade_out:] *= np.linspace(1, 0, fade_out)

    return signal / np.max(np.abs(signal)) * 10 ** (PEAK_DBFS / 20)


def main() -> None:
    out = Path(__file__).resolve().parents[2] / "src" / "app" / "ios" / "Sounds" / "card-tick.caf"
    samples = (tick() * 32_767).astype("<i2")
    with tempfile.TemporaryDirectory() as tmp:
        wav = Path(tmp) / "tick.wav"
        with wave.open(str(wav), "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(RATE)
            w.writeframes(samples.tobytes())
        # Uncompressed linear PCM: decoding nothing is what lets the first play start on time.
        subprocess.run(["afconvert", "-f", "caff", "-d", "LEI16@44100", "-c", "1", str(wav), str(out)], check=True)
    print(f"{out}  {samples.size / RATE * 1000:.0f} ms")


if __name__ == "__main__":
    main()
