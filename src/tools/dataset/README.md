# Dataset tools

The .NET solution that makes and inspects the training data of JassCardEye:
`JassCardEye.Dataset.slnx`, five projects around one job.

| Project | What it is |
|---|---|
| `Cards` (`JassCardEye.Dataset.Cards`) | The domain model, free of dependencies: decks, suits, ranks, and the 72 classes with their IDs and labels |
| `Generation` (`JassCardEye.Dataset.Generation`) | The render engine on SkiaSharp: scenes, camera, light, labels, the dataset writer, the rebuild and the run plan |
| `Cli` (`JassCardEye.Dataset.Cli`) | The command line: `generate`, `rebuild`, `plan`, `classes`, `backgrounds` |
| `Viewer` (`JassCardEye.Dataset.Viewer`) | **JassCardEye Dataset Tool**, a desktop app on Avalonia: browse a dataset and correct its labels, label real photos, extract frames from a video |
| `Checks` (`JassCardEye.Dataset.Checks`) | The rules of the tool that need no window, held to account: reading a recognition log, the anomalies, and what a model's answer turns into when a label is proposed. CI runs it |

What the data looks like and why – scene model, label variants, negatives, dataset layout,
determinism – is described in
[context/architecture/data-pipeline.md](../../../context/architecture/data-pipeline.md).

## Prerequisites

- **.NET 10 SDK.**
- **ffmpeg**, only for extracting frames from a video (macOS: `brew install ffmpeg`). The dataset tool
  looks for it once at start, in `PATH` and in `/opt/homebrew/bin`, `/usr/local/bin` and `/usr/bin` –
  an app started from the Finder or the Dock does not inherit the shell's `PATH`. Install it before
  starting the tool.
- **macOS**, to delete reviewed frames recoverably: they go to the Trash through the system's `trash`
  command. Where that command is missing, deleted frames are gone for good, and the tool says so.
- **Python 3**, for the checks next to this solution (`src/tools/check_dataset.py`,
  `src/tools/check_run_plan.py`).
- **The training environment and the weights of variant B**, only to have a label proposed while
  labelling photos (see *Label photos*): `src/training/.venv` with Ultralytics (see
  [src/training/README.md](../../training/README.md)), and one training run holding both stages:

  ```bash
  python src/training/fetch_run.py --run-id <run-id> --variant b1 --weights-only
  python src/training/fetch_run.py --run-id <run-id> --variant b2 --weights-only
  ```

  Without them the tool simply says so on the Label photos page, and labelling works without proposals.

## Build

```bash
dotnet build src/tools/dataset/JassCardEye.Dataset.slnx -c Release
```

## The CLI

Run it from the repository root: the default paths (`data/cards`, `data/negatives`, `output/dataset`)
are relative to the working directory.

```bash
dotnet run -c Release --project src/tools/dataset/Cli -- <command> [options]
```

Options take the form `--name value`. A number that does not parse falls back to its default without a
warning, so it is worth reading the first line a command prints – `generate` states the image count and
size it is about to render.

### generate

Renders a synthetic dataset and writes every selected label variant from the one render run.

```bash
dotnet run -c Release --project src/tools/dataset/Cli -- generate --count 2000
```

| Option | Default | Meaning |
|---|---|---|
| `--out` | `output/dataset` | Target folder. A sample of the same name is replaced; images of an earlier, larger run stay behind, so start from an empty folder |
| `--cards` | `data/cards` | Root of the card scans, `{deck}/{suit}/{rank}.jpg` |
| `--count` | `100` | Number of images |
| `--seed` | `1` | Seed; seed and count together decide every image |
| `--size` | `640` | Edge length of the square images |
| `--tasks` | `all` | Variants, comma-separated: `c` or `detect`, `b` (= `locate` + `classify-crop`), `a` or `classify-full`, `locate`, `classify-crop`, `all` |
| `--negatives` | `0.3` | Share of frames without a recognisable card |
| `--border-flying` | `0.25` | Share of positives with a blurred card clipping in from the border |
| `--slight-blur` | `0.3` | Share of positives whose top card is slightly motion-blurred |
| `--aligned-stacks` | `0.05` | Share of frames showing a squared deck instead of a scattered heap |
| `--real-negatives` | `data/negatives`, if it exists | Folder of card-free photos for the negatives; `""` switches them off |
| `--real-negative-share` | `0.25` | Share of the negatives taken from those photos |
| `--backgrounds` | none | Folder of own background photos, drawn flat for 85 % of the frames. Without it every background is a generated pattern |
| `--distance` | random, 1.8–4.2 | Fixes the camera distance (smaller = closer) |
| `--format` | `jpg` | `jpg`, or `png` – lossless, and considerably larger |
| `--quality` | `90` | JPEG quality |
| `--parallel` | one per processor | Images rendered at once; the result is the same at any value |

Camera tilt and deck are not options: both follow from the image index and the count – see `plan`. A
training run renders `--count 200000 --seed 1 --size 640 --tasks all` (`src/scripts/run_training.sh`).

### rebuild

Recreates the derived parts of a dataset – classification folders, rectified crops, the hard-link
folders and the `data.yaml` files – from `images/`, `detect/labels/` and `locate/labels/`. Needed after a
checkout of `data/real/val`, before training on it or browsing its variants.

```bash
dotnet run -c Release --project src/tools/dataset/Cli -- rebuild --dataset data/real/val
```

| Option | Default | Meaning |
|---|---|---|
| `--dataset` | `output/dataset` | Dataset root; it must contain `images/` |

### plan

Prints `<tilt> <deck>` for every image of a run of the given size, in generation order, without rendering
anything.

| Option | Default | Meaning |
|---|---|---|
| `--count` | `1000` | Size of the run |
| `--summary` | off | Print only `count`, `min`, `max` (the tilt range) and `decks` – what `src/tools/check_run_plan.py` reads |

### classes

Lists the 72 classes per deck, with class ID, short display and label.

| Option | Default | Meaning |
|---|---|---|
| `--out` | none | Also write them, one per line, to this `classes.txt` |

### backgrounds

Writes generated table patterns as flat PNG previews – only to look at them. A dataset does not need
them: every background is generated from its image's seed.

| Option | Default | Meaning |
|---|---|---|
| `--out` | `output/backgrounds` | Target folder |
| `--count` | `8` | Number of patterns |
| `--size` | `1024` | Edge length |
| `--seed` | `1` | Seed |

## The dataset tool

```bash
dotnet run -c Release --project src/tools/dataset/Viewer
```

The window is called **JassCardEye Dataset Tool**, and its sidebar says "Dataset tool". The sidebar
holds four sections, each showing only its own controls. The status bar at the bottom names the file
on screen and offers ← and → to page through and −, Fit and + to zoom.

### Browse dataset

Look through a dataset and correct the label of a sample.

- **Open folder…** opens a dataset root, or a folder inside one. The tool finds the variants the dataset
  holds and lists them under **Variant**: C – Detector (72 classes), B₁ – Localise (OBB, 1 class), A –
  Classification (whole image), B₂ – Classification (crop). Labels are drawn on the image – the box for
  C, the oriented quadrilateral for B₁ – with their class name; for A and B₂ the class is the folder name
  and is shown in the status bar. A folder without variants is shown as a plain folder of images.
- **Open image…** shows a single image, with its YOLO label if one lies next to it or in a sibling
  `labels/` folder.
- **Edit label (E)** corrects the sample on screen. It works in a dataset with source labels
  (`detect/labels/`), whichever variant is being browsed: the dataset's own frame is put on screen, the
  stored corners appear in their stored order, and the stored class is preselected.
  - Drag a corner to move it. **Turn ↻ (R)** moves the corner order round by one; twice fixes a card that
    was annotated from the opposite corner and rectifies upside down. The **Rectified card** preview in
    the bottom right shows the crop as it would be written.
  - **Save (Enter)** writes only the two source labels and then rebuilds every variant of the dataset;
    the image itself is not rewritten.
  - **No card (N)** removes both labels; the frame stays in the dataset as a negative.
  - **Delete sample** (or the Delete key), pressed twice, removes the frame and its labels – for a card
    that is nearly complete but touches the border, see below. The images and source labels of
    `data/real/val` are version-controlled, so a deleted sample can be brought back with Git.
  - **Next flagged (F)** jumps to the next sample whose oriented label touches the image border, with the
    threshold of `src/tools/check_dataset.py` – exactly what would stop a training run.
  - **E** leaves the correction.

### Label photos

Write real photos into a dataset: four corners and one class per photo.

- **Dataset – Change…** picks the target dataset. It is created or extended, and remembered.
- **Photos – Choose photos…** opens a folder of photos: JPEG, PNG or BMP. HEIC and HEIF files are
  skipped, and the status bar says how many. Convert them first, for example with `sips` on macOS:

  ```bash
  mkdir -p jpg && find . -maxdepth 1 -iname '*.heic' | while read -r f; do
    sips -s format jpeg -s formatOptions 92 "$f" --out "jpg/$(basename "${f%.*}").jpg"
  done
  ```

- **Click the four corners of the top card** in order – top left, top right, bottom right, bottom left of
  the card standing upright. Drag a corner to move it; Backspace removes the last one, **Clear points
  (Esc)** all of them. Once all four are set, the **Rectified card** preview shows the crop: upright
  means the order is right, squashed or upside down means it is not, and **Turn ↻ (R)** fixes it.
- **Pick the class** as deck, suit and rank: **French** or **German** (D switches; the deck is
  remembered), a suit button (keys 1–4 in the order the buttons stand), and a rank (6–9, 0 for the ten,
  U or B for the Under or Bauer, O for the Ober or Dame, K, A). Suit and rank start empty on every photo,
  so a class is never carried over by accident; the deck stays.
- **Save (Enter)** writes the photo into every variant, named after its file, and moves on to the next
  photo. **No card (N)** saves it as a negative.
- **Propose (⇧→)** has the corners and the class placed by variant B instead of clicked: B₁ finds the
  oriented box, B₂ names the card on the rectified crop. **⇧→** goes to the next photo and proposes
  there (**⇧←** backwards); the button proposes for the photo on screen. It is a proposal and nothing
  else:
  - Nothing is ever saved by it. **Enter** still saves, and until then everything is corrected as
    usual - drag a corner, **Turn ↻ (R)**, pick another rank.
  - Proposed corners are drawn **orange** and turn green as soon as one of them is moved; the status
    line names the card with both confidences.
  - A photo that is **already in the dataset** keeps its stored label and gets no proposal.
  - Below a box confidence of **0.50** nothing is placed ("no proposal"); below a class confidence of
    **0.60** the corners are placed and the class stays open. The class is always the best card of the
    deck being labelled - a proposal never switches the deck, it says when the other one looks
    stronger.
  - Which way up the card is, is a guess: B₂ scores a crop and the same crop turned by 180° the same,
    because both decks are double-headed. The tool takes whichever way up the card stands on the
    photo, and the **Rectified card** preview shows what came out.
  - The page's **Propose** line says which run the weights come from, or what is missing. A run other
    than the newest one holding both stages can be pointed at with `JASSCARDEYE_PROPOSE_B1` and
    `JASSCARDEYE_PROPOSE_B2`, and another Python with `JASSCARDEYE_PROPOSE_PYTHON`.
  - Every sample saved after a proposal is written to `proposals.csv` in the settings folder: what was
    proposed, what was saved, and what became of the corners – `as proposed`, `turned` (the same four
    corners half a turn on, so only the guess about which way up was wrong), `reordered` or `moved`
    (B₁ really did put a corner in the wrong place). The real validation set is what these models are
    measured on, so how much of it they wrote themselves has to stay countable.
- **Next unlabelled** jumps to the next photo that is not in the dataset yet. A photo that already is
  shows its stored corners and class, and saving replaces the entry – a mistake is corrected by moving a
  corner or picking another rank.

Photos are brought to 640×640 by a centre crop – the size of the generator's images.

**A card cut off by the border** follows the generator's rule. The tool warns when a marked corner comes
close to the image border – more cautiously than the dataset check does. A card that is clearly cut off
is saved with **No card (N)**: a negative, as the generator files it. A card that is nearly complete and
only touches the border is the ambiguous band and does not belong in the dataset: in Label photos, leave
that photo unsaved; a sample that is already in the dataset is removed with **Delete sample** in Browse
dataset.

### Extract frames

Turn a video into photos to label.

- The page says whether **ffmpeg** was found. Without it no video can be chosen; install it and start
  the tool again.
- **Choose video…** accepts MP4, MOV, M4V, AVI and MKV. **Frames go to** shows the target: a folder named
  after the video, next to it. If that folder exists and is not empty, extraction is refused – frames of
  two extractions with different steps would sit side by side with nothing to tell them apart.
- **Frame step** keeps every n-th frame: 4, the default, keeps frames 1, 5, 9, …; the step is
  remembered. Neighbouring frames are nearly identical, and a validation set gains nothing from them.
- **Extract frames** runs ffmpeg; while it runs the button cancels. Frames are named
  `<video>_<frame>.jpg`, the number being the frame's position in the video counted from 1, at least five
  digits long – `game_1_00005.jpg` is the fifth frame of `game_1.mp4`. ffmpeg writes into a hidden
  `.extracting` folder, and the frames get their real names only once it is done, so a cancelled or
  failed run leaves nothing behind.
- Afterwards **Review frames →** goes through the result, and **Label these frames →** opens it in Label
  photos straight away. **Review a folder…** reviews frames extracted earlier.

### Review frames

Even every fourth frame leaves runs of near-identical frames, a hand in the way, or no card at all.
Going through them before labelling is quicker than skipping them one by one while labelling.

- ← and → browse. **Space** or **X** – or **Mark for deletion (Space)** – marks the frame on screen and
  moves on; on a marked frame it takes the mark away again. A marked frame gets a red border.
- **Delete N marked**, clicked twice (the second time it reads **Confirm: delete N**), moves the marked
  frames to the Trash, from where they can be put back – or deletes them for good where the macOS
  `trash` command is missing; the button's tooltip says which. **Unmark all** clears the marks.
- **Label these frames →** opens the folder in Label photos, **Close review** goes back to the extraction
  page.

### Analyse sessions

A session recorded in the app with *Session aufzeichnen*, and what the model made of every frame of it, on
one timeline. The columns of the recognition log and what counts as an anomaly are in
[data-pipeline.md, "Session recordings"](../../../context/architecture/data-pipeline.md#session-recordings).

- **Open sessions…** takes the ZIPs the apps write, one per session - each is unpacked once into a folder of
  its name beside it, and opened from there - or session videos with their log of the same name (`.csv`)
  and, when it is there, the session info (`.json`) next to them. A pair that does not belong together - another number of frames than rows, or a frame at
  another time than its row - is refused rather than shown with detections on the wrong pictures. Like
  Extract frames it needs ffmpeg, and ffprobe beside it.
- **The timeline** runs from left to right through the session: each card the model named in a colour of
  its own, nothing as the dark ground, a white tick where a card was counted, and the anomalies in orange
  above. Click or drag to show a frame; ← and → step one frame.
- **The frame** is shown with the model's box and card, the confidence, and whether it was counted there;
  the status bar gives the frame number and its time.
- **Anomalies** lists outliers, ghosts, cards taking turns, and counts that disagree with the frames around
  them; clicking one shows its frame. Below, **over all open sessions**, it counts confused pairs, ghosts per
  card, and the direct changes from one card to another. The changes include every card laid on the pile in
  the ordinary way; they are context, not errors.
- **Cards** lists every card that was the top detection in the session, the weakest first: in how many
  frames, its highest confidence, how many frames reached the threshold being tried and the longest run of
  them, the frame the app counted it on, and whether the settings being tried count it.
- **Settings** replays every open session with another **threshold**, **rule** (Run or Majority) and number
  of **frames**, exactly as the app's pile decides. Per session it shows the device, app and model from the
  session info, the threshold and rule the session was counted with (from the info, or else the rule that
  explains the counts), how many cards the app counted and the replay counts, the cards counted **in
  addition** - possible false counts, listed first because they weigh more than a missed card - and those
  **no longer** counted, the short readings of one or two frames, and the distribution of confidences. On the
  timeline, frames below the threshold are dimmed and a green tick marks where the replay counts a card.
  **As recorded** goes back to the session's own settings. The log holds no detection below the threshold the
  app was running with, so a lower threshold changes nothing.
- **Label this frame →** writes the frame as `<video>_<frame>.jpg` into the folder named after the video -
  the name Extract frames would give it - and opens it in Label photos, where it is labelled as a card or
  saved with **No card (N)**. Back in Analyse sessions the session is still where it was.

On screen and in file names frames count from 1, as in Extract frames; the log's `frame` column counts
from 0.

### Keys

| Key | Where | Action |
|---|---|---|
| ← → | wherever images are shown | previous / next image |
| ⇧← ⇧→ | Label photos | previous / next photo, with a label proposed there |
| E | Browse dataset | start or leave correcting the sample on screen |
| D | labelling or correcting | switch the deck |
| 1 2 3 4 | labelling or correcting | suit, in the order of the buttons |
| 6 7 8 9 0 | labelling or correcting | rank 6–9; 0 is the ten |
| U or B, O, K, A | labelling or correcting | Under / Bauer, Ober / Dame, König, Ass |
| Enter | labelling or correcting | save |
| Esc | labelling or correcting | clear the corners |
| Backspace | labelling or correcting | remove the last corner |
| N | labelling or correcting | save as "No card" |
| R | labelling or correcting | turn the corner order by one |
| F | correcting | next label that touches the border |
| Delete | correcting | delete the sample (press twice) |
| Space, X | Review frames | mark or unmark the frame |
| ← → | Analyse sessions | previous / next frame of the session |

Letters and digits do nothing while a text field has the focus.

### Settings

The target dataset, the deck and the frame step are remembered between sessions, in
`viewer-settings.json` in the user's application data folder, next to the `proposals.csv` a label
proposal writes: `~/Library/Application Support/JassCardEye/`
on macOS, `%APPDATA%\JassCardEye\` on Windows, `~/.config/JassCardEye/` on Linux. The last photo folder is
stored there as well, but not reopened.

### Scripted sessions

For repeatable screenshots the tool can be driven from the environment, without screen recording:

| Variable | Effect |
|---|---|
| `JASSCARDEYE_MODE` | `label`, `extract`, `review` or `sessions`: start in that section instead of Browse dataset |
| `JASSCARDEYE_OPEN` | what to open – a dataset, the photos to label, the frames to review, or a session video |
| `JASSCARDEYE_FILE` | show the first image whose name contains this text |
| `JASSCARDEYE_TAB` | `anomalies`, `cards` or `settings`: the tab of Analyse sessions to show |
| `JASSCARDEYE_EDIT` | `1`: switch correcting on |
| `JASSCARDEYE_PROPOSE` | `1`: propose a label for the photo shown (Label photos) |
| `JASSCARDEYE_MARK` | `1`: mark the frame on screen (review) |
| `JASSCARDEYE_SNAPSHOT` | render the window at 2× into this PNG file, then quit |
