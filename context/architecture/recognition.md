# Recognition: from the camera to a counted card

What both apps do with a camera frame until a card lies on the pile, and every value on that way that
decides what is shown or counted: where it is set, whether iOS and Android agree, and whether it is
measured or set by hand. It follows both code paths step by step.

## The premise

**Rather nothing than wrong.** A card counted that is not on the pile weighs far more than a card
missed. The player sees a missing card and adds it by hand; a wrong card goes unnoticed and the score is
off. Every value below is judged by false counts first, and so is every change to the training data.

## The way, and its values

| # | Step | Value | iOS | Android | Basis |
|---|---|---|---|---|---|
| 1 | Camera | back camera, wide lens; ultra-wide when chosen in the settings | the physical wide or ultra-wide camera | CameraX's back camera; the ultra-wide as a camera of its own or by zooming out | choice |
| 2 | Capture size | 1280×720; 1920×1080 with the ultra-wide lens | a fixed preset; the ultra-wide falls back to 720 | a request CameraX may answer with another size, which is not recorded | choice, not measured |
| 3 | Late frames | a frame that arrives during an inference is dropped | `alwaysDiscardsLateVideoFrames` | `KEEP_ONLY_LATEST` | same |
| 4 | Analysed square | the largest centred square of the frame: 720 or 1080 px | Vision's region of interest | CameraX's 1:1 viewport | same geometry |
| 5 | Model input | 640×640 RGB, values divided by 255, the square scaled to fit | Vision crops and scales (`.scaleFit`) | bilinear `Canvas.drawBitmap` | same size, different resampler, not measured |
| 6 | Model | YOLO11n detector over 72 classes, variant C, both files exported from the same `best.pt`; the bundled `models.json` names the run and commit | Core ML with built-in NMS, stored in **FP16** (5.3 MB) - Ultralytics stores every model with NMS that way - on Neural Engine, GPU or CPU as Core ML decides (`computeUnits = .all`) | LiteRT without NMS, **FP32** in the file (10.7 MB); on the GPU delegate where the phone supports it, otherwise on the CPU with four threads | different precision, not measured; no 8-bit quantisation on either side |
| 7 | Candidates | a box is considered when its best class scores above 0.25 | inside the Core ML NMS stage | `TensorDecoding`, called by `DetectorRecognizer` | Ultralytics default |
| 8 | Suppression | per class, overlaps above IoU 0.7 removed | the Core ML NMS stage | `TensorDecoding`, strongest first, then at most 20 boxes | Ultralytics default |
| 9 | Confidence | the best class's score, 0 to 1 | `observation.confidence × labels.first.confidence` - Vision's own confidence is the sum over all classes, its labels are those scores normalised to 1 | the score in the tensor | same |
| 10 | Live threshold | **0.6**: a detection below it does not exist for the app | `minConfidence` in `LiveDetectionModel`, applied in `DetectorRecognizer` | the same | set by hand, not measured |
| 11 | Deck filter | cards of the deck not in play are dropped before the stability rule sees them | `LiveDetectionModel` | the same | a round is played with one deck, chosen in the start sheet |
| 12 | Top card | the remaining detection with the highest confidence | `LiveDetectionModel` | the same | same |
| 13 | Stability rule | **Run** (*Serie*, default): the same top card in N frames in a row. **Majority** (*Mehrheit*): N of the last 2N−1 frames, with no other card that is not yet counted appearing more than once. N is 3, adjustable from 1 to 10 | `PileTracker`, `StabilityRule` | the same | both defaults set by hand, not measured; which rule is the better trade is reasoning, not a measurement |
| 14 | Pile | a card counts once; a card removed by hand is not counted again until another card, or none, has been on top; *Reset* empties the pile | `PileTracker` | the same | same, `check_pile.swift` and `PileTrackerTest` |
| 15 | Feedback | the tap and the tick sound for a counted card - never for a mere detection | `Haptics`, `CardSound` | the same | same |

The live threshold (10) and the stability rule (13) are what stand between a detection and a count. The
threshold decides which detections exist at all; the rule decides how long one has to last.

## Where the two apps differ

| Difference | What it can change | Known |
|---|---|---|
| Precision: iOS FP16, Android FP32 in the file - computed in FP16 on the GPU delegate, in FP32 on the CPU | scores differ from about the second decimal, so a card close to a threshold can go either way | not measured on devices; sessions of the same pile on several devices would show it |
| Model at release: with `run_id` blank, each platform's job of the Release workflow looks up the newest finished run by itself | a run that finishes between the two jobs gives the apps different models | pinning `run_id` avoids it |
| Torch: iOS at 60 %; Android at 60 % only where the phone reports strength levels, otherwise at full power | a burnt-out card face on Android phones without strength levels | not measured |
| Resampling from 720 or 1080 to 640: Vision's scaler against bilinear filtering without mipmaps | fine pips and indices alias differently, most with the ultra-wide lens | not measured |
| Capture size: a preset against a request | an Android phone may analyse another resolution than asked for, and nothing records it | not recorded |
| Pixel format: iOS asks AVFoundation for BGRA, Android asks CameraX for RGBA - each platform converts from the sensor's YUV itself | small shifts in colour, red against orange suits | not verified |
| Whether Core ML's NMS stage ranks and thresholds by the summed score | two overlapping boxes could resolve differently | not verified |
| Android keeps at most 20 detections | nothing in practice: the app reads one top card | accepted |

## What is measured

- **Inference time per frame** of variant C: iPhone 17 Pro Max 15 ms, iPhone 14 Pro 30 ms, Pixel 7a
  50 ms, Nexus 5X about 3000 ms. The target is under 50 ms.
- **Accuracy** of the detector on the real validation set (`data/real/val`, 813 frames): mAP@50 0.908
  for the model trained on 200 000 images. This is the model alone, at Ultralytics' validation
  settings - not the apps' threshold and stability rule.
- **Compute units** move a confidence in the second or third decimal: the same Core ML model on the
  Neural Engine against the CPU. In Python, GPU and CPU give the same values.

The live threshold, the number of frames and the choice of the default rule are **not** among them:
they are set by hand.

## How a value is checked

- **On real sessions.** Record with *Session aufzeichnen*. The dataset tool's **Analyse sessions**
  replays the open sessions with another threshold and stability rule (tab *Settings*), exactly as
  `PileTracker` decides, lists the weakest cards (tab *Cards*), and shows from the session info which
  device, app, model and settings each session came from. See `context/architecture/data-pipeline.md`,
  "Session recordings".
- **On the models.** `src/tools/compare_capture.py` runs a capture through both model files. It checks
  the models, not the apps: Vision's scaling, the Neural Engine's FP16 and each app's resampling are not
  part of it.
