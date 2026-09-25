# JassCardEye

JassCardEye counts the points of a Jass game with the phone camera. After a game, the cards one party has
won are laid on a pile one by one; a YOLO model on the phone recognises the card on top, and the app adds
up the points. It knows the French-suited and the German-suited Swiss deck, every trump, Obenabe,
Undenufe, Slalom and Guschti, the last trick and a factor from ×1 to ×8.

The project is two things at once. It is the transfer project of a CAS in Machine Learning at HSLU, which
asks how well the top card of a pile can be recognised by a model trained on synthetic images. And it is
an iPhone app and an Android app by YARX GmbH: free to try, with one purchase that makes the counted
points readable. The two apps are one product - a feature, a text or a design change lands in both.
Both are submitted to the App Store and Google Play.

The website with the manual, support and the privacy policy: https://jasscardeye.yarx.ch

## Where to start

Project knowledge - the vision, the architecture, the conventions and a glossary - is collected in
[context/README.md](context/README.md), with a reading order. It is the entry point for people and agents
alike. Each part of the code has its own README for the details.

## What is where

- **`src/app/ios`** - the iPhone app: SwiftUI and Core ML, Xcode project generated with XcodeGen.
  [README](src/app/ios/README.md)
- **`src/app/android`** - the Android app: Kotlin, Jetpack Compose, CameraX and LiteRT.
  [README](src/app/android/README.md)
- **`src/tools/dataset`** - the .NET solution that renders the synthetic training data: domain model
  (`Cards`), renderer (`Generation`), command line (`Cli`), and a desktop viewer for reviewing and
  labelling (`Viewer`). [Data pipeline](context/architecture/data-pipeline.md)
- **`src/training`** - training (`train.py`), export to Core ML and LiteRT (`export.py`), and fetching the
  results of a run (`fetch_run.py`). [README](src/training/README.md)
- **`src/scripts`** - what a training pod runs, the sweep that removes orphaned pods, and the release
  scripts of both apps. [Training pipeline](src/training/pipeline.md)
- **`src/tools`** - checks (scoring, pile, dataset, run plan, pod sweep, app texts) and the generators of
  the icons, the tick sound and the apps' string resources.
- **`l10n`** - the texts of both apps, one file per language, German first. [Keys](l10n/keys.md)
- **`src/web`** - the website: Angular, every page prerendered. [README](src/web/README.md)
- **`data/cards`** - the 72 card scans the training images are rendered from, 36 per deck.
- **`data/real/val`** - labelled real photos and video frames: the validation set.
- **`docker`** - the training image the rented GPU pods run. [Training pipeline](src/training/pipeline.md)
- **`doc`** - the thesis and the work journal, in LaTeX. [README](doc/README.md)
- **`context`** - project knowledge. [README](context/README.md)
- **`.github`** - CI, the release of both apps, the training launch, the pod sweep and the website deploy.

Datasets, training results, the exported models, the Xcode project and the apps' string resources are
generated and never committed.
Both apps build without a model and then say so.

The apps ship recognition variant C, one detector for all 72 cards. The thesis compares it with a
classifier of the whole image (A) and a two-stage approach that locates the card and then classifies it
(B). After the thesis only C is pursued; A and B stay trainable.

## Checks

What `.github/workflows/ci.yml` runs on every pull request and every push to `main` - unless the change
touches only documents, the thesis or the website - as commands for a local run from the repository root:

```bash
# Dataset tooling and the Python checks
dotnet build src/tools/dataset/JassCardEye.Dataset.slnx -c Release
dotnet run -c Release --project src/tools/dataset/Cli -- generate --count 300 --seed 1
dotnet run -c Release --project src/tools/dataset/Cli -- rebuild --dataset data/real/val
python3 src/tools/check_run_plan.py
python3 src/tools/check_dataset.py output/dataset data/real/val
python3 src/tools/test_sweep.py
python3 src/tools/test_manifest.py
dotnet run -c Release --project src/tools/dataset/Checks

# The Jass scoring and the pile, with nothing but Foundation
swiftc -o /tmp/check_scoring src/app/ios/Sources/JassScoring.swift src/app/ios/Sources/JassDeck.swift \
    src/tools/localized_fallback.swift src/tools/check_scoring.swift && /tmp/check_scoring
swiftc -o /tmp/check_pile src/app/ios/Sources/PileTracker.swift src/app/ios/Sources/StabilityRule.swift \
    src/tools/localized_fallback.swift src/tools/check_pile.swift && /tmp/check_pile

# The texts of both apps: every language complete, every key used, the code's German defaults right
python3 src/tools/l10n.py

# The Android app: unit tests, a debug build and a release bundle without the upload key
cd src/app/android
./gradlew :app:testDebugUnitTest :app:assembleDebug :app:bundleRelease -Pjasscardeye.allowUnsigned=true
```

The website has its own workflow: `cd src/web && npm ci && npm run build`. The iPhone app is built in
Xcode after `cd src/app/ios && xcodegen generate`; its purchase tests run from Xcode as well, see its
README. Training runs on a rented GPU started from GitHub Actions, as described in
[src/training/pipeline.md](src/training/pipeline.md).

## Prerequisites

- .NET SDK 10
- Python 3.12 for the checks; the model export environments want Python 3.13 (see `src/training`)
- a Swift toolchain - Xcode on a Mac; CI uses the `swift:6.2` container on Linux
- for the iPhone app: Xcode with the iOS platform and XcodeGen 2.38 or newer; the app runs from iOS 17
- for the Android app: JDK 17 or newer (CI uses 21), the Android SDK with platform 37.2, and for a release
  bundle the NDK version pinned in `src/app/android/gradle/libs.versions.toml`; the app runs from Android 10
- for the website: Node 22

## Licence and source

JassCardEye is free software under the GNU Affero General Public License 3.0, with an additional permission
for distribution through app stores such as the App Store and Google Play - see [LICENSE](LICENSE). The
recognition models are trained with Ultralytics YOLO, itself licensed under the AGPL-3.0, and the AGPL-3.0
covers the trained models as well. The credits for the suit marks and the libraries of the apps and the
website are on https://jasscardeye.yarx.ch/lizenzen.

This repository is the complete corresponding source of both apps: the app code, the generator of the
training data with its card scans, the training and export scripts, the workflows that build the releases,
and the real validation set. The source code links in the apps and on the website point here. The trained
weights of the model the apps bundle (`best.pt` and the two exports) are attached to the releases of this
repository; everything needed to train them again is described in [src/training/pipeline.md](src/training/pipeline.md) and
[src/training/README.md](src/training/README.md).

## Contact

Published by YARX GmbH, Kölliken, Switzerland - support@yarx.ch
