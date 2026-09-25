# JassCardEye App (iOS)

The iPhone app: SwiftUI, AVFoundation, and Core ML through Vision. One multiplatform target builds
iOS and macOS from the same sources; iOS is what ships, and the macOS build is a compile guard,
built by hand.

**Both apps move together.** The Android app in [`src/app/android/`](../android/README.md) is the
same app in features, texts and design, and every change lands on both platforms in the same change.
`src/app/ios/Sources/` is the reference the Kotlin files mirror, mostly under the same file names.
Both bundle the weights of one `best.pt` - as Core ML here, as LiteRT there.

## What the app does

The app counts a pile of Jass cards after a game. The start questions settle deck, discipline, last
trick and factor; then the phone is held over the pile, the top card is recognised frame by frame,
committed once it is stable, and the points add up. Around that sit the settings (model, lens,
stability, feedback, purchase), the demo with blurred points and its one purchase, the *Über* page, and
the hidden developer tools *Bild* and *Session aufzeichnen*. Both apps behave the same.

This file is about how the iOS app does it, and how it is built, tested and released.

## Where the sources meet

The Kotlin twins live in `src/app/android/app/src/main/java/ch/yarx/jasscardeye/`; the Android README
maps them file by file.

| Swift file | What it is |
|---|---|
| `JassCardEyeApp` | the `@main` entry; a minimum window size on macOS |
| `ContentView` | the root: owns `LiveDetectionModel` and `Store` for the app's whole life |
| `HomeView` | home screen and `SettingsSheet` |
| `StartSheet` | *Neue Zählung*: deck, discipline, last trick, factor |
| `MultiplierBar` | the ×1…×8 row of the start sheet |
| `ScanView` | the counting session: status bar, viewfinder, score bar, pile chips, the manual card picker, the camera preview bridge |
| `ScoreRow` | the three-column score line of session and home, blurred when locked |
| `SuitMark` | a suit's drawn mark on its white plate, and `CardChip` (mark plus rank) |
| `AboutView` | *Über*; the `About` enum holds its facts in the order Android's `AboutScreen` shows them |
| `Store` | StoreKit 2: entitlement, product, purchase, restore |
| `PurchaseView` | the purchase page, and `UnlockButton` |
| `LiveDetectionModel` | the frame loop, the session, the settings and their persistence, torch, capture requests |
| `PileTracker` | the pile, the corrections and the stability window, as one value behind one lock |
| `StabilityRule` | *Serie* and *Mehrheit* |
| `JassScoring` | `DeckLayout`, the value tables, `LastTrick`, `CountingMode`, `JassModes`, `Tally`, `CountResult` |
| `JassDeck` | `JassDeck`, the eight `JassSuit`s with the credits of their marks, `CardLabel` (the one place a label is taken apart) |
| `CameraService` | the `AVCaptureSession`: permission, lens, preset, rotation, torch, frames on a serial queue |
| `CameraLens` | normal and ultra-wide lens: which the phone has, the capture preset of each |
| `VideoFrameSource` | the looping test video in place of the camera, simulator builds only |
| `RecognitionVariant` | the variants C, A, B, and `ModelCatalog`, which finds the bundled models |
| `CardRecognizer` | the recogniser protocol: `DetectorRecognizer` (C), `ClassifierRecognizer` (A), `CardLocator` and `TwoStageRecognizer` (B) |
| `Detection` | `Detection` and `ImageSource` |
| `FrameGeometry` | the one crop rectangle stills and recordings share |
| `FrameCapture` | *Bild*: the analysed square as JPEG, and the file names |
| `SessionRecorder` | *Session aufzeichnen*: the analysed square as H.264 video, with its recognition log |
| `SessionInfo` | what a recording was made on and with, written as JSON beside it |
| `Haptics` | the tap for a counted card |
| `CardSound` | the tick for a counted card |
| `AppInfo` | version and build number, read from the bundle |

## Recognition

- **One model ships: C, the detector.** A YOLO11 detector at 640 px with 72 classes - both decks, 36
  cards each - that finds and names the card in one pass. After the thesis only C is pursued. The
  classifier A and the two-stage B stay in the code and remain trainable and exportable, but they are
  not shipped.

  | Variant (as the picker names it) | Pipeline | Bundled model(s) |
  |---|---|---|
  | **C · Detektor** | one detector, boxes and labels in a single pass | `JassCardEye-c` |
  | **A · Klassifikation** | one classifier over the whole square, `none` = no card | `JassCardEye-a` |
  | **B · Zweistufig** | a locator finds the card, a classifier reads the rectified crop | `JassCardEye-b1` + `JassCardEye-b2` |

- **Found at launch, not compiled in.** Xcode compiles each bundled `JassCardEye-<variant>.mlpackage`
  to an `.mlmodelc`, and `ModelCatalog` lists what is there, so a variant that was never exported does
  not exist for the app. With more than one variant bundled, the settings offer a model picker, and
  switching clears the pile - a score must not mix two models. A release bundles exactly one, `c`, so
  the picker never shows in a store build. The class names travel inside the model's metadata, so the
  class-ID order needs no Swift-side list; the label names themselves are parsed through `JassSuit.all`
  and `DeckLayout`.
- **Without a model** the project still builds, and starting a count shows *Kein Modell im Bundle -
  src/training/export.py ausführen.* - so the app compiles on a fresh checkout.
- **Provenance.** `src/training/fetch_run.py --export` writes `Models/models.json` next to the model:
  run, commit, dataset size and seed, metrics. It is bundled but not displayed; the release run prints
  it, so a build number leads to its Release run and from there to the training run.
- **NMS.** The Core ML export bakes NMS into the detector, so Vision returns finished
  `VNRecognizedObjectObservation`s with the pipeline's defaults - candidates from 0.25, suppression per
  class at IoU 0.7 - which the app never overrides. As the confidence the app takes the best class's score, as Android does:
  Vision's `observation.confidence` is the sum over all classes, so it is multiplied by the top label's
  share - and that score is held against the live threshold of 0.6. The detector is stored in FP16,
  which Ultralytics does with every Core ML model that carries NMS. Android's LiteRT
  graph has no NMS and decodes to the same rules. `export.py` asks for NMS on B's oriented-box locator
  too, but Ultralytics drops it there, so `CardLocator` reads the raw `[1, 6, 8400]` tensor
  (`cx, cy, w, h, confidence, angle` per anchor) and takes the most confident box above 0.25. The
  classifiers have no boxes, and `export.py` leaves NMS off for them, keyed on the model's task.
- **One square for everything.** The live loop hands Vision the framing square as its region of
  interest: the largest centred square of the portrait frame, its full width. Vision crops before it
  scales, so the square gets the model's full 640 pixels, and boxes come back normalised to it - the
  space the overlay draws in. B cuts the square out itself, rectifies the card from the locator's
  corners and reports the weaker of its two confidences; its oriented box is ambiguous by 180°, so a
  face card can be rectified upside down and misread.

## Build

```bash
# 1. The model, in the pinned Core ML export environment (see src/training/requirements-export.txt)
python3.13 -m venv src/training/.venv-export
src/training/.venv-export/bin/pip install -r src/training/requirements-export.txt
src/training/.venv-export/bin/python src/training/fetch_run.py --list
src/training/.venv-export/bin/python src/training/fetch_run.py \
    --run-id <run> --variant c --weights-only --export        # model and models.json into Models/

# 2. The Xcode project, generated from project.yml
brew install xcodegen
cd src/app/ios && xcodegen generate
open JassCardEye.xcodeproj
```

`xcodegen generate` comes after the export: the project lists what is on disk, so a model or a
`models.json` that arrives later is not in the bundle until the project is generated again. Weights
from a local training run go through `src/training/export.py --variant c` instead, which writes no
`models.json`.

**macOS** is the compile guard for the shared sources. No workflow builds it; run it by hand from the
repository root:

```bash
xcodebuild -project src/app/ios/JassCardEye.xcodeproj -scheme JassCardEye -destination 'platform=macOS' build
```

**Outside the YARX team**, set your own `DEVELOPMENT_TEAM` (it appears for the app and the test
target) and `bundleIdPrefix` in `project.yml` before generating. Team `5TV46KLX3W` and the bundle ID
`ch.yarx.JassCardEye` belong to YARX, and signing for a device needs a team you are a member of.

## Texts

Every text a user reads is a key in `l10n/de.json`, with a line of context in `l10n/keys.md`; both apps
are built from that file. `xcodegen generate` runs `src/tools/l10n.py --ios .` first (`preGenCommand`
in `project.yml`), which writes `Localizable.xcstrings` and `InfoPlist.xcstrings` - generated like the
project, not versioned. In the code a text is its key with the German text as the default:

```swift
Text(String(localized: "scan.missing_card", defaultValue: "Karte fehlt?"))
```

The default is what a reader of the code sees and what the app would show for a key the catalog lacks;
`python3 src/tools/l10n.py` from the repository root holds it to `de.json`, checks that every key is
used, and runs in CI. A number interpolated into the default fills the catalog's `%1$lld`, a text its
`%1$@`, in the order they appear. German is the development language; every file in `l10n/` becomes a
language of the catalog and an `.lproj` of the bundle, which is what iOS offers in the app's settings
and what App Store Connect lists. A phone set to any other language sees German. To try a language in
the simulator: `xcrun simctl launch booted ch.yarx.JassCardEye -AppleLanguages "(fr)"`. The developer tools (*Bild*, *Session aufzeichnen*), the model picker a release never
shows and the notes of a build without a model stay inline German: no user sees them.

The Info.plist still carries the German app name and camera question from `project.yml`, because a
catalog only translates keys that exist; the check makes sure the two agree.

## Testing in the simulator

The simulator has no camera, so a **looping video** takes its place and feeds the same frame loop:
stability rule, pile and score behave as on a phone.

- **Never bundled.** A simulator app can read the host's file system, so the video is opened from an
  absolute path given in `JASSCARDEYE_VIDEO` (Xcode: *Edit Scheme → Run → Arguments → Environment
  Variables*; with `xcrun simctl launch`, as `SIMCTL_CHILD_JASSCARDEYE_VIDEO`). Without it the scan
  screen says that no test video is set.
- **Not compiled into a device build.** `VideoFrameSource` and every use of it sit behind
  `#if targetEnvironment(simulator)`, so the shipping app contains no video code at all.
- iPhone videos are HDR. A video composition maps them to Rec. 709; read as SDR they come out pink,
  colours no card has. A recorded session (`session_….mov`) plays back the same way.
- A note on the picture says *Simulator: Testvideo statt Kamera (Endlosschleife).*;
  `JASSCARDEYE_SCREENSHOTS=1` leaves it out for store screenshots (see `store/listing.md`). The
  simulator offers no torch and no lens choice.
- Without a filmed pile, `python3 src/tools/make_test_video.py test-video.mp4` renders one from the card
  scans: twelve French cards laid one by one, the same on every run.

## Demo and purchase

The demo shows the counted points blurred; one purchase makes them readable. The
product is `ch.yarx.jasscardeye.counting`: non-consumable, CHF 5, Family Sharing on.

- **`Store`** is the only place that knows. It reads `Transaction.currentEntitlements` at launch and
  listens to `Transaction.updates` for the app's whole life, so a refund, a family member's purchase
  and an Ask to Buy approved hours later change the screen without a restart. A transaction that fails
  verification unlocks nothing and is not finished, so the App Store offers it again. Nothing is kept
  in `UserDefaults`; offline, StoreKit still has the signed entitlement. The product - name and price -
  is loaded at launch as well; the price is never written down in the app.
- **`ScoreRow(locked:)`** blurs both numbers and gives VoiceOver "Punkte, freischalten"; the callers
  keep the card points out of the caption. The scoring itself knows nothing about the purchase.
- **`PurchaseView`** closes itself once `Store.unlocked` turns true, so a purchase in the middle of a
  count turns the running score sharp. `UnlockButton` leads there from every blurred number.

### Testing the purchase

- **Run from Xcode** (⌘R): the scheme's run action uses `JassCardEye.storekit`, so the product sells
  locally, without an account. *Debug → StoreKit → Manage Transactions* refunds a purchase, approves or
  declines an Ask to Buy, and interrupts a purchase. An archive talks to the real App Store.
- **`Tests/StoreTests.swift`** holds the cases against the same file with `SKTestSession`: the price, a
  purchase, the purchase at the next launch, a start without the App Store, a fresh install, a refund,
  Ask to Buy approved and declined, an interrupted purchase. Run them from Xcode (⌘U) after the app has
  run once. On the iOS 26.5 simulator price, purchase and next launch pass. Everything the session has
  to write - a refund, clearing transactions, Ask to Buy, an interrupted purchase, a simulated offline
  start - fails inside `SKTestSession` with `SKInternalErrorDomain Code=3`, a known StoreKit issue, and
  those cases skip with a reason; the Transaction Manager covers them by hand.
- **`xcodebuild test`** depends on the simulator runtime: on the iOS 26.1 simulator every case passes,
  on others the session cannot save its configuration (`SKInternalErrorDomain Code=3`) and every case
  fails without reaching the app's code. That is why the tests run from Xcode and not in CI.
- **`testWalkthroughByHand`** keeps the test session alive for ten minutes, to walk through the running
  app with the local product; it runs only with `TEST_RUNNER_JASSCARDEYE_WALKTHROUGH=1`.
- **Launched with `xcrun simctl`**, the app has no StoreKit configuration and shows the demo, with
  *Nicht verfügbar* on the purchase page - also what a start without the App Store looks like.
- **On a device or in TestFlight** a purchase is a sandbox purchase against the product in App Store
  Connect.

## Developer tools

*Bild* saves the analysed square of the current frame, *Session aufzeichnen* records a counting
session as video with a recognition log; five taps on *Firma* on the *Über* page show them. Both hang on
`LiveDetectionModel.developerTools`, persisted under `developerTools`. The *Session aufzeichnen* toggle
itself is not persisted, and a recording starts only while the tools are shown.

### Capture a situation

`FrameCapture` writes the square the model was given - not a photo of the table - as a JPEG (quality
0.95) into the app's Documents folder. The context travels in the file name, so it survives AirDrop
and a copy into any folder: `capture_<yyyyMMdd-HHmmss-SSS>_<label>-<confidence %>_<discipline>.jpg`,
with `nichts` in place of label and confidence when the model saw no card, for example
`capture_20260912-173958-895_clubs_6-98_hearts.jpg` - the discipline as a token that does not change
with the language, see `CountingMode.token(deck:)`. The request is served by the next frame on the
camera queue, so the name records what the model made of exactly the picture that was written.

The saved square is a dataset image: the same crop, so it goes straight into the labelling tool as a
validation case, and `src/tools/compare_capture.py` runs it through the Core ML or the LiteRT model.

### Session recording

`SessionRecorder` writes every analysed square of the session - the same rectangle a still gets,
through `FrameGeometry` - as a square H.264 QuickTime movie,
`session_<yyyyMMdd-HHmmss-SSS>_<discipline>.mov`, next to the captures. It follows the analysis rather
than the camera, which is what makes it usable as simulator input. A frame the encoder is not ready
for is skipped; a recording that breaks removes its partial file and says why when the session ends,
because a broken recording and an empty one otherwise look alike.

Beside the movie it writes the recognition log, `session_….csv`: one row per frame in the movie, with
the card, confidence and box the model continued with and whether the frame counted a card. The row is
written only after the writer has taken the frame, with that frame's presentation time, so row n is
frame n and a skipped frame leaves no row. With it goes the session info, `session_….json`, written by
`SessionInfo`: device, system, app, model run and the threshold and stability rule the pile counted with.
When the session ends, `SessionArchive` packs the three into `session_….zip` with a folder of that name
inside - one file per session to copy off the phone - through `NSFileCoordinator`'s `.forUploading`, which
hands a folder over zipped. If that fails, the files stay together in the folder and the note says so. The
columns and keys are in `context/architecture/data-pipeline.md` ("Session recordings"); the dataset tool's
*Analyse sessions* opens the ZIP.

### Getting the files off the phone

**Files app → Auf meinem iPhone → JassCardEye**, or Finder with the phone connected. That takes two
Info.plist keys. `LSSupportsOpeningDocumentsInPlace` has a build setting in `project.yml`;
`UIFileSharingEnabled` has none, so it comes through the base plist XcodeGen writes (`info:` in
`project.yml`), which Xcode merges the generated keys on top of. One without the other lists nothing.
On a Mac, where the app is not sandboxed, the files go to `~/Library/Application Support/JassCardEye`
instead of the real Documents folder.

## Checking on an iPhone

With an Apple ID of the YARX team signed in to Xcode, choosing the phone as run destination and ⌘R is
enough: the team is set in `project.yml`, and automatic signing creates the development certificate
and profile for `ch.yarx.JassCardEye`.

What only a phone shows: the permission prompt and *Kein Kamerazugriff* after refusing, the torch, the
ultra-wide lens where the phone has one, the tap and the tick, the captures in the Files app, and the
phone's own frame rate and inference time in the status bar.

## Checking the scoring

The rules and the pile go wrong silently - a plausible number on screen and a wrong one on the slate -
so both are held to account away from the app. The files involved import nothing but Foundation, so
CI runs both checks on Linux (`.github/workflows/ci.yml`, job *Scoring and pile*, container
`swift:6.2`), and the Android app runs the same cases as `JassScoringTest` and `PileTrackerTest`.

```bash
swiftc -o /tmp/check_scoring src/app/ios/Sources/JassScoring.swift src/app/ios/Sources/JassDeck.swift \
    src/tools/localized_fallback.swift src/tools/check_scoring.swift && /tmp/check_scoring
swiftc -o /tmp/check_pile src/app/ios/Sources/PileTracker.swift src/app/ios/Sources/StabilityRule.swift \
    src/tools/localized_fallback.swift src/tools/check_pile.swift && /tmp/check_pile
```

`localized_fallback.swift` stands in for `String(localized:)`, which Foundation on Linux lacks, with the
German default; on a Mac it compiles to nothing.

- **`check_scoring`:** every discipline totals 152 on both decks, a German card is worth what its French
  counterpart is worth, trump lands on the right suit, each deck owns four suits with a mark of its
  own, a card of the other deck is not a card, each discipline counts with its table, and a full pile
  with the last trick is 157, with the factor moving both sides.
- **`check_pile`:** both stability rules, removing and adding by hand, and *Reset*.
- **`check_recorder`** exercises `SessionRecorder` against the real AVFoundation encoder - every ending,
  and that row n of the log describes frame n, on frames that show their own number - so it runs by hand
  on a Mac and stays out of CI. With `CHECK_RECORDER_KEEP=1` the recording stays behind, for trying
  *Analyse sessions*:

  ```bash
  swiftc -o /tmp/check_recorder src/app/ios/Sources/SessionRecorder.swift src/app/ios/Sources/SessionArchive.swift \
      src/app/ios/Sources/FrameGeometry.swift src/tools/check_recorder.swift && /tmp/check_recorder
  ```

- **The purchase:** `Tests/StoreTests.swift`, from Xcode - see "Testing the purchase".

## Layout

| Path | Purpose |
|---|---|
| `project.yml` | XcodeGen definition; `JassCardEye.xcodeproj`, `Generated-Info.plist` and `JassCardEye.entitlements` are generated from it and not versioned |
| `Sources/` | the app - see "Where the sources meet" |
| `Tests/` | `StoreTests`, the purchase against the StoreKit configuration |
| `JassCardEye.storekit` | the local StoreKit configuration for the run action and the tests |
| `Models/` | `JassCardEye-<variant>.mlpackage` and `models.json` - generated, not versioned |
| `Assets.xcassets/` | the app icon, and the eight suit marks as SVG image sets named after their tokens |
| `Sounds/` | `card-tick.caf`, made by `src/tools/make_card_sound.py` |
| `PrivacyInfo.xcprivacy` | the privacy manifest |
| `Localizable.xcstrings`, `InfoPlist.xcstrings` | the texts, written from `l10n/` by `src/tools/l10n.py` - generated, not versioned |
| `ExportOptions.plist` | how the archive becomes an `.ipa` for App Store Connect |
| `store/` | App Store and TestFlight texts (`listing.md`) and the screenshots |

## Releasing to TestFlight

`src/scripts/release_ios.sh` is the whole release - archive, export, upload - and the iOS job of the
`Release` workflow calls the same script, so a failure can be reproduced on the desk.

```bash
# 1. The model that ships: exactly one variant, c. The script refuses anything else in Models/.
src/training/.venv-export/bin/python src/training/fetch_run.py \
    --run-id <run> --variant c --weights-only --export
rm -rf src/app/ios/Models/JassCardEye-{a,b1,b2}.mlpackage

# 2. A rehearsal, then the real thing - which from a desk needs a build number, see below.
src/scripts/release_ios.sh --no-upload
src/scripts/release_ios.sh --build <n>
```

The script checks the models first and runs `xcodegen generate` itself, then writes the archive, the
`.ipa` and - with a named profile - the export options it used to `artifacts/ios/` (`--out` elsewhere).
It warns when the working tree is dirty, because such a build cannot be reproduced from its commit.
`--version` overrides the version; `--variant` defaults to `c`, the only variant a release ships.

### Version and build number

Both are decided in `src/scripts/lib/release.sh`, which the Android release uses as well, and passed
into the archive; `ExportOptions.plist` sets `manageAppVersionAndBuildNumber` to false so Xcode leaves
them alone.

- **The version** is `MARKETING_VERSION` in `project.yml`, one version for both apps (the Android build
  reads it through the same script). A GitHub release ships under its tag instead, and `--version`
  overrides both; a version that differs from `project.yml` is warned about, because a local build
  would still show the old one.
- **The build number** is the run number of the `Release` workflow plus 1200, decided once by its
  `version` job and taken by both platform jobs, so one release carries one number on the iPhone and on
  Android. Both stores refuse a number that is not higher than the ones already uploaded, and say so
  only at the upload, after the build. The run counter rises whatever branch a run came from; the
  offset keeps the number above every build the stores already hold and covers the one way the counter
  can drop - it restarts when the workflow file is renamed. A re-run keeps its number. The commit count
  does not work: it rises on a branch but not across a squash merge.
- **From a desk** there is no run to take a number from, so a real upload needs `--build <n>` higher
  than every build already uploaded; a rehearsal takes the commit count, since nothing leaves the
  machine.

### The Release workflow

`.github/workflows/release.yml` builds both apps. It starts in two ways:

- **A GitHub release** whose tag starts with `v` (`v1.0.0`; `v1.2` becomes 1.2.0) ships both apps under
  the tag's version. A release under any other tag builds nothing. Bump `MARKETING_VERSION` in the
  same change.
- **By hand** (*Run workflow*, or `gh workflow run release.yml -f platforms=ios`), with these inputs:

| Input | Default | Effect |
|---|---|---|
| `platforms` | `both` | `both`, `ios` or `android` |
| `run_id` | blank | the training run whose model ships; blank takes the newest finished run |
| `variant` | `c` | the variant bundled; a release uses `c` |
| `version` | blank | blank means `MARKETING_VERSION` |
| `track` | `internal` | the Play track; every value but `none` also uploads iOS to TestFlight, `none` only builds and keeps the builds as artefacts |
| `status` | `completed` | Play only |

**Pin `run_id` for a release.** Blank takes whatever finished last, which is not necessarily the model
that was tested; a release names the run (`<run-id>`) its test builds carried. The job prints
`models.json` and keeps it with the `.ipa` as the `ios-build` artefact for 14 days.

**Secrets and variables** of the iOS job. All nine are checked before anything runs, because everything
after is minutes on a macOS runner:

| Name | Kind | What it holds |
|---|---|---|
| `DIST_CERT_BASE64`, `DIST_CERT_P12_PASSWORD` | secret | the Apple distribution certificate as a base64 `.p12`, and its password; imported into a keychain that is thrown away |
| `PROVISIONING_PROFILE` | secret | the App Store distribution profile for `ch.yarx.JassCardEye`, base64 |
| `APPSTORE_API_KEY_ID`, `APPSTORE_ISSUER_ID` | secret | the App Store Connect API key |
| `APPSTORE_API_PRIVATE_KEY` | secret | that key's `.p8`, base64 |
| `AZURE_STORAGE_SAS_TOKEN` | secret | read access to the training runs |
| `AZURE_STORAGE_ACCOUNT` | secret | the storage account of the training runs |
| `AZURE_STORAGE_CONTAINER` | variable | the container of the training runs |

### Signing

Two cases, and the difference is which certificates the machine holds.

- **A developer Mac** holds a development and a distribution certificate, so signing stays automatic:
  `xcodebuild archive` signs with the first and `-exportArchive` re-signs with the second. Nothing has
  to be named.
- **A build machine** holds only the distribution certificate, and automatic signing then stops at the
  archive with *No signing certificate "iOS Development" found*. Naming a profile switches the script
  to manual signing: it passes `CODE_SIGN_STYLE=Manual`, `CODE_SIGN_IDENTITY="Apple Distribution"` and
  the profile to the archive, and exports with a copy of `ExportOptions.plist` that names the same
  profile - the export has to re-sign with the one the archive used.

```bash
src/scripts/release_ios.sh --profile "JassCardEye - Distribution" --build <n>
# or: PROVISIONING_PROFILE_NAME="JassCardEye - Distribution" src/scripts/release_ios.sh --build <n>
```

In Actions the profile comes from `PROVISIONING_PROFILE`, and its name is read out of the profile
itself rather than written down a second time where it could drift; the job also checks that the
profile is for `ch.yarx.JassCardEye`.

**The API key.** `-allowProvisioningUpdates` lets Xcode fetch profiles through the App Store Connect
API key, and `altool` uploads with it. Locally, set `APPSTORE_API_KEY_ID`, `APPSTORE_ISSUER_ID` and
`APPSTORE_API_KEY_PATH`, with the key at `~/private_keys/AuthKey_<key id>.p8`: `altool` looks there by
itself, `xcodebuild` is handed the path, as the workflow does.

### Before the first upload

Once per app, in the developer portal and App Store Connect; `store/listing.md` records what is
entered for JassCardEye:

1. The bundle ID `ch.yarx.JassCardEye`, an App Store distribution profile for it (base64 into
   `PROVISIONING_PROFILE`), and the certificate and API key as secrets.
2. The app record (name, SKU, primary language German) and testers in the internal TestFlight group.
   External testers additionally need Beta App Review, which wants a test description, a contact and
   a privacy policy URL.
3. For the purchase: the Paid Applications Agreement, the in-app purchase with its review screenshot,
   the App Privacy answers, and the purchase added to the submission of the version that uses it.

### What ships

iPhone only (`TARGETED_DEVICE_FAMILY` 1), portrait, iOS 17 and newer. With `@Observable` the
deployment target is a floor of the code as well; it means an A12 (iPhone XS and XR) or newer.

- **Encryption.** The app has no networking code of its own; only StoreKit talks to the App Store, for
  the price, a purchase and a restore. It uses no encryption beyond what iOS provides, so
  `ITSAppUsesNonExemptEncryption` is `NO` in the build settings and App Store Connect does not ask for
  every build.
- **Privacy manifest.** `PrivacyInfo.xcprivacy` declares no tracking and no collected data, and the one
  required-reason API the app uses, `UserDefaults`, under reason CA92.1: only the app's own settings.
  Re-check before an upload:

```bash
grep -rnE 'UserDefaults|creationDate|modificationDate|systemUptime|mach_absolute_time|volumeAvailableCapacity|activeInputModes' src/app/ios/Sources
```

## The icon

`Assets.xcassets/AppIcon.appiconset/icon-1024.png` is a designed icon: a small fanned pile on the green
of a Jass mat, with the detection brackets the live view draws around the card it has found. 1024 ×
1024, opaque and without rounded corners, which is what the App Store requires and what iOS masks
itself. `src/tools/make_app_icon.py` draws a simpler rendition of the same idea and is the fallback;
it refuses to overwrite the committed icon unless passed `--force`. `src/tools/make_android_icon.py`
derives the Android icons from this file, so the two cannot drift apart.

## Implementation notes

- **Threads.** Frames arrive on one serial queue - from the camera, or from the test video - which also
  runs the model. The published state lives in `LiveDetectionModel` (`@Observable`, main actor). What
  the queue needs of it - the settings, the pile, the recorder, a capture request, the active
  recogniser - sits behind `OSAllocatedUnfairLock`s and changes as a whole. A variant's recogniser is
  built off the main thread, kept across sessions because loading a model is slow enough to notice, and
  held together with its variant, so a frame never runs the model of a previous selection.
- **Main-thread budget.** The frame loop publishes only what changed: frame rate and inference time at
  5 Hz, the box per frame but nothing while no card is in view, the pile when a card is committed. The
  scan screen is split into small views, so with `@Observable` a fast-moving value invalidates only the
  view that reads it, and the score bar is not rebuilt thirty times a second. Without this the controls
  compete with camera and model for the main thread on a phone - and a simulator, with no camera and
  more headroom, hides it.
- **Outdated state.** A frame's commit reaches the main actor a moment after it was decided, and a
  correction or *Reset* can land in between. `PileTracker.version` grows with every change and an older
  copy is dropped; the tap and the tick fire only for a copy that was shown. A session token disarms a
  `start()` still waiting on camera or model after the screen has closed.
- **Backpressure.** `alwaysDiscardsLateVideoFrames`: while a frame is analysed, newer ones are dropped
  rather than queued, so the displayed frame rate is the real analysis rate and latency cannot pile up.
  The test video is polled at 30 Hz and skips frames the same way.
- **Orientation.** Buffers are rotated to portrait at the connection (`videoRotationAngle = 90`) and the
  app is portrait-locked, so image, preview (`resizeAspectFill` in a square view) and overlay agree with
  no orientation bookkeeping.
- **Lens and resolution.** The normal lens captures at 1280 × 720, the ultra-wide at 1920 × 1080: it sees
  more table, so the card covers fewer pixels, and the higher resolution puts that detail back before
  the square is scaled to 640. Only the lenses the phone has are offered; a stored lens the phone lacks
  falls back to the normal one, and a change takes effect at the next session.
- **Torch.** Level 0.6, not full power: a hand's width above the pile, the full beam burns out the white
  of a card. The button shows what the device reports, since iOS refuses or drops the torch when the
  phone is hot. It is never persisted and goes off when the session ends.
- **Feedback.** One prepared `UIImpactFeedbackGenerator(.heavy)`, its intensity scaled by the
  *Vibration* slider. The tick is a bundled file through one `AVAudioPlayer` in a `.playback` audio
  session with `.mixWithOthers`: it follows the media volume, sounds in silent mode and lets music play
  on. Both are warmed up when a session opens, so the first card is not the late one.
- **Persisted settings** (`UserDefaults`, under the same keys Android uses in `SharedPreferences`):
  `deck`, `requiredFrames` (1-10, default 3), `stabilityRule` (`run` or `majority`), `cameraLens`
  (`wide` or `ultraWide`), `hapticStrength` and `soundVolume` (0-1, default 0.5) and `developerTools`.
  A stored value outside its range falls back to the default, and the two feedback values are read with
  `object(forKey:)`, because a missing key must not read as 0, which means off. The variant, the
  discipline, last trick, factor and torch are not persisted.
