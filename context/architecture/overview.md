# Architecture – overview

> System layout, components and data flow from card scan to the counting apps.
> Context: [vision](../vision.md).
> Generator concepts, determinism and dataset format: [data-pipeline.md](data-pipeline.md).
> Every value between the camera and a counted card: [recognition.md](recognition.md).

## Repository layout

All code lives under `src/`; data, documentation and infrastructure stay at the root.

| Path | What it is |
|---|---|
| `src/tools/dataset/` | .NET 10 solution that makes and inspects the training data (see below). |
| `src/training/` | Training (`train.py`), fetching a finished run (`fetch_run.py`), export for both apps (`export.py`). |
| `src/scripts/` | What the training pod and the release workflow run, e.g. `run_training.sh`, `release_ios.sh`, `release_android.sh`. |
| `src/tools/` | Checks and generators around the rest: `check_scoring.swift`, `check_pile.swift`, `check_run_plan.py`, `check_dataset.py`, `compare_capture.py`, the icon and sound makers. |
| `src/app/ios/`, `src/app/android/` | The two counting apps. |
| `src/web/` | The project website, https://jasscardeye.yarx.ch. |
| `data/` | The source of truth for data: card scans (`cards/`), real photos without a card (`negatives/`), the real validation set (`real/val/`). |
| `docker/` | The container the training pod runs. |
| `.github/workflows/` | `ci.yml` (builds and checks), `train.yml` (starts a training pod), `image.yml` (builds its container), `cleanup.yml` (terminates pods that outlived their training), `release.yml` (both apps under one build number), `web.yml` (website). |
| `doc/`, `context/` | The thesis, project knowledge. |

## Dataset tools

The data tooling lives under `src/tools/dataset/` as a .NET 10 solution (`JassCardEye.Dataset.slnx`):
five projects around one job, making and inspecting the training data. How to run them is in
[src/tools/dataset/README.md](../../src/tools/dataset/README.md).

| Project | Type | Responsibility |
|---|---|---|
| **JassCardEye.Dataset.Cards** | Class library, dependency-free | Domain model: `Deck`, `Suit`, `Rank`, `Card`, `JassDeck` (2 decks × 36 cards), canonical YOLO class list (`JassClasses`, 72 entries). Single source of truth for class IDs and labels. |
| **JassCardEye.Dataset.Generation** | Class library (SkiaSharp) | Render engine for synthetic training data: scene model, `RunPlan` (deck and camera tilt from the image index), random parametrisation, pinhole camera, perspective card warping, real negatives. `VariantDatasetWriter` derives every label variant from one annotation and is shared with the Viewer; `DatasetRebuilder` restores the derived folders from the source labels. |
| **JassCardEye.Dataset.Cli** | Console app | CLI over Generation: `generate` (dataset), `rebuild` (derived folders from images and the detect and locate labels), `plan` (deck and tilt of every image of a run), `backgrounds` (table patterns), `classes` (class list). |
| **JassCardEye.Dataset.Viewer** | Avalonia desktop app | Four sections in a sidebar. **Browse dataset** shows a dataset with its labels, axis-aligned and oriented, for every variant it finds (C/B₁/A/B₂); **Edit label (E)** corrects the sample on screen and rebuilds the variants. **Label photos** annotates real photos by their four card corners and class, or marks a frame as **No card**; **Propose (⇧→)** lets variant B place that label first, to check and correct rather than to click. **Extract frames** turns a video into photos with the ffmpeg installed on the machine; **Review frames →** pages through them, and **Delete marked** moves the frames marked for deletion to the Trash. **Analyse sessions** puts a session recorded in the app - its video and its recognition log - on one timeline, marks where the model contradicted itself, and hands a frame to Label photos. |
| **JassCardEye.Dataset.Checks** | Console app | Checks the rules of the Viewer that need no window - how it reads a recognition log and what it calls an anomaly, and what it makes of the two models that propose a label - by compiling those source files of the Viewer; CI runs it. |

Dependencies: Generation → Cards; Cli → Cards, Generation; Viewer → Cards, Generation; Checks → Cards.
Cards stays deliberately free of image and framework dependencies, so anything can use the class-ID
contract without pulling in SkiaSharp or Avalonia.

## Training and export

Training runs on a rented GPU: `train.yml` starts a RunPod pod that generates the dataset, trains,
validates against the real photos in `data/real/val`, uploads to Azure Blob Storage as it goes and
terminates itself - see [src/training/pipeline.md](../../src/training/pipeline.md). `src/training/fetch_run.py` brings
a finished run back, and `src/training/export.py` turns one `best.pt` into the model of both apps -
see [src/training/README.md](../../src/training/README.md).

The pipeline can train all three recognition variants the thesis investigates: **A** classifies the
whole image, **B** locates the card (B₁) and classifies the rectified crop (B₂), **C** is one detector
over the 72 classes. After the thesis only C is pursued, and it is what both apps ship. A and B stay
in the code and remain trainable, but are not used in the apps.

## The counting apps

The counting apps sit outside the .NET solution: `src/app/ios/` (SwiftUI) and `src/app/android/`
(Kotlin, Jetpack Compose). They are one app on two platforms - every feature, text and design change
lands in both in the same change. `src/app/ios/Sources/` is the reference the Kotlin sources mirror:
every Swift file has a Kotlin file of the same name, and both keep their settings under the same keys.
The iOS target also builds for macOS as a compile guard; only the iPhone app ships.

Both bundle one model exported by `src/training/export.py` from the same `best.pt` - Core ML for
Apple, LiteRT for Android - so a card is recognised by the same weights on both platforms. A release
carries exactly one variant; the release scripts refuse more.

Inside, both apps are built from the same parts:

- **`LiveDetectionModel`** lives as long as the app. It holds the settings and the loaded recogniser;
  camera and per-frame inference run only while a counting session is open, so the home screen costs
  nothing.
- **`CardRecognizer`** puts the variant behind one interface, so the frame loop, the stability rule
  and the pile never learn which model runs.
- **`PileTracker`** owns the pile - the stability rule, the counted cards and the corrections by hand -
  and changes it as one value.
- **`JassScoring`** holds the rules as data: disciplines, value tables, last trick, factor. The same
  invariants are checked on both platforms, by `src/tools/check_scoring.swift` and `JassScoringTest`.
- **`Store`** is the one place that knows whether the points are unlocked, read from the App Store or
  Google Play rather than from a flag of the app's own.

The apps have no network code of their own and no server behind them. How to build, test and release is in
`src/app/ios/README.md` and `src/app/android/README.md`.

## Data flow

```
data/cards/<deck>/<suit>/<rank>.jpg      data/negatives/
72 scans, 1710×2670                      real photos without a card
        │                                        │
        ▼                                        ▼
┌────────────────────────────────────────────────────────┐
│ JassCardEye.Dataset.Generation                         │
│  RunPlan (deck, tilt) + GenerationParameters           │
│    → SceneRandomizer → Scene                           │
│  Scene → SceneRenderer (pinhole camera + homography)   │
│    → 640×640 image + corners of the top card           │
│  VariantDatasetWriter → images/  detect/  locate/      │
│    classify_full/  classify_crop/  classes.txt         │
└────────────────────────────────────────────────────────┘
        │
        ▼
   train.py on a RunPod GPU (train.yml), validated on data/real/val
        │
        ▼
   fetch_run.py → export.py (one best.pt, variant C)
        ├──► Core ML (.mlpackage) ──► src/app/ios/       iPhone
        └──► LiteRT  (.tflite)    ──► src/app/android/   Android
                        │
                        ▼
   Counting app: recognise the top card live, count the pile
```

Table patterns are generated procedurally for every image unless a folder of backgrounds is passed
to `generate`. The real validation set has the same layout as a generated dataset, because the
Viewer's **Label photos** writes it through the same `VariantDatasetWriter`.

**Visual inspection** of a generated or real dataset is done in the Dataset Tool's **Browse
dataset** section: open the folder, page through the images with their labels drawn in.

## Class-ID contract

72 classes, ID = `(int)Suit * 9 + (int)Rank`, order suit (clubs, diamonds, hearts, spades, then
acorns, roses, bells, shields) × rank ascending (6 → ace), labels `suit_rank`. The French deck is
0–35 and the German deck 36–71, so each deck is one contiguous block. Variant A adds a 73rd class,
`none`, for images without a card. Defined in `JassCardEye.Dataset.Cards` and binding for all
training data; the apps read the class names from the model and split a label at its first
underscore. The second deck is modelled as more classes of the same model rather than a second model,
so one export serves both decks and the app filters by the deck in play.

## Output responsibility

The detect label holds **at most one box per image** – the topmost card; a negative, with no card in
view, has none. The locate variant marks the same card as an oriented box of the single class `card`,
and the classification variants file the whole image or the rectified crop under its class.
Underlying cards are rendered for realistic occlusion but never labelled, since they are not the
detection target.
