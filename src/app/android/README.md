# JassCardEye App (Android)

The Android app: Kotlin, Jetpack Compose, Material 3, CameraX and LiteRT, laid out for a phone in
portrait. It is the same app as the iOS one in `src/app/ios/` - the same flow, the same screens, the
same German wording, translated into Android idioms. **Both apps move together**: every feature, text
and design change lands on iOS and Android in the same change, and the Kotlin sources mirror
`src/app/ios/Sources/` file for file, which stays the reference for names and structure.

What the app does, on both platforms alike:

- Counting session - home screen, start sheet (deck, discipline, last trick, factor), the session with
  viewfinder, stability rule, pile, corrections and score
- Demo and purchase - the blurred points and the one purchase
- Settings - recognition model, lens, stability, feedback, purchase
- About page - *Über*
- Developer tools - *Bild* and *Session aufzeichnen*, shown by five taps on *Firma*

This file is about *how* the Android app does it, and about every place where Android asked for a
different translation.

## Where the sources meet

Every Swift file has a Kotlin counterpart of the same name, except that a SwiftUI `…View` is a
`…Screen` here and the app entry is `MainActivity` - so a change on one side is found on the other in
one step. The Kotlin sources are in `app/src/main/java/ch/yarx/jasscardeye/`, the tests in
`app/src/test/java/ch/yarx/jasscardeye/`.

| iOS (`src/app/ios/Sources/`) | Android | What it is |
|---|---|---|
| `JassCardEyeApp`, `ContentView` | `MainActivity` | entry point; switches between home and a counting session, provides the `Store` |
| `HomeView` | `HomeScreen` | home screen and the settings sheet (`SettingsSheet`) |
| `StartSheet` | `StartSheet` | *Neue Zählung*: deck, discipline, last trick, factor |
| `ScanView` | `ScanScreen` | the counting session, the manual card picker, the camera-refused notice |
| `AboutView` | `AboutScreen` | *Über* (`AboutSheet`), with the five taps that show the developer tools |
| `PurchaseView` | `PurchaseScreen` | *Punkte freischalten* (`PurchaseSheet`) and the `UnlockButton` |
| `Store` | `Store` | the one purchase: Play Billing instead of StoreKit 2 (an `AndroidViewModel`, handed down as `LocalStore`) |
| `LiveDetectionModel` | `LiveDetectionModel` | the frame loop, score and settings (an `AndroidViewModel`) |
| `PileTracker` | `PileTracker` | the pile and the stability rules, as one value behind one lock |
| `CameraService` | `CameraService` | CameraX instead of AVFoundation |
| `VideoFrameSource` | `VideoFrameSource` | the emulator's stand-in for the camera |
| - | `FrameSource` | camera or test video behind one type, chosen once at launch |
| - | `TensorDecoding` | what Core ML's NMS stage does for iOS: the raw tensors, read and suppressed |
| `CardRecognizer`, `RecognitionVariant`, `Detection` | same names | the recognisers behind one interface, and which variants are bundled |
| `JassScoring`, `JassDeck`, `StabilityRule` | same names | the rules, as data |
| `FrameCapture`, `SessionRecorder`, `SessionInfo`, `SessionArchive`, `FrameGeometry` | same names | *Bild*, *Session aufzeichnen* with its recognition log, session info and ZIP, the one crop |
| - | `RecordingSurface` | draws a recorded frame into the encoder with a presentation time of its own, by which the log is matched to the frames the encoder wrote |
| `CameraLens`, `Haptics`, `CardSound`, `AppInfo` | same names | lens, feedback, version |
| `ScoreRow`, `MultiplierBar`, `SuitMark` | same names | shared views |
| - | `Theme` | the SwiftUI system colours and text styles, written out |
| - | `Form` | the grouped form of the settings and *Über* - SwiftUI's `Form`, written out |
| `src/tools/check_scoring.swift` | `JassScoringTest` | the scoring invariants |
| `src/tools/check_pile.swift` | `PileTrackerTest` | the stability rules and corrections |
| `src/app/ios/Tests/StoreTests.swift` | - | no counterpart: the purchase is tested by hand with license testers |
| `src/tools/check_recorder.swift` | `SessionRecorderTest`, `SessionInfoTest`, `SessionArchiveTest` | the rows of the recognition log, the keys of the session info, the layout of the ZIP; the recording itself is checked by hand on the emulator and a phone |

## Where Android differs, and why

- **The back gesture.** iOS has none; Android does, and it has to do something sensible. In a
  counting session it does what *Fertig* does - the count is finished and handed to the home screen,
  never thrown away by a stray swipe. In a sheet it cancels, like swiping the iOS sheet down; the
  settings and *Über* sheets, when open at full height, first drop to half height (Material's
  behaviour, and the equivalent of dragging the iOS sheet from large to medium).
- **Where captures and recordings go.** The developer tools write not into a private Documents folder
  but, through MediaStore, into the shared `Pictures/JassCardEye` and `Movies/JassCardEye`. That is
  Android's answer to the Files app on iOS: a capture shows up in the gallery, in the Files app and on
  a computer over USB, with no adb and - from Android 10 on - no storage permission. The file names are
  the same contract, `capture_<timestamp>_<result or "nichts">_<mode>.jpg` and
  `session_<timestamp>_<mode>.mp4`. Two consequences: the files outlive an uninstall, and a session is
  an MP4 with H.264 rather than a QuickTime movie. The recognition log of a recording,
  `session_<timestamp>_<mode>.csv`, and its session info (`.json`) cannot follow the video into Movies:
  MediaStore takes a file that is neither picture, video nor audio only into Download or Documents. So when
  a recording ends, `SessionArchive` packs all three into one `session_<timestamp>_<mode>.zip` in
  `Documents/JassCardEye`, the layout iOS writes, and the loose files are discarded while still pending.
  Should packing fail, they are published on their own: the video in Movies, log and info in Documents. Like everything about the developer tools, this stays out of user-facing texts.
- **Sound and haptics.** The tick plays on the media stream, like the sound of a video: the volume
  buttons set it, silent or vibrate mode does not mute it, and because it takes no audio focus, music
  that is already playing keeps playing underneath - the counterpart of the iPhone's `.playback`
  session mixing with others. The tap is a touch vibration, so the phone's touch-feedback setting
  governs it; the settings note names the vibration settings where iOS names the system haptics.
- **Camera refused.** Same notice, same wording; *Einstellungen öffnen* opens the app's page in the
  system settings. It matters more than on iOS: after a second refusal Android stops asking at all.
- **The ultra-wide lens** is offered when the phone exposes one - as a back camera of its own with an
  intrinsic zoom ratio below 0.9, or as a logical camera whose minimum zoom ratio is below 0.9, which
  the app then binds at that minimum.
- **The purchase.** Play Billing instead of StoreKit: the app asks Play what is owned rather than
  listening for transactions, has to acknowledge a purchase within three days, and has no Family
  Sharing to offer. The Billing library brings permissions of its own, `INTERNET` among them - see
  "What ships" under "Releasing to Google Play".
- **Large screens.** Android 16 ignores the portrait lock on screens 600 dp and wider for apps that
  target API 36, and lets them be resized freely. The scan screen is laid out for a portrait phone,
  so the manifest opts out with `PROPERTY_COMPAT_ALLOW_RESTRICTED_RESIZABILITY`. That opt-out only
  exists while targeting 36: before `targetSdk` goes to 37, the layout has to hold in landscape.
- **The look.** Dark only, like the iOS app. Where Material draws a control differently - switch,
  slider, segmented picker, capsule buttons - it is styled after the iOS control rather than left at
  the Material default, so both apps read as the same app.

## Recognition

- **The model** comes from the same `best.pt` as the iOS one. `src/training/export.py --format litert`
  writes `JassCardEye-<variant>.tflite` and `JassCardEye-<variant>.labels.txt` (the class names in ID
  order) into `app/src/main/assets/models/`; `src/training/fetch_run.py --export --format litert` does
  the same for the weights of a training run and also writes `models.json`, which names the run they
  came from - a plain `export.py` leaves `models.json` as it was. FP32 in the file; the GPU delegate
  computes in FP16, the CPU fallback in FP32 - while the iOS detector is stored in FP16 (see
  `context/architecture/recognition.md`). The files are generated and not versioned; without them the app still builds, and a count says
  *Kein Modell in der App - src/training/export.py --format litert ausführen.*
- **Variant C only.** After the thesis only the detector is pursued. A (one classifier over the whole
  square) and B (a locator, then a classifier on the rectified crop) stay in the code and remain
  trainable, and the app offers whatever is bundled - a debug build with their models shows
  *Erkennungsmodell* in the settings. A release bundles exactly one variant, `c`.
- **No NMS in the graph.** The Core ML export bakes NMS into the box heads, the LiteRT export does not,
  so `TensorDecoding.kt` decodes the raw tensor the way Core ML's NMS stage does with the export's
  defaults, which iOS never overrides: candidates from 0.25, suppression **per class** at IoU 0.7, at
  most 20 boxes (a cap only Android has) - and then the live threshold of 0.6 on the best class's score,
  which is what iOS reads too. `TensorDecodingTest` holds it to that. The shapes are checked when a model loads - NCHW input, one output channel per label - so a
  model and a label file that do not belong together fail with a sentence instead of decoding into
  nonsense. B's locator is decoded as `CardLocator` does on iOS (`cx, cy, w, h, confidence, angle`).
- **The runtime** is LiteRT's Interpreter: the GPU delegate when LiteRT's compatibility list vouches
  for the GPU, otherwise - or when the delegate fails to build - the CPU through XNNPACK on four
  threads. Which one runs is logged (`adb logcat -s JassCardEye`). The GPU delegate belongs to the
  thread that created it, so models are loaded on the analysis thread itself. On the emulator XNNPACK
  stays off: on an Apple Silicon host it picks instructions the virtual processor lacks and the app
  dies with SIGILL.
- **One coordinate system.** Preview and image analysis are bound in one use-case group under a 1:1
  viewport, so CameraX gives both the same centred square. The analysis frame (latest only - a frame
  that arrives while one is analysed is dropped, like `alwaysDiscardsLateVideoFrames`) is cut to that
  square and turned upright; the preview fills its square view. Model input, overlay and capture are
  all that one square, exactly the property the iOS viewfinder is built on. The camera is asked for
  1280 × 720, or 1920 × 1080 with the ultra-wide lens, where the card covers fewer pixels. CameraX
  treats the size as a request and may deliver another; which size a frame had is not recorded.
- **Same capture, same model output.** `src/tools/compare_capture.py` runs a capture through both model
  files; a capture saved by the Android app gives the same card at the same confidence on Core ML and
  LiteRT, the boxes equal to three decimals. It checks the models, not the apps: it runs on the CPU with
  its own resizing and reads the best class's score, so Vision's scaling, the Neural Engine's FP16 and
  each app's resampling are not part of it.

### Speed (variant C)

| Where | Frame rate | Inference |
|---|---|---|
| iOS simulator (Apple Silicon Mac) | 24 FPS | 41 ms |
| Android emulator (arm64 on Apple Silicon, CPU, XNNPACK off) | 2–7 FPS | 130–160 ms |
| Nexus 5X (Snapdragon 808, 2015, Android 8.1, CPU - the GPU delegate refuses the model) | 1 FPS | 1.9–3.6 s |
| Pixel 7a | - | 50 ms |

The emulator numbers only show that the pipeline runs end to end; they measure a virtual processor
with its fast kernels switched off, and the Nexus 5X is below the app's floor. The Pixel 7a is the
figure of a current phone; next to it stand the iPhone 17 Pro Max with 15 ms and the iPhone 14 Pro with
30 ms per frame.

## Testing in the emulator

The emulator has no usable camera, so - as in the iOS simulator - a looping video takes its place and
feeds the very same detection path: stability rule, pile and score behave as on a phone. The same two
properties keep it honest:

- The video is **never bundled**. The app reads it from the device's storage: by default
  `test-video.mp4` in its own external files folder, which `adb push` reaches without any permission;
  a different path on the device can be baked in with `-Pjasscardeye.testVideo=/sdcard/…`. That folder
  belongs to the app, so `adb uninstall` deletes the video with it.
- It is active only in a **debug build running on an emulator**. A phone, and every release build,
  uses the camera.

An iPhone clip is HEVC in 10-bit HDR; converted once to 8-bit H.264, the emulator plays it reliably:

```bash
ffmpeg -i IMG_1639.MOV -an -r 30 -c:v libx264 -crf 20 \
  -vf "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=hable,zscale=t=bt709:m=bt709:r=tv,format=yuv420p,crop='min(iw,ih)':'min(iw,ih)',scale=1080:1080" \
  test-video.mp4
adb push test-video.mp4 /sdcard/Android/data/ch.yarx.jasscardeye/files/test-video.mp4
```

**The camera path on the emulator.** The video bypasses CameraX, the permission dialog and the
camera-refused notice. To exercise those without a phone, a debug build can use the emulator's own
emulated camera instead:

```bash
./gradlew assembleDebug -Pjasscardeye.emulatorCamera=true
adb shell pm revoke ch.yarx.jasscardeye android.permission.CAMERA   # to see the dialog and the notice again
```

That way the system dialog, *Kein Kamerazugriff* with *Einstellungen öffnen*, counting through CameraX
and the torch (the emulated camera reports a flash) all run; the lens picker stays hidden, as the
emulator has no ultra-wide camera.

## Store screenshots

The four Play screenshots - *Start* with a last count, *Neue Zählung*, *Zählen*, *Einstellungen* - are
taken on the emulator at 1080 × 2160, with the test video as the camera. What Play shows has to be the
app a buyer gets, so build and app are set up for that first:

- only `JassCardEye-c.*` in `app/src/main/assets/models/` - with more variants the settings show
  *Erkennungsmodell*, which the released app does not have;
- a debug build with `-Pjasscardeye.screenshots=true`, which leaves out the viewfinder's "Emulator:
  Testvideo statt Kamera" note;
- the developer tools hidden: no *Session aufzeichnen* on the home screen (five taps on *Firma* toggle
  them);
- *Stabilität* at 1 while counting, because the emulator analyses only a frame or two per second - and
  back to 3 afterwards.

```bash
cd src/app/android
./gradlew assembleDebug -Pjasscardeye.screenshots=true
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb shell wm size 1080x2160
# A clean status bar at 9:41: Android's system UI demo mode.
adb shell settings put global sysui_demo_allowed 1
adb shell am broadcast -a com.android.systemui.demo -e command enter
adb shell am broadcast -a com.android.systemui.demo -e command clock -e hhmm 0941
adb shell am broadcast -a com.android.systemui.demo -e command battery -e level 100 -e plugged false
adb shell am broadcast -a com.android.systemui.demo -e command network -e wifi show -e level 4
adb shell am broadcast -a com.android.systemui.demo -e command network -e mobile hide
adb shell am broadcast -a com.android.systemui.demo -e command notifications -e visible false
```

Then count the test video for about three minutes and take *Zählen* once most of the pile is on it;
*Fertig* gives *Start* its last count, *Zählen starten* opens *Neue Zählung*, the gear opens
*Einstellungen*. Each one with `adb exec-out screencap -p > store/screenshots/01-start.png` (then
`02-neue-zaehlung`, `03-zaehlen`, `04-einstellungen`). Afterwards set *Stabilität* back to 3 and undo
the emulator with `adb shell wm size reset` and
`adb shell am broadcast -a com.android.systemui.demo -e command exit`.

An emulator without a Google account runs the demo, so the points come out blurred. The committed
screenshots differ from that: they show readable points and, with all four models bundled, the
*Erkennungsmodell* section; taking them again is open (`store/listing.md`).

## Demo and purchase

The demo shows the counted points blurred; one purchase makes them readable. On Android:

- **`Store`** (an `AndroidViewModel`, handed down as `LocalStore`) uses Play Billing 9.1. It asks
  `queryPurchasesAsync` once the connection to Play is up and again whenever the app comes to the
  foreground, hears completed and pending purchases through its `PurchasesUpdatedListener`, and
  **acknowledges** a purchase the moment it sees it - an unacknowledged purchase is refunded by Google
  after three days. The app stores nothing about the purchase and there is no server; Play keeps the
  answer on the phone, so a start without network still knows. The client reconnects to the Play
  service by itself (`enableAutoServiceReconnection`).
- **`ScoreRow(locked = true)`** blurs both numbers and gives TalkBack "Punkte, freischalten".
  `Modifier.blur` needs Android 12; on 10 and 11 the digits are drawn as dots instead.
- **`PurchaseSheet`** shows Play's formatted price, closes itself once unlocked, and has *Kauf
  wiederherstellen*, which is also in the settings. A purchase paid by a slow method stays pending and
  unlocks once Play reports it purchased.

The product `ch.yarx.jasscardeye.counting` is a one-time product, CHF 5.00 in 173 regions. Play has no
Family Sharing for it, so the purchase belongs to the Google account. How it is set up in the Play
Console, and why the Swiss price is entered on its own, is in `store/listing.md`.

**Testing.** The emulator without a Google account answers *billing unavailable*: the demo, with *Nicht
verfügbar* on the purchase sheet. Real purchases need a phone (or an emulator with the Play Store and an
account) whose account is a **license tester** (*Play Console → Einstellungen → Lizenztests*; the list
belongs to the developer account and applies to all of its apps), running a build from the internal or
closed track; a license tester's purchase costs nothing. Checked by hand this way: the demo, a purchase
and restoring it. Not checked: the same account on a second phone, and an unacknowledged purchase
lapsing. There is no automated counterpart of iOS's `StoreTests.swift`.

## Layout

| Path | Purpose |
|---|---|
| `app/build.gradle.kts` | the app module: SDK levels, version, signing, R8, native symbol tables, the `-Pjasscardeye.*` properties |
| `gradle/libs.versions.toml` | every dependency version, the plugins and the NDK in one place |
| `app/src/main/AndroidManifest.xml` | permissions, the camera feature, portrait lock, backup off, the large-screen opt-out |
| `app/src/main/java/ch/yarx/jasscardeye/` | the sources - see "Where the sources meet" |
| `app/src/main/assets/models/` | `JassCardEye-<variant>.tflite` + `.labels.txt` + `models.json` (generated, not versioned) |
| `app/src/main/res/` | suit marks, the tick sound, the launcher icon, the dark window theme - the strings are generated, see "Texts" |
| `app/src/test/` | `JassScoringTest`, `PileTrackerTest`, `TensorDecodingTest`, `SuitMarkTest`, `SessionRecorderTest`, `SessionInfoTest`, `SessionArchiveTest` - plain JVM tests |
| `app/proguard-rules.pro` | no rules of our own: LiteRT's AARs bring the ones its native code needs |
| `store/` | Play Store icon, feature graphic, screenshots, and `listing.md` - what is entered in the Play Console |
| `local.properties`, `keystore.properties` | the SDK path and the upload key of one machine, never committed |

## Build

```bash
# 1. Export the model - its own pinned environment, see src/training/requirements-export-android.txt
python3.13 -m venv src/training/.venv-export-android
src/training/.venv-export-android/bin/pip install -r src/training/requirements-export-android.txt
src/training/.venv-export-android/bin/python src/training/export.py --format litert --variant c
# or the weights of a training run, with models.json naming the run:
# src/training/.venv-export-android/bin/python src/training/fetch_run.py \
#     --run-id <run-id> --variant c --weights-only --export --format litert

# 2. Build and install. Gradle needs JDK 17 or newer; Android Studio's own will do.
cd src/app/android
export JAVA_HOME="/Applications/Android Studio.app/Contents/jbr/Contents/Home"
./gradlew assembleDebug
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

The export needs an environment of its own because Ultralytics exports LiteRT through `litert-torch`,
which wants a far newer torch than the pinned Core ML set can take. The Android SDK is found through
`ANDROID_HOME` or `src/app/android/local.properties` (`sdk.dir=…`, not versioned).

The build takes these properties, each with `-P`:

| Property | For |
|---|---|
| `jasscardeye.testVideo` | another path of the emulator's test video |
| `jasscardeye.emulatorCamera` | the emulator's emulated camera instead of the video |
| `jasscardeye.screenshots` | store screenshots: no emulator note over the viewfinder |
| `jasscardeye.versionName`, `jasscardeye.versionCode` | version and build number, passed by the release script |
| `jasscardeye.allowUnsigned` | a release build without upload key and versionCode, not meant for Play |

Without `versionName` the version is `MARKETING_VERSION` from `src/app/ios/project.yml`, read through
`src/scripts/lib/release.sh`; without `versionCode` a build is number 1, which is what *Über* shows in a
debug build.

**SDK levels.**

- **minSdk 29 (Android 10).** The first release where a capture can be written to `Pictures/` through
  MediaStore without a storage permission - which is what keeps captures reachable without adb.
  CameraX and LiteRT would go lower, but below 29 the app would need
  `WRITE_EXTERNAL_STORAGE` and a second code path for it. It also lands roughly where the iOS app's
  device floor does (iOS 17, A12 from 2018): Android 10 phones are from 2019 on.
- **targetSdk 36**, the level Play currently requires for new apps.
- **compileSdk 37.2**, because the Compose BOM demands it.

**Dependencies**, all in `gradle/libs.versions.toml`:

- Android Gradle Plugin 9.4.0 on Gradle 9.7.1, Kotlin 2.4.20 with its Compose compiler plugin, Java 17
  bytecode (CI builds with JDK 21).
- Jetpack Compose from BOM 2026.09.00 (UI, foundation, Material 3, extended icons), activity-compose
  1.13.0, lifecycle 2.11.0, core-ktx 1.19.0.
- CameraX 1.6.2: core, camera2, lifecycle, view.
- LiteRT 1.4.2 with its GPU delegate - 1.4 rather than 2.x, because the GPU delegate is published for
  the 1.4 line only, and the two have to match.
- Play Billing 9.1.0, for the purchase.
- JUnit 4.13.2 for the JVM tests.
- NDK 28.2.13676358, only to extract the native symbol tables for Play.

## Texts

Every text a user reads is a key in `l10n/de.json`, with a line of context in `l10n/keys.md`; the iOS
app is built from the same file. The `generateStrings` task in `app/build.gradle.kts` runs
`src/tools/l10n.py --android` into `app/build/generated/res/generateStrings/` and adds that folder to
the resources of every variant, so there is no `strings.xml` in `src/main/res` and nothing to commit.
It needs `python3` on the path. A key becomes a resource name with underscores for dots:
`scan.missing_card` is `R.string.scan_missing_card`, read with `stringResource`, and a plural such as
`score.cards` is `R.plurals.score_cards`, read with `pluralStringResource`.

The classes the JVM tests compile - `JassScoring`, `JassDeck`, `StabilityRule`, `CameraLens` - hold
`@StringRes` ids rather than text and stay free of Android types; the screen resolves them, and the view
models, which have the application, do so for their messages. `python3 src/tools/l10n.py` from the
repository root checks that every key is used and that every `R.string` exists; CI runs it.
`localeFilters` lists the languages the app ships - German, for now - which also keeps the libraries'
own texts to them. The developer tools (*Bild*, *Session aufzeichnen*), the model picker a release
never shows and the notes of a build without a model stay inline German: no user sees them.

## Checking on a phone

What the emulator cannot show, in the order worth checking. A debug build on a phone uses the camera -
the video source exists only on an emulator - so the same APK serves.

1. **Install.** Developer options and USB debugging on, the phone connected, then
   `adb install -r app/build/outputs/apk/debug/app-debug.apk`. Keep `adb logcat -s JassCardEye` open.
2. **Backend.** The first count after launch logs `Model C · Detektor loaded on GPU` - or `on CPU`
   where LiteRT's compatibility list does not vouch for the GPU. The model is kept, so later counts log
   nothing.
3. **Speed.** Count a pile for half a minute and read frame rate and inference time off the status
   bar. They belong in the table under "Speed", next to the iPhone's.
4. **Session aufzeichnen.** Show the developer tools with five taps on *Firma* on the *Über* page.
   Switch recording on, count, *Fertig*. The log shows `Recording <side>x<side> with <encoder>` and
   `Session recording: Saved(...)`; the ZIP in *Dokumente/JassCardEye* holds the MP4, which plays, the log
   with as many rows as the note named frames, and the session info. A failure is logged
   with the encoder's refusal - the note under the viewfinder is gone once the session closes.
5. **Ultra-wide lens.** On a phone that has one, *Einstellungen* shows *Kamera → Objektiv*; switching
   widens the picture and counting carries on.
6. **Light** on a real flash, and *Bild* in *Bilder/JassCardEye* - the same checks as in the emulator,
   with a card under a real lens.
7. **The purchase**, with a license tester and a build from the internal track - see "Demo and
   purchase".

On a Nexus 5X (Android 8.1, below the floor) a build with a lowered `minSdk` runs: LiteRT falls back to
the CPU, a recording goes through the Qualcomm hardware encoder, and only publishing the file fails, on
the storage permission Android 8.1 still demands. It is no measure of a current phone.

## Releasing to Google Play

`src/scripts/release_android.sh` is the release up to a signed bundle: it checks that exactly one model
variant is bundled, runs the unit tests, builds the App Bundle with R8, has AGP write the native symbol
tables, and checks the bundle's certificate against the upload key. The Android job of the `Release`
workflow runs the same script and then uploads to Play, so a failure can be reproduced on the desk.

```bash
# 1. The model that ships: variant c, from a named training run. The script refuses more than one variant.
src/training/.venv-export-android/bin/python src/training/fetch_run.py \
    --run-id <run-id> --variant c --weights-only --export --format litert
rm -f src/app/android/app/src/main/assets/models/JassCardEye-{a,b1,b2}.*

# 2. Test, build and sign. A rehearsal takes any versionCode and builds without the upload key.
src/scripts/release_android.sh --rehearsal
src/scripts/release_android.sh --build <versionCode>
```

The bundle lands in `artifacts/android/` as `JassCardEye-<version>-<build>.aab`, with R8's
`mapping-<version>-<build>.txt` and `native-debug-symbols-<version>-<build>.zip` next to it; Play needs
both to turn a crash report back into something readable.

**The workflow.** `.github/workflows/release.yml` builds both apps; its Android job runs the script
above and uploads with `r0adkll/upload-google-play`, pinned to a commit because that step holds a
service account with release rights. Started by hand, it takes:

| Input | Default | What it does |
|---|---|---|
| `platforms` | `both` | `ios` or `android` builds one app on its own |
| `run_id` | blank: the newest finished training run | the run whose weights ship - name it |
| `variant` | `c` | the one variant bundled; a release ships `c` |
| `version` | blank: `MARKETING_VERSION` in `src/app/ios/project.yml` | the version both apps ship under |
| `track` | `internal` | `alpha`, `beta`, `production`, or `none` to build and sign without uploading |
| `status` | `completed` | `draft` leaves the release unpublished in the Play Console |

A GitHub release tagged `v<MAJOR.MINOR.PATCH>` starts the same workflow for both apps with these
defaults - including the newest finished training run, whatever it is. The run that ships is therefore
pinned by starting the workflow by hand with `run_id`, naming the run the builds before it were tested
with. `models.json` travels into the bundle and names the run. Every run with the default track puts the build in front of
the internal testers at once; wider tracks are reached by promoting it in the Play Console.

**The numbers.** One app on two platforms carries one version and one build number, both worked out in
`src/scripts/lib/release.sh`, which the iOS release sources as well. `versionName` is the
`MARKETING_VERSION` from `src/app/ios/project.yml` unless `--version` or the tag of a GitHub release
says otherwise. The `versionCode` is the iOS build number of the same release: the workflow's run
number plus 1200, decided once for both apps - "Releasing to TestFlight" in `src/app/ios/README.md`
has the reasoning. Outside Actions a bundle meant for Play needs `--build`; `--rehearsal` takes the
commit count.

**Native debug symbols.** The app has no native code of its own. LiteRT brings two libraries per ABI,
`libtensorflowlite_jni.so` and `libtensorflowlite_gpu_jni.so`, and their symbol tables are what turn a
raw address in a native crash into a function name; the small native libraries of CameraX and
androidx.graphics ship without such tables. The release build type sets
`debugSymbolLevel = "SYMBOL_TABLE"`, and AGP extracts the tables with the NDK pinned as `ndk` in
`gradle/libs.versions.toml`, which `.github/actions/setup-android` installs; they never reach the
phones. AGP packs them into the bundle under
`BUNDLE-METADATA/com.android.tools.build.debugsymbols/<abi>/<library>.so.sym`
(`unzip -l artifacts/android/*.aab | grep debugsymbols`), but Play does not list them from there after
an upload through the API: such a build shows no *Native Symbole zum Debuggen*. So the same tables also
go up on their own, like `mapping.txt`. The script has AGP write the zip
(`:app:mergeReleaseNativeDebugMetadata` - `bundleRelease` alone does not), refuses a real release without it, and the workflow hands it to the upload as
`debugSymbols`. Whether a build has them shows under *App Bundle Explorer → the build → Downloads*.
What Play can show is what the table holds: for a stripped library, exported function names, not line
numbers.

**Signing.** With Play App Signing, Google holds the key the app is signed with and we sign uploads
with an *upload key* (alias `upload`). Losing it is recoverable through the Play Console; a new one
also means a new fingerprint in `src/scripts/lib/release.sh`. Locally it is found through the
environment (`ANDROID_KEYSTORE_PATH`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`,
`ANDROID_KEY_PASSWORD`) or through `src/app/android/keystore.properties` (never committed):

```properties
storeFile=/absolute/path/to/upload.jks
storePassword=…
keyAlias=upload
keyPassword=…
```

In Actions the Android job reads the repository **secrets** `ANDROID_KEYSTORE_BASE64`
(`base64 -i upload.jks`), `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`,
`AZURE_STORAGE_ACCOUNT` and `AZURE_STORAGE_SAS_TOKEN` to fetch the model, and
`PLAY_SERVICE_ACCOUNT_JSON` for the upload - that one only when the track is not `none`. The storage
container is no secret: it is the repository **variable** `AZURE_STORAGE_CONTAINER`. The job checks
that all of them are set, the variable included, before it builds anything.

Without a key a release build stops: Gradle refuses to shrink or package a release that has no upload
key or no versionCode, unless `-Pjasscardeye.allowUnsigned=true` says the bundle is not meant for Play -
which `--rehearsal` passes when it finds no key, and the CI check always. A signed bundle is then
checked for the *right* key: the script reads the certificate out of the bundle and compares its
SHA-256 with the upload key's, written down in `src/scripts/lib/release.sh`. `jarsigner -verify` is not
that check - it passes a bundle with no signature at all.

**In the Play Console.** What exists, so nothing is set up twice; the entries themselves are in
`store/listing.md`.

- The app `ch.yarx.jasscardeye` - a package name is fixed forever once uploaded - with its default
  store listing in German (de-DE).
- Play App Signing, enrolled with the first bundle. The first bundle of an app is uploaded by hand: the
  Play API accepts uploads only for an app that already has a bundle, and only drafts while the app
  has never been rolled out.
- A Google Cloud service account with the *Google Play Android Developer API*, invited with release
  rights; its JSON key is `PLAY_SERVICE_ACCOUNT_JSON`.
- The internal tester list «Intern», and license testers on the developer account.
- The one-time product `ch.yarx.jasscardeye.counting`. Play lets one be created only once a bundle
  carrying `com.android.vending.BILLING` has been uploaded.
- Managed publishing: an approved release waits for *Veröffentlichen*. Production is reached by
  promoting a build from the internal track.

**What ships.** Android 10 and newer, portrait. Play filters on a camera
(`android.hardware.camera.any`, which a front camera satisfies too) with the flash optional; the app
needs a back camera and says *Keine Kamera gefunden* without one. Nothing restricts it to phones, which
is why the large-screen opt-out exists.

| Permission | Brought in by | For |
|---|---|---|
| `CAMERA` | the app | the recognition - the only permission asked at runtime |
| `VIBRATE` | the app | the tap for a counted card |
| `com.android.vending.BILLING` | Play Billing 9.1.0 | the purchase |
| `INTERNET` | Play Billing 9.1.0, through Google's datatransport library (`com.google.android.datatransport:transport-backend-cct`) | the library; the app's own code does not use it |
| `ch.yarx.jasscardeye.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` | androidx.core | a signature permission for receivers registered as not exported |

`ACCESS_NETWORK_STATE`, which androidx.media3 (through CameraX) and the datatransport libraries bring
along, is removed in the manifest. The app's own code opens no network connection and has no server:
recognition runs on the phone, and the purchase talks to Google Play. `INTERNET` comes with Play
Billing and is kept. The Play data safety answer («keine Daten») and the privacy paragraph on *Über*
(«keine Internetverbindung») rest on the app's own code opening no connection. A build's permissions
show in the merged manifest under `app/build/intermediates/merged_manifest/`, or with `aapt2 dump permissions` on an APK.

Android's cloud backup is off (`allowBackup="false"`), so settings are not copied to a Google account;
a device-to-device transfer while setting up a new phone can still carry them, as there are no
`dataExtractionRules`. R8 shrinks the code with no keep rules of our own - LiteRT's AARs bring the ones
its native code needs.

## The icon

`src/tools/make_android_icon.py` derives everything from the iOS icon, so the two cannot drift apart. An
Android launcher shows an adaptive icon as a 108 dp layer cut to its own shape - a circle on a Pixel -
and only the centre 66 dp is sure to stay visible; the iOS art runs to the edges and its detection
brackets sit near the corners. The script therefore scales the art into that safe zone on the green
of its own border, draws a white silhouette for themed icons, and writes the Play Store icon
(512 × 512) and feature graphic (1024 × 500). The results are committed.

```bash
python3 src/tools/make_android_icon.py
```

## Checking the scoring

```bash
cd src/app/android && ./gradlew :app:testDebugUnitTest
```

`JassScoringTest` holds the Kotlin rules to the same seven invariants, in the same order, as
`src/tools/check_scoring.swift`: every discipline totals 152 on both decks, a German card is worth its
French counterpart, trump lands on the right suit, each deck owns four suits with marks of their own,
the deck filter keeps exactly 36 of the 72 labels, each discipline counts with its table, and a full
pile with the last trick writes 157, with the factor multiplying both sides. `PileTrackerTest` does the
same for the stability rules and the corrections, next to `src/tools/check_pile.swift`, and
`SessionRecorderTest`, `SessionInfoTest` and `SessionArchiveTest` hold the recognition log, the session
info and the ZIP to the format iOS writes, next to `src/tools/check_recorder.swift`. Two checks are
Android's own: `TensorDecodingTest` for the decoding
above, and `SuitMarkTest`, which makes sure every suit has a drawn mark of its own.

CI (`.github/workflows/ci.yml`) runs the Swift checks and these tests on every pull request, so a rule
changed on one platform and not the other fails there. It also builds the debug app and the release
bundle without a key - with no models, the state of a fresh checkout - so R8 and resource shrinking run
on every pull request too.

## Implementation notes

- **Threads.** CameraX delivers frames on one analysis thread, which also runs the model, captures
  and recordings: a single thread, because a GPU delegate has to run where it was created, and because
  it gives the backpressure iOS gets from its serial camera queue. Compose state lives in
  `LiveDetectionModel` and `Store`; the frame loop hands its results to the main thread and publishes
  what changed and no more - frame rate and inference time five times a second, the box per frame,
  nothing while no card is in view - the main-thread budget of the iOS design notes. Atomics and
  `synchronized` stand in for `OSAllocatedUnfairLock`. Only the emulator's `VideoFrameSource` sets its
  preview frame from its own playback thread.
- **Recomposition.** The scan screen is split into small composables that read only their own state,
  the counterpart of the `@Observable` split on iOS: a frame rate that changes does not recompose the
  score bar.
- **Persistence.** `SharedPreferences` (file `settings`) under the keys the iOS app uses in
  `UserDefaults`: `requiredFrames`, `stabilityRule`, `cameraLens`, `deck`, `hapticStrength`,
  `soundVolume`, `developerTools`. A stored value out of range falls back to the default. The
  discipline, the torch and the recognition variant are not kept, and the purchase is never stored by
  the app - Play is asked.
- **The session recorder** feeds an H.264 encoder (30 fps, a key frame every second) through its input
  surface and muxes straight into a pending MediaStore entry in `Movies/JassCardEye`. The file becomes
  visible only when the session closes, and a broken or empty recording is deleted, so nothing
  half-written shows up in the gallery and a long session is not written twice. The square is the
  largest the encoder says it can take, at most 1080 px.
- **`CONFIGURE_FLAG_ENCODE` is not optional.** Without it the codec is set up in the *decoder* role and
  refuses every format with an error that names nothing; on a Qualcomm encoder the log then reads
  `Failed to set standard component role 'video_decoder.avc'`.
- **The torch** runs at 60 % of its strength where the phone lets the level be set - a hand's width
  above the pile, the full beam washes the card out - and is switched off when a session ends.
