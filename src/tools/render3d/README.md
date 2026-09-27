# 3D counting sequences (src/tools/render3d)

Renders what a phone sees at the table while cards are counted: a hand carries a card over the pile,
lowers it and lets go, the card falls the last centimetre and settles, and the phone shakes a little in
the other hand. Blender renders it, with hard shadows from a lamp - of the hand and of the phone - and
real motion blur. Every frame gets its label from the scene itself. Why this exists, what it showed and
how it relates to the 2D generator is in "3D counting sequences" in
[context/architecture/data-pipeline.md](../../../context/architecture/data-pipeline.md).

| File | What it does |
|---|---|
| `scene.py` | Decides what a seed means: cards, pile, the laid card's path, the hand, table, light, camera and its shake. Pure Python, no Blender. |
| `render_table.py` | Runs inside Blender: builds the scene `scene.py` resolved, renders the frames asked for and writes their labels. |
| `make_dataset.py` | Renders a dataset of many sequences in the layout of the 2D generator. |
| `test_scene.py` | Checks `scene.py` without Blender; CI runs it. |

## Prerequisites

Blender 4.2 or newer - it was built and measured with **Blender 5.2.1 LTS**. `make_dataset.py` and the
Dataset Tool look for `$BLENDER`, then `/Applications/Blender.app`, then `blender` on the path. Nothing
else: the scripts use Blender's own Python and the standard library, and the card scans in
`data/cards`.

## A dataset

```bash
python3 src/tools/render3d/make_dataset.py --count 3000 --engine eevee --out output/dataset3d
src/training/.venv/bin/python src/tools/check_dataset.py output/dataset3d
src/training/.venv/bin/python src/training/train.py --variant c --train-data output/dataset3d
```

| Option | Default | |
|---|---|---|
| `--count` | required | images to write |
| `--out` | `output/dataset3d` | replaced unless `--keep` |
| `--seed` | `1` | seed and count decide every scene |
| `--frames-per-sequence` | `10` | frames taken from each sequence, chosen from the seed |
| `--engine` | `cycles` | `eevee` renders about ten times faster, `cycles` traces the light |
| `--samples` | `32` | per pixel; 16 is plenty for EEVEE |
| `--blender` | found | the Blender binary |

Each sequence lays one card. The deck alternates, so half the sequences are French and half German, like
a 2D run; the pile holds 0-34 cards, a hand lays the card in 90 % of the sequences, and the camera is held
by hand in 70 %, steady in 15 % and sweeps sideways in 15 %. The rest - table, light, tilt, distance -
is drawn per sequence.

The output is `images/`, `detect/labels/`, `locate/labels/` and `classes.txt`, the source of truth of a
generated dataset; at the end the script runs the CLI's `rebuild`, which derives the classification
folders and `data.yaml` from it as for any dataset. `render3d.jsonl` records every chosen frame - what it
was and why, how long it took, on which device - and `render3d-summary.json` adds it up.

## A job, as the Dataset Tool writes it

```bash
blender --background --factory-startup --python src/tools/render3d/render_table.py -- job.json
```

```json
{
  "cards_root": "data/cards",
  "output": "output/simulate",
  "mode": "preview",
  "engine": "eevee",
  "samples": 16,
  "sequences": [
    { "stem": "french-8-seed3",
      "params": { "deck": "french", "played": 8, "laid": 1, "table": "random", "light": "random",
                  "hand": true, "camera": "handheld", "seed": 3, "fps": 30, "size": 640 },
      "frames": null }
  ]
}
```

`frames` lists the frames to render, `null` meaning all of them. In `preview` mode the frames go to
`output/<stem>/NNN.jpg` with a `frames.json` beside them; in `dataset` mode to `output/images/` with their
label files, and the ambiguous frames are left out without being rendered.

`params` are those of the *Simulate table* page: `deck` (`french`, `german`), `played` (cards already on
the pile), `laid` (cards laid during the sequence, 1.6 s each), `table` (`felt`, `solid`, `speckle`,
`checker`, `tartan`), `light` (`lamp`, `diffuse`, `hdri`), `hand`, `camera` (`handheld`, `steady`,
`sweep`), `seed`, `fps` and `size`. `random` draws a value from the seed.

## What a frame says

`frames.json` holds one record per frame:

| Field | |
|---|---|
| `kind` | `positive`, `negative` or `difficult` |
| `reason` | why: `lies still and free`, `covered`, `smeared by motion`, `cut off by the border`, `touches the border`, `partly covered`, `blurred`, `no card on the table` |
| `card`, `class_id` | the card on top of the pile - a laid card counts once it has settled |
| `corners` | its four corners in pixels: top left, top right, bottom right, bottom left of the scan |
| `visibility` | share of its face the camera sees, the least over the open shutter |
| `in_frame` | share of its bounding box inside the picture |
| `travel`, `motion` | how far its corners move while the shutter is open, in card heights: `still`, `slight`, `blurred`, `moving` |
| `flying_card` | a laid card is in the air |

The rules are the 2D generator's: a positive lies 3 px clear of the border, at least 95 % visible and
smeared by no more than 0.09 card heights; at most 85 % in frame, at most 70 % visible or smeared by 0.25
or more is a negative; everything between is `difficult` and stays out of a dataset.

## Speed

Measured on an Apple M4 Max (40 GPU cores) at 640 × 640, one card laid per sequence:

| Renderer | Rendering alone | With start-up and labels | |
|---|---|---|---|
| EEVEE, 16 samples | 9.9 frames/s | 7.9 frames/s | what the datasets were rendered with |
| Cycles on Metal, 32 samples, denoised | 1.0 frames/s | - | closer to a photo in reflections and soft light |

The first frame of a Blender process takes a few seconds for EEVEE and about a minute and a half for
Cycles on Metal, which compiles its kernels once and keeps them. On an A100 nothing has been measured
yet: by Blender's benchmark database (Cycles, Blender 4.5, median scores) an A100 scores about 3600-4200,
an M4 Max with 40 GPU cores 5270, so Cycles would run at 0.7-0.8 frames/s there. How to measure it on a
pod is below.

## On a GPU pod

The training image carries no Blender. On a pod - Linux with an NVIDIA GPU - Blender is fetched as it is
published and run in the checkout:

```bash
curl -L https://download.blender.org/release/Blender5.2/blender-5.2.1-linux-x64.tar.xz | tar -xJ -C /opt
BLENDER=/opt/blender-5.2.1-linux-x64/blender python3 src/tools/render3d/make_dataset.py --count 300 --engine eevee --out /tmp/eevee
BLENDER=/opt/blender-5.2.1-linux-x64/blender python3 src/tools/render3d/make_dataset.py --count 300 --engine cycles --out /tmp/cycles
```

`render3d-summary.json` of each run says how fast it was and on which device. Cycles picks OptiX, then
CUDA; EEVEE needs the GPU's EGL, which NVIDIA's driver brings.
