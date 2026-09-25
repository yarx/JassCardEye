# Data pipeline – synthetic training data

> How labelled images for recognising the topmost card are made: rendered from 72 real card scans –
> the French-suited deck and the German-suited one, 36 cards each – and joined by real photos in the
> same format. Implementation: `src/tools/dataset/Generation`. How to run the CLI and the dataset
> tool, with every command, option and default: [src/tools/dataset/README.md](../../src/tools/dataset/README.md).

## Inputs

| Folder | Content | Used for |
|---|---|---|
| `data/cards/{deck}/{suit}/{rank}.jpg` | the 72 card scans | the cards of every rendered scene |
| `data/negatives/` | 25 photographs of card-free everyday scenes | a quarter of the negatives |
| `data/real/val/` | hand-labelled phone photos and video frames | validation only, never training |

The scans are 1710×2670 pixels – the proportion of a real Jass card (57×89 mm) – JPEG at quality 75
with 4:2:0 subsampling and no colour profile or metadata. The French deck is AGMüller's standard deck,
the German one its Schaffhauser Spielkarten «Opti-Quatro». Folder and file names are the tokens of the class
labels: `french`/`german`, `clubs` … `shields`, `6` … `10`, `jack`, `queen`, `king`, `ace`. Four
German shields cards are white-balanced to the paper white of the others, because a colour cast must
not become the mark of a single card. All 72 scans are
decoded before rendering starts, so a missing file stops a run at once.

`data/negatives/` holds square 1024-pixel JPEGs; `src/tools/import-negatives.sh` converts new phone
photos, HEIC included, to that format.

## Scene layout (layers back to front)

1. **Background** – table surface or Jass mat, generated for every image (`TableTexture`): a uniformly
   repeating pattern at a card-appropriate tile size – solid colour, felt, speckle, checker or tartan –
   in random cloth colours, never "the whole mat" with a frame. It lies as a tiling texture on the
   **table plane (z=0)** and is projected with the **same camera** as the cards, so it gets the
   identical tilt and vanishing point. Own photos can be passed instead (`--backgrounds`); they are
   drawn flat and do not follow the tilt.
2. **0..35 underlying cards** – distinct cards from the rest of *the same deck*, scattered around the
   centre and rotated in the table plane. The maximum of 35 plus the top card is exactly one deck. A
   pile is never mixed: a Jass is played with the French deck or the German one, so a frame showing
   both is a scene that cannot occur, and the renderer refuses to draw one.
3. **Topmost card** – drawn last, on top of all others, and placed off-centre on purpose (see *Label
   variants*). **It alone is labelled.**

All cards lie **parallel to the table plane**; the spatial effect comes from the camera perspective
alone. Each card sits one **card thickness** (0.3 mm, the real measure) above the one below, so a full
deck stands about a centimetre tall – visible as parallax on a heap and as a band of cut edges on a
squared deck.

The cards have **rounded corners**: the scan is drawn into a rounded-rectangle mask, and once the card
is warped in perspective its transparent corners reveal the card underneath.

Depending on the camera distance the pile fills the image to different extents. Close up, widely
scattered underlying cards extend beyond the image – like a phone held by hand, which does not always
capture everything.

### Squared deck

In 5 % of the frames (`--aligned-stacks`) the pile is a squared deck instead of a scattered heap: the
cards lie flush with a little residual offset and rotation, only the top card shows its face, and the
cut edges of the cards below form a band under it. Only the bottom card casts a contact shadow – 36 of
them would stack into a black ring. The share is small because cards are thrown onto the pile in play;
a squared deck has to be covered, not learned as the norm. Whether a frame shows one is drawn from the
image's own random stream, and turning the heap into a deck consumes no further random numbers, so
every other frame stays exactly as it would be without the squared decks.

### Deck and camera tilt: computed from the index, not drawn

Two properties of a scene are **not drawn** but computed from the image's index and the run's count,
in `RunPlan`: the camera tilt and the deck. Both have to come out exact rather than right on average.
They are handed to the scene alongside the other parameters, so a scene that is drawn again (see *Only
a completely visible card is labelled*) keeps them.

**Tilt.** Image *i* of a run of *n* is tilted `60° × (p(i) + ½) / n` away from the vertical, where `p`
permutes the indices. Every image has its own angle, neighbouring angles are a constant step apart,
and any part of the range holds frames in proportion to its width – at any count. Flat views are the
hardest case for the model, so they must not be rarer than steep ones. The permutation keeps a run from
being sorted by angle: the first few hundred images – what anyone looks at when checking a run by eye –
already span the whole range. Its step is derived from the count (about n/φ, sharing no factor with
n), because a fixed step is congruent to 1 for some counts and would sort the run after all.

**Deck.** Even positions of the same permutation are French, odd ones German. Each deck therefore gets
exactly half the run *and* an even sweep of its own, every second step of the full one. A coin flip
would leave the halves unequal, and a deck chosen independently of the sweep could end up with more
flat frames than the other. A standard run of 200 000 images gives each deck 100 000.

**Why 60°.** It is the flattest view the renderer draws correctly. The horizon enters the frame at
about 90° minus half the field of view, which is drawn from 45–55°: no frame shows it at 62°, some do
from 63° on. Once it is in view, `PlaneProjection.ForCamera` back-projects image corners that lie behind
the camera, and the table texture and the light are not drawn above the horizon – the frame comes out
white there. Raising the ceiling means fixing that first.

The CLI's `plan` command prints deck and tilt for every image of a run without rendering anything, and
`src/tools/check_run_plan.py` checks the plan in CI – asking the generator for the range and the deck
names rather than knowing them.

## Lighting

For realistic, camera-dependent light and shadow (`SceneLight`):

- **Table-bound falloff**: a light creates a brighter area around `GroundCenter` that falls off towards
  the `Ambient` residual brightness. Like the background, it is applied on the table plane and
  projected with the same camera, so it tilts along in perspective.
- **Two lighting moods**, drawn per scene, because a table is lit in two ways in practice: a **single
  lamp** – a pool of light and a clear shadow – in about 65 % of the scenes, and **diffuse** light from
  an overcast sky or a bright room in the rest, with a falloff radius of 4.5–8 and a residual brightness
  of 0.82–0.97, so practically even. 30 % of the lamp-lit scenes get a **second, brightening source**
  from roughly the opposite side; it only adds light, which is what a fill light does.
- **The light centre is drawn from a wide disc** (radius 2.6 world units, a card being 1 unit tall), so
  the bright spot can sit beside the pile, behind it or outside the frame. Light that always fell where
  the camera looks would give every image the same frontal lighting – a pattern the model could learn
  and the world does not have.
- **A subtle sheen** at the light centre.
- **Soft contact shadows** under each card, offset away from the light and blurred. Offset and blur
  follow `ShadowSoftness`: a lamp throws the shadow clearly to one side, diffuse light leaves little
  more than a soft darkening under the card. A top card smeared by strong motion is in the air and
  casts none.
- **Exposure** over the whole image, then a gentle **vignette** darkening the image corners, as a lens
  does.

## Camera model

A **pinhole camera** (`PinholeCamera`) looks from a random position above the table at a target point
near the centre. For each card the four world corners are projected, and the card image is warped
exactly into the projected quad via a **homography** (`Homography`, a 4-point perspective map →
`SKMatrix`). Different camera positions thus simulate holding the phone over the table from varying
angles and distances.

## Parameters

Bundled in `GenerationParameters`: `null` means draw randomly, a set value is used as is. The random
ranges live in `RandomizerOptions`. Deck and tilt come from `RunPlan` instead (see above).

| Parameter | Meaning | Default range |
|---|---|---|
| deck (`ScenePlan`, not a parameter) | Which deck the whole pile is dealt from | not drawn: half the run each |
| `TopCard` | Topmost (target) card | uniform over that deck's 36 |
| `UnderCardCount` | Number of underlying cards | 0..35 |
| `AlignedStack` | Squared deck instead of a scattered heap | 5 % of the frames |
| `Camera` | Camera pose in 3D | azimuth 0–360°, field of view 45–55°, target within ±0.08 of the centre; tilt not drawn: 0–60° from `RunPlan` |
| `CameraDistance` | Camera proximity (smaller = closer) | 1.8 (near) – 4.2 (far) |
| `Exposure` | Exposure factor (1 = neutral) | 0.8–1.2 |
| `Light` | Mood, centre, falloff, residual brightness, shadow, fill light | centre within 2.6; lamp (65 %): radius 1.4–3.6, ambient 0.45–0.78; diffuse (35 %): radius 4.5–8, ambient 0.82–0.97; shadow strength 0.30–0.55 (×0.55 when diffuse); fill light on 30 % of the lamp scenes |
| `Background` | Table surface | generated pattern; with `--backgrounds`, a photo for 85 % of the frames |
| `ImageSize` | Edge length of the square image | 640 |

Further values in `RandomizerOptions`: `CardThicknessMillimeters` (0.3), `UnderCardRadius` (0.55, the
scatter of the heap), `TopCardJitter` (0.32), and `AlignedStackRadius` (0.014) with
`AlignedStackRotationDegrees` (2.5) for the residual mess of a squared deck.

Not modelled: tilted or standing cards, a hand in the frame, and shadows cast by anything but the cards.

## Label variants (three approaches A/B/C)

From **one** render run – the same images – all approaches are written as labels, so a comparison
differs only in the label format and the model. `--tasks` selects them; the default is all.

| Variant | `--tasks` | Idea | Output |
|---|---|---|---|
| **C** | `c` / `detect` | Detector with 72 classes: box and value in one step | `detect/` (box `classId cx cy w h`) |
| **B** | `b` (= `locate` + `classify-crop`) | Oriented localisation (OBB, 1 class) → rectify → classify | `locate/` (OBB `0 x1 y1 … x4 y4`) + `classify_crop/` (rectified card crop) |
| **A** | `a` / `classify-full` | Classification of the whole image, no box | `classify_full/` (whole image per class) |

All labels refer solely to the **topmost card**. C uses an **axis-aligned** box: single-stage and
mobile-friendly. B uses an **oriented box (the 4 corner points, in order)** in stage 1. That lets the
rotated card be cropped precisely and rectified in perspective into an upright 246×384 card image
(`CardCrop`) that matches the format of the single-card scans – the real value of the two-stage
variant. Stage 1 is trained as a YOLO-OBB model (`src/training/train.py --variant b1`), stage 2 as an
image classifier on the crops (`--variant b2`).

The thesis investigates A, B and C on the same data. After the thesis only C is pursued: A and B stay
in the code and remain trainable, but they are not used in the apps.

**Off-centre topmost card:** `TopCardJitter` is deliberately large (0.32), so the top card does not
systematically lie in the centre – otherwise variant A learns "centre = answer". It also makes the
localisation of B and C more robust.

## Only a completely visible card is labelled

A card cut off by the image border cannot be annotated correctly: two of its corners lie outside the
frame, so the oriented box and the crop would be wrong, and the axis-aligned box would enclose only the
visible part rather than the card. The generator decides by projecting the top card's corners, without
rendering anything:

| Top card | Result |
|---|---|
| completely inside the frame, 3 px clear of the border (`FullyVisibleMarginPixels`) | **positive**, labelled |
| at most 85 % visible (`MaxVisibleForNegative`, measured on its bounding box) | **negative** |
| in between – nearly complete but slightly clipped | *ambiguous*: the scene is drawn again, keeping deck and tilt; after 16 ambiguous scenes the frame becomes a negative |

The middle band is skipped on purpose: labelling a 99 %-visible card as a negative would put almost
identical images into opposite classes.

**Real photos follow the same rule.** A card that is clearly cut off by the border is saved
with **"No card (N)"** – a negative, as the generator files it. A card that is nearly complete and only
touches the border is the ambiguous band and does not enter the dataset: a new photo like that is not
saved, and a sample already in the dataset is removed with **"Delete sample"** – the counterpart of the
generator drawing the scene again. The dataset tool warns when a marked card touches the border.

## Negatives (no card)

About 30 % of the images (`--negatives`, default 0.3) are **negatives**: frames in which no card should
be recognised.

- **Empty table** – a quarter of the rendered negatives: background only.
- **Thrown top card** – 40 %: the pile's own top card smeared in mid-motion (`Scene.TopCardMotion`).
  The model only ever detects the single topmost card, so a blurred top card means "no valid top card"
  and therefore no detection.
- **Covering card** – 35 %: a separate blurred card (`Scene.Flying`) from the same deck passes over the
  pile, a large part of it in frame, covering a substantial part of the top card. When no such
  placement is found, the top card is thrown instead.
- **Clipped top card** – a frame whose top card is clearly cut off by the border (see above) is a
  negative whatever the draw.
- **Real photographs** – a quarter of all negatives, see below.

**Motion blur** models a card that moved while the shutter was open: the warped card is rendered once
and then drawn many times along the motion vector, so the overlapping copies accumulate to an *opaque
core* with translucent leading and trailing edges. The card keeps its shape and presence while its
detail smears – what a thrown card looks like in a video frame, rather than a faint ghost. The travel is
a multiple of the card's own **projected height**, so the blur scales with how large the card appears.

**Blur is graded, not binary**:

| Travel (× card height) | Look | Label |
|---|---|---|
| 0.02 – 0.09 | slightly smeared, easy to read at a glance | **positive** (`--slight-blur`, 0.3 of the positives) |
| 0.09 – 0.25 | ambiguous | *deliberately not generated* |
| 0.25 – 0.65 | not reliably readable | **negative** |

The gap between the bands avoids ambiguous frames, and the slightly blurred positives stop the model
from equating "any blur" with "no card". A slightly blurred top card still rests on the pile and keeps
its contact shadow; a strongly smeared one is treated as being in the air.

**Counter-case (still a positive):** a blurred card that only clips in **from the border** while the
top card stays clearly visible must still be detected. A share of the positives (`--border-flying`,
0.25) is generated this way, alternating between a narrow sliver (5–35 % of the card in frame) and a
larger portion (30–70 %). Placement is checked geometrically: the border case allows at most 5 %
overlap with the top card, the covering case needs at least 30 %. A card at the border is blurred by
0.04–0.30 card heights, a covering one by 0.12–0.40.

Per variant: **C (detect)** and **B₁ (locate)** get **no label file** – Ultralytics treats a label-less
image as background – so **B** abstains through its stage-1 detector; **A (classify_full)** files the
image under a `none` class, so A has 73 classes (72 cards + none). No crop is produced.

These negatives teach the model to abstain on blurred and transitional frames. The real-world share of
"no card" frames does **not** have to be matched in training; that is handled at inference by a **high
confidence threshold**. Training only needs enough hard negatives next to clean positives.

### Real photographs as negatives

A quarter of the negative frames (`--real-negative-share`) are not rendered but cut from photographs in
`data/negatives/`: card-free everyday scenes – paper, receipts, blister packs, boxes, tables, rooms.
Rendered negatives only ever show the generated table, so without these the detector never sees the
rest of the world and reads any bright rectangle on a darker surface as a card. They need no labels:
for a detector a negative is simply an image without a box.

Each use takes a random square section (55–100 % of the shorter edge) of a random photo, in one of eight
orientations and with the exposure range of the rendered scenes, so a couple of dozen photos cover
thousands of frames. Mirroring is allowed here although it is ruled out for cards: a mirrored bathroom is
still not a card, while a mirrored card is a different card. The folder is used automatically when it
exists and is versioned like the card scans.

## Output (Ultralytics layout)

The dataset lands in the git-ignored `output/dataset/` by default. Training data is never checked in; it
is regenerated for every training run. Generated images are named by their index; at the
default JPEG quality 90 an image takes about 65 KB, so a standard run of 200 000 images takes 12–13 GB.

```
output/dataset/
├── images/NNNNNN.jpg              # the shared 640×640 images, rendered once
├── classes.txt                    # the 72 class names (from JassCardEye.Dataset.Cards)
├── detect/                        # variant C: detector, 72 classes
│   ├── images/                    #   hard links to the shared images (no extra storage)
│   ├── labels/NNNNNN.txt          #   classId cx cy w h
│   └── data.yaml                  #   nc: 72; train and val = images; absolute path
├── locate/                        # variant B, stage 1: OBB detector, 1 class
│   ├── images/                    #   hard links to the shared images
│   ├── labels/NNNNNN.txt          #   0 x1 y1 x2 y2 x3 y3 x4 y4 (corners in order)
│   └── data.yaml                  #   nc: 1 (card); trained as an OBB model
├── classify_full/                 # variant A: classify the whole image
│   ├── train/<class>/NNNNNN.jpg   #   hard link of images/NNNNNN.jpg ('none' = negatives)
│   └── val/                       #   hard-link mirror of train/
└── classify_crop/                 # variant B, stage 2: classify the rectified card
    ├── train/<class>/NNNNNN.jpg   #   rectified crop, 246×384
    └── val/                       #   hard-link mirror of train/
```

Shared images are referenced via **hard link**, so they cost no extra storage, and are **copied** where
hard links are unavailable – on a different volume, where every image then takes about five times the
space. Symbolic links are deliberately not used: Ultralytics resolves them before deriving the label
path from the image path, so an image reached through a link would be looked up in the wrong `labels/`
folder and every frame would silently count as background – and a class folder would lose its class
name the same way.

Inside one dataset, `train` and `val` hold the same data. Training never relies on that:
`src/training/train.py` trains on the synthetic dataset and validates on `data/real/val`, and builds the
`data.yaml` and class folders for that pair itself.

### What is version-controlled, and what is rebuilt

Only the **source of truth** of a dataset is committed – plain files, with no links and no absolute
paths:

```
images/            the photos
detect/labels/     class + axis-aligned box
locate/labels/     oriented box (the four corners, in order)
classes.txt
```

Everything else is **derived and git-ignored**: `classify_full/`, `classify_crop/`, the `detect/images`
and `locate/images` folders, and the `data.yaml` files. Two reasons:

- The classification folders and the per-variant `images/` folders are **hard links**. Git does not
  know about hard links and would store each one as a full copy, inflating the repository several
  times over.
- `data.yaml` carries an **absolute path**, which is valid only on the machine that wrote it.

The CLI's `rebuild` command recreates the derived parts after a checkout. It starts from scratch: it
deletes the classification folders, files every image without a detect label under `none`, re-derives
the class folders from the detect labels and the rectified crops (JPEG, 384 pixels high) from the
oriented boxes, recreates the hard-link folders, and writes a `data.yaml` with the correct local path
for every variant whose labels exist.

## Real photos in the dataset

Real photos are annotated with the dataset tool (JassCardEye Dataset Tool, see the
[README](../../src/tools/dataset/README.md)): the four corners of the top card in order – top left, top
right, bottom right, bottom left – and its class as deck, suit and rank, or "No card". What matters for
the data:

- **One writer.** The tool writes through the same `VariantDatasetWriter` as the generator, so real and
  synthetic data are format-identical and can be combined. Non-square photos are brought to the same
  **640×640** by a centre crop, and the corners are transformed along with the image.
- **The name is the input file name**, e.g. `game_1_00005` for the fifth frame of `game_1.mp4`. It
  points back to the original photo, and writing it again replaces the earlier entry everywhere – box,
  oriented box, crop and class folder, including a switch between a card and "No card". Names have to
  be unique across sources by themselves; frames extracted by the tool carry their video's name.
- **A correction** of a sample already in the dataset writes only the two source-of-truth labels and
  then runs the same `rebuild` a training run performs. The image is not rewritten: it is
  version-controlled, and re-encoding it would change the file without changing the picture.
- **A card cut off by the border** follows the generator's rule, see *Only a completely visible card
  is labelled*.
- **Formats:** JPEG, PNG and BMP. HEIC/HEIF is not supported – Skia, used by the tool and the writer,
  has no decoder for it, and since every photo is re-encoded to JPEG for the dataset anyway, a native
  HEIF decoder would add a heavy dependency for nothing. Such photos are converted first.

**A label the models propose.** In *Label photos*, ⇧→ goes to the next photo and places
what variant B makes of it: B₁'s oriented box as the four corners, B₂'s reading of the rectified crop
as the class. It is a proposal and nothing more - it is drawn in orange until a corner is moved,
nothing is ever saved without **Enter**, and a photo already in the dataset keeps its stored label.
What holds it back is deliberate: no corners below a box confidence of **0.50**, no preselected class
below **0.60** (the corners stand, the class stays open), and never a card of the other deck. The
models see the same 640×640 square this writer stores, and the crop is rectified with the same
`CardCrop.Rectify` that produced B₂'s training crops, so neither stage is asked about a picture it was
never trained on.

Two things about it are worth keeping in mind. The **half turn is a guess**: B₂ scores a crop and the
same crop upside down at 0.999 and 1.000, because both decks are double-headed, so the tool takes
whichever way up the card stands on the photo. That costs little - see the paragraph below - and the
rectified preview shows it. And a proposal that a person waves through makes `data/real/val`, the set
these very models are measured on, a little more like the model. That cannot be prevented by being
careful, only counted: every sample saved after a proposal is written to `proposals.csv` beside
`viewer-settings.json`, with what was proposed, what was saved, and what became of the corners - taken
as they came, turned by half (the guess above), reordered, or really moved.

**Which variants a wrong corner order can damage: only B₂.** Worth knowing before hunting for one,
because it bounds the problem to a single folder:

- **Detect (C)** is immune – `YoloBox.FromCorners` takes the minimum and maximum over the four corners,
  and no order changes those.
- **Locate (B₁)** is immune too. Ultralytics converts an `xyxyxyxy` label with
  `cv2.minAreaRect(points)` (`ultralytics/utils/ops.py`, `xyxyxyxy2xywhr`), which depends on the set of
  points and not on their order.
- **`classify_crop` (B₂)** is the only one that cares: `CardCrop.Rectify` maps the first corner to the
  top left of an upright portrait frame, so the order alone decides how the card is turned.

That leaves two mistakes with very different visibility. Starting **one corner off (90°)** lays the
card's long edge along the crop's short edge – landscape squeezed into portrait, unmistakable. Starting
at the **opposite corner (180°)** merely turns the crop upside down. The class is still right, so this
is inconsistent training data for B₂ rather than a wrong label – but it is worth correcting, and it is
the one that slips through, because the pips of a low card look the same either way up. The face cards
and the banner do not. Only the oriented label keeps the corner order, which is why a label can be read
back and turned in the tool at all.

## The real validation set

`data/real/val` holds the hand-labelled real frames every variant is validated on. It is never
trained on: training uses synthetic images only, so a metric says how a model does on real piles, not
how well the renderer matches itself. There is no separate test set; the best epoch is chosen on this
set. Before a training run generates anything, `src/scripts/run_training.sh` rebuilds its
derived parts and checks it, so a bad label costs seconds rather than a run; CI does the same on every
push. Validation reports one metric over the whole set.

Every sample is named `jasscardeye_{image|video}_{french|german}_cards_{set}_{number}`: where it came
from, which deck it shows, which set it belongs to, and the frame number the extraction gave it - for
the stills, a count of their own. The sets are numbered in the order the sources were recorded, taken
from the videos' own capture date (`com.apple.quicktime.creationdate`) and the photos' EXIF. Card-free
photos that belong to no set are `jasscardeye_image_negatives_{number}`.

Its composition - 305 French cards, 208 German cards and 300 negatives:

| Group | Frames | With a card label | Negatives |
|---|---|---|---|
| `image_french_cards_1` – phone stills of French piles | 37 | 37 | 0 |
| `image_negatives` – card-free photos | 14 | 0 | 14 |
| `video_french_cards_1` … `_7` – frames of seven videos, French deck | 429 | 268 | 161 |
| `video_german_cards_1` … `_6` – frames of six videos, German deck | 333 | 208 | 125 |
| **Total** | **813** | **513** | **300** |

A sample carries the name of the photo it was written from, so the folders of extracted frames carry
these names as well: a set renamed here has to be renamed there too, or the next photo labelled out of
it comes back under its old name.

## Session recordings

*Session aufzeichnen*, one of the apps' hidden developer tools, records
a counting session as a video of the analysed square and, with it, a **recognition log**: a CSV with one
row per frame of the video, saying what the model made of that frame. The dataset tool's
**Analyse sessions** reads both, shows the session as a timeline and hands a frame worth keeping to
**Label photos** - the way a false alarm at a real table becomes a case in `data/real/val`.

```
frame,t_ms,label,confidence,x,y,w,h,committed
0,0.000,,,,,,,0
1,33.412,spades_6,0.9132,0.2104,0.1877,0.3321,0.4410,0
2,66.803,spades_6,0.9310,0.2111,0.1869,0.3314,0.4425,1
```

| Column | Meaning |
|---|---|
| `frame` | the frame's position in the video, in display order, counted from 0 without a gap |
| `t_ms` | its presentation time in the video in milliseconds, three decimals; the first frame is at 0 |
| `label` | the class label of the top detection, `suit_rank`; empty when the model saw no card of the deck in play |
| `confidence` | that detection's confidence, four decimals: the score of its best class, 0 to 1, the same on both platforms (see below); empty without a label |
| `x`, `y`, `w`, `h` | its box, normalised to the analysed square, origin top-left, four decimals; empty without a label |
| `committed` | 1 when this frame put a card on the pile, otherwise 0 |

Both apps keep these rules, and the tool refuses a log that breaks them rather than guessing:

- **Row n is frame n.** A frame the encoder does not take gets no row - iOS writes the row once the
  writer has accepted the frame, Android once the frame comes out of the encoder, matched by its
  presentation time. A log that skipped a row would pin every later detection on the wrong picture.
- **Every analysed frame, not only the hits.** The pattern - nothing, 6♠, 6♠, 8♠, 6♠ - is the
  information.
- **Frames are at least a millisecond apart**, so each can be found by its time: the tool lists the
  frames' times with ffprobe, compares them with `t_ms` (to half a millisecond), and fetches a frame by
  seeking just before it.
- The detection is the one the frame loop continued with, after cards of the other deck were dropped,
  so the log shows what the stability rule was given.

**The confidence is the best class's score on both platforms.** Android reads it from the model's
output, as Ultralytics does. On iOS, Vision's `observation.confidence` is the *sum* of the box's class
scores and its labels are those scores normalised to 1, so the app multiplies the two. Read directly,
the sum would put a box the model cannot decide on - two classes at 0.6 each - above 1: the highest
confidence exactly where it hesitates. The tool notes a log with confidences above 1 as not comparable.

With the log, both apps write the **session info**, `session_<time>_<discipline>.json`: what the session
was recorded on and with, so sessions from different devices can be compared. Keys are snake case and
sorted:

| Key | Meaning |
|---|---|
| `platform`, `system`, `device` | iOS or Android, its version, and the device - the machine identifier on iOS (`iPhone17,1`), manufacturer and model on Android |
| `app_version`, `app_build` | the app's version and build number |
| `model_variant`, `model_run` | the variant, and the training run its model came from (out of the bundled `models.json`) |
| `compute` | where the model runs: `Core ML, all compute units` on iOS, `GPU` or `CPU` on Android |
| `confidence_threshold` | the live threshold detections were filtered with |
| `stability_rule`, `stability_frames` | `run` or `majority`, and its number of frames |
| `deck`, `camera_lens`, `discipline` | the deck in play, the lens, and the discipline being counted - as a token that does not change with the app's language: the trump suit's token for the deck in play (`roses`), or the id of a discipline without trump (`obenabe`, `slalom.obe`) |
| `started_at` | when the recording started, UTC ISO 8601 |

In file names the discipline is that token with anything but letters and digits dropped (`slalomobe`).
When a recording ends, both apps pack its three files - `session_<time>_<discipline>.mov` (an `.mp4` on
Android), `.csv` and `.json` - into **`session_<time>_<discipline>.zip`**, with a folder of that name
inside that holds them. The ZIP lies in the iPhone app's Documents folder, and on Android in
`Documents/JassCardEye`, because MediaStore takes a file that is neither picture, video nor audio only
into Download or Documents. One file per session is what gets copied off the phone; the dataset tool
opens the ZIP and unpacks its folder beside it. If packing fails, the files are kept - on iOS together in
that folder, on Android on their own, the video in `Movies/JassCardEye` and log and info in
`Documents/JassCardEye` - and the note under the viewfinder says so.

What the tool marks as an **anomaly** is a heuristic, not ground truth
(`src/tools/dataset/Viewer/Sessions/SessionAnalysis.cs`):

- **Outlier** - a single frame naming another card inside a run of one card, with at least two frames
  on either side.
- **Ghost** - one or two frames naming a card in a stretch of nothing.
- **Back and forth** - two cards taking turns in runs of up to three frames, at least four runs long.
- **Count against its neighbourhood** - a card was counted while most of the four frames on either side
  named another.

## Consistency checks

- **`src/tools/check_dataset.py`** checks a dataset for the mistakes that silently poison training:
  every image is either a labelled card or a negative, never both and never neither; an image sits in
  exactly one classification folder; the class of a detect label matches the crop folder it was filed
  into; every labelled card has a crop; the detect and locate label sets match; and no labelled card
  touches the image border. It exits non-zero on any problem.
- **`src/tools/check_run_plan.py`** checks that the tilt sweep is exact and that each deck holds half of
  it, for a set of awkward counts, by asking the CLI's `plan` command.
- **`src/tools/dataset/Checks`** checks how the dataset tool reads a recognition log - what it
  accepts and what it refuses - the anomaly rules, and the rules a label proposal follows: the corner
  order of an oriented box, the way back from the stored square to the photo, and the thresholds. `src/tools/check_recorder.swift` checks on a Mac
  that the iOS recorder's rows describe its frames; `SessionRecorderTest` holds the Android rows to the
  same columns.
- **CI** (`.github/workflows/ci.yml`, Ubuntu) builds the solution, generates 300 images with seed 1,
  rebuilds `data/real/val`, and runs both dataset checks on the generated dataset and on the real set,
  and the dataset tool's checks.
  `src/scripts/run_training.sh` runs `check_dataset.py` on the real set before generating and on the
  generated dataset afterwards.

## Reproducibility and speed

A run is deterministic from `--seed` and `--count`: the same seed, count, code and card scans give an
identical dataset, bit for bit, on any machine. That is why datasets are regenerated for every training
run rather than stored.

- **One random stream per image.** Each image draws from its own stream, seeded from (seed, index) with
  a splitmix64 step – never from a stream shared by the run, and never from a framework hash, which is
  randomised per process. Image *i* therefore does not depend on the images before it.
- **The count is part of the identity.** Tilt and deck come from `RunPlan.For(i, count)`, so a dataset
  is **not** prefix-stable: the first 500 images of a 5000-image run are not those of a 500-image run.
  Datasets of different sizes are independent samples of the same distribution, not subsets of each
  other.
- **Parallel without changing the result.** Images render in parallel (`--parallel`, default one per
  processor), and the output is bit-identical to a single-threaded run. Measured on a machine with 12
  cores: about 13 images/s on one thread and about 120 images/s in parallel. Depending on the machine,
  a 200 000-image run takes between 20 and 65 minutes.
- **A rare scene variant draws no extra random numbers**, or every existing seed would change; the
  squared deck is built that way.

Writing a sample is **idempotent**: before writing, `VariantDatasetWriter` removes every artifact of an
earlier write of the same name – detect and locate label, crop, and the entry in *any* classification
class folder including `none`. Without this, generating into an existing directory – or correcting a
photo from a card to "No card" – would leave stale labels behind, and a frame without a recognisable
card could keep a valid card label.
