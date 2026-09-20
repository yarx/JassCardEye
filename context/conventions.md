# Conventions

How code, texts, workflows and documents are written in this repository. Some rules are the owner's
decisions; the rest are what the code does consistently. When code and this file disagree, fix one of
them in the same change. The working rules for AI agents are in the root `CLAUDE.md`.

## Language and user-facing text

- **English** for code, identifiers, comments, commit messages, READMEs and `context/`. German
  exceptions: `src/web/privacy.md` (published), the store notes `src/app/*/store/listing.md`
  (they mirror the consoles) and the thesis in `doc/`.
- **German for everything a user reads**: the apps, the website, the privacy policy, store texts and
  the training notifications. Swiss spelling - never "ß", always "ss" (*Schliessen*, *ausser*).
- The user is addressed with **du** (a group with *ihr*), never *Sie*.
- A label quoted inside a sentence goes in **« »**. Typography: the multiplication sign `×` (×1…×8,
  "Normal (1×)"), the decimal comma ("0,5×"), an ellipsis after a space ("Preis wird geladen …").
  Dashes are not unified: the apps mix " - " and " – "; the website uses " – ".
- On screen the ranks are *Under*, *Ober*, *König*, *Ass* for both decks; suit and trump names follow
  the deck in play (*Herz* on the French deck, *Rosen* on the German one).
- UI strings are written inline in the code; there are no localisation resources.
- Full sentences end with a period; labels, captions and button titles do not. Sheet vocabulary is
  fixed: *Abbrechen* / *Schliessen* lead, *Fertig* trails; actions are short verb phrases
  (*Zählen starten*, *Kauf wiederherstellen*).
- The hidden developer tools (*Bild*, *Session aufzeichnen*) never appear in user-facing text: not in
  the privacy policy, on the website, in store texts, review notes or in-app texts.

## One app on two platforms

- Every feature, behaviour, text and design change lands in `src/app/ios` and `src/app/android` in
  the same change. `src/app/ios/Sources` is the reference the Kotlin files mirror.
- **Mirrored files.** Each Swift source has a Kotlin file of the same name; a SwiftUI `…View` becomes
  a `…Screen`, the app entry is `MainActivity`. A ported Kotlin file starts with
  `// Port of src/app/ios/Sources/<X>.swift.`; Android-only files such as `TensorDecoding.kt` say
  "Android only.".
  `LiveDetectionModel.kt` keeps the members of the Swift model in the same order.
- **Identical settings.** Same persisted keys and raw values on both platforms: `requiredFrames`,
  `stabilityRule` (`run`/`majority`), `cameraLens` (`wide`/`ultraWide`), `deck` (`french`/`german`),
  `hapticStrength`, `soundVolume`, `developerTools`. A stored value that is unusable falls back to its
  default; a missing feedback value must not read as 0, because 0 means off.
- **Identical constants.** Live threshold 0.6 on the best class's score; stability 3 frames in 1-10;
  feedback 0.5; statistics published 5 times a second; JPEG quality 95; file timestamps
  `yyyyMMdd-HHmmss-SSS`; the B locator floor 0.25. Every value on the way from the camera to a counted
  card, and where the two apps cannot be identical - model precision, capture size, torch strength,
  resampling - is in `context/architecture/recognition.md`.
- **Checks exist twice.** `src/tools/check_scoring.swift` ↔ `JassScoringTest`,
  `src/tools/check_pile.swift` ↔ `PileTrackerTest`, same cases in the same order. Scoring and pile
  code imports Foundation only (Swift) and no Android types (Kotlin), so the checks run on Linux.
- Deliberate platform differences (back gesture, where captures go, audio stream, store wording such
  as *Apple-ID* / *Google-Konto*) are listed in `src/app/android/README.md`.
- **Dependencies.** iOS uses Apple's frameworks only. Android keeps every version in
  `gradle/libs.versions.toml`; LiteRT stays on the line its GPU delegate is published for; there are
  no keep rules of our own and no `-dontwarn`.
- **Privacy by construction.** No networking code, no server, no analytics in either app; only
  StoreKit and Play Billing talk to their store. The purchase state is never cached by the app - the
  store's entitlement is the truth. Android removes `ACCESS_NETWORK_STATE`; `INTERNET` arrives only
  with Play Billing.
- **A control appears only when there is a choice**: the model picker with more than one bundled
  variant, the lens picker with a second lens, the torch button with a torch.
- **Look.** Dark only. Colours carry meaning: own points green, opponents orange, the status
  percentage green for a new card and yellow for one already on the pile, recording red, a selected
  cell green with black text; green is the one accent for things to tap. One ground colour. Numbers
  use tabular digits. Nothing that comes and goes may resize the viewfinder (reserved heights,
  overlays on the picture). Android controls are restyled after their iOS counterparts.
- **Suit marks** are drawn images, never Unicode or emoji, one per suit named after its token. They
  are never tinted and sit on a white rounded plate; a card reads as mark plus rank in neutral text.
- **Accessibility.** Every icon-only control has a German label; decorative images have none. A
  blurred score is read as "Punkte, freischalten". A mark carries its suit name; selected cells expose
  their state.
- **Feedback per card** fires on a real commit or a card added by hand, never on a detection, a
  removal or *Reset*. Each is a slider from 0 (off) to 1, 0.5 on a fresh install; letting go plays it
  once; the phone's own haptics and volume rules win.

## Code and comments

- **Comments explain why**, in full English sentences, and describe the code as it is - not how it
  came to be. No `TODO`/`FIXME` markers - open points are written as prose. Swift uses `///`, Kotlin
  KDoc, both `// MARK: -`. UI terms in comments are italic (*Firma*) or quoted ("Fertig").
- **Logging.** iOS: one `os.Logger` (subsystem `ch.yarx.JassCardEye`), no `print`. Android: tag
  `JassCardEye`. Log text is English; text a user sees is German.
- **C# (`src/tools/dataset`).** net10.0 with nullable reference types, file-scoped namespaces,
  `readonly record struct` for values, `sealed record` with `required`/`init` for options, private
  fields `_camelCase`. Numbers are always formatted and parsed with `CultureInfo.InvariantCulture`.
  Enum-to-token mappings are explicit switches, never enum names.
- **Determinism (generator).** Every image draws from its own random stream, splitmix64 of seed and
  index - never `HashCode.Combine`, which changes per process. File lists are sorted ordinally. A change
  to how random numbers are drawn changes every image and has to be stated.
- **Python.** `from __future__ import annotations`; a module docstring with purpose and exact
  invocation; `argparse(description=__doc__)`; `def main() -> int` and `raise SystemExit(main())`;
  the repository root from `Path(__file__).resolve().parents[2]`. Scripts that run in the pod or on
  runners use the standard library only.
- **Shell.** `#!/usr/bin/env bash`, a header comment that doubles as `--help`, `set -euo pipefail`
  (exceptions are commented where they are made), the repository root from `BASH_SOURCE`. Log
  sections as `== section ==`, non-fatal problems prefixed with `!`.
- **Checks fail loudly.** A checker exits non-zero on a failure - a checker that cannot fail is worse
  than none. Reporting (notifications, epoch reports, the sweep's summary) is never fatal; optional
  side channels (Azure, Telegram) are silent no-ops when not configured, decided in one place.
- Timestamps in files and manifests are UTC ISO 8601 with `Z`.

## Data and generated files

- **Class-ID contract:** class ID = suit × 9 + rank, 0-71. The order of the suit and rank enums
  never changes; the German suits follow the French ones, so the French deck is 0-35 and the German
  deck 36-71. A label is `suit_rank` with the tokens, in enum order, `clubs diamonds hearts spades
  acorns roses bells shields` and `6 7 8 9 10 jack queen king ace`.
- **Never committed:** `output/`, `artifacts/`, exported models and `models.json`, the generated Xcode
  project and plist, the thesis PDFs, and the derived parts of a dataset (`classify_*/`, image links,
  `data.yaml`). A dataset's source of truth is `images/`, `detect/labels/`, `locate/labels/` and
  `classes.txt`; `rebuild` derives the rest.
- **Hard links, never symlinks** - Ultralytics resolves symlinks before deriving a label path.
- A generated asset that is committed (icons, the tick sound, the real negatives) sits next to the
  script that makes it; the script is its record.
- Cards are never mirrored in training; photos used as negatives may be.

## Versions and releases

- `MARKETING_VERSION` in `src/app/ios/project.yml` is the version of both apps. The build number is
  the Release workflow's run number + 1200 for both stores - raise the offset if a store ever refuses a
  number as too low, never lower or remove it.
- A release bundles exactly one recognition variant, C. After the thesis only C is pursued; A and B
  stay in the code and remain trainable.
- The release scripts in `src/scripts` are exactly what the workflow runs, so a failure can be
  reproduced on a desk. Pin `run_id` when the shipped model must be a specific training run.

## GitHub workflows

- Every step name starts with an emoji; every workflow opens with a prose comment saying what it does
  and why.
- GitHub's own actions (`actions/*`) are referenced by major tag. **Every other action is pinned to a
  commit SHA** with its version as a comment: a tag can be moved to other code, and each of them holds
  something worth taking - the Play service account, the website's deployment token, the right to
  push the trainer image - or, like `setup-gradle`, runs inside a build that signs. To update one, look
  up the new release's commit and replace both.
- Anything from outside - dispatch inputs, secrets, variables, step outputs, branch and repository
  names - reaches a script only through `env:`, never as `${{ }}` inside `run:`. Dispatch inputs are
  validated before use.
- Every workflow or job declares least-privilege `permissions:`, every checkout sets
  `persist-credentials: false`, and every job has a `timeout-minutes` well above its usual time, so a
  job that hangs ends by itself.
- Expensive jobs check their secrets and variables first; run blocks start with `set -euo pipefail`;
  results go to `$GITHUB_STEP_SUMMARY`, problems to `::error::` and `::warning::` annotations.
- **Nothing is published from a pull request or a branch**: the website goes live and the trainer
  image is built only from `main`. Trainings and releases are started by hand and may run from a
  branch.
- **Secrets** live in the repository's Actions secrets and variables, never in files, logs or
  documents. Promo codes and phone numbers never go into the repository either.
- CI skips a change that touches only documents, the thesis or the website (`paths-ignore` in
  `ci.yml`).

## Documents

- **Where things live.** READMEs next to the code say how to build, run, test and release;
  `context/` says what and why; `src/training/pipeline.md` the cloud training run; `src/web/privacy.md` the
  privacy policy.
- **Style.** Prose that explains why, short paragraphs, lines wrapped at about 100 characters. Name
  files by path; no line numbers, which go stale at the next commit. No history: a document describes
  the project as it is, and says why where that helps.
- **Sync points** - a change on one side updates the other in the same change:
  - `src/web/privacy.md` ↔ `src/web/src/app/pages/datenschutz.html` ↔ the privacy text on the *Über*
    page of both apps
  - the *Über* page of both apps ↔ the website's licence page
  - the store descriptions (`src/app/*/store/listing.md`) ↔ the website's home and support pages
  - the scan rules on the website's *Anleitung* ↔ the training data (`context/architecture/data-pipeline.md`)
  - the persisted settings ↔ the settings named in the privacy policy
