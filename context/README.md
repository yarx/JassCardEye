# Context directory

This directory holds the collected project knowledge so that people and AI agents can continue
working on the project without asking around. It is **not** part of the official submission - the
thesis lives in `doc/` - but the continuously maintained working basis.

## Reading order

| Order | File | Purpose |
|---|---|---|
| 1 | `vision.md` | What JassCardEye is today, where it is headed and its deliberate non-goals. |
| 2 | `architecture/overview.md` | System layout: dataset tools, training and export, the two counting apps, and the data flow from card scan to app. |
| 3 | `conventions.md` | How work is done here: both apps in one change, naming, language, code, data, workflows and documents. |
| 4 | `glossary.md` | Jass terms and the terms of the ML setup. |
| as needed | `architecture/recognition.md` | Every value from the camera to a counted card - confidence, threshold, stability rule, NMS, precision - whether it is measured, and where iOS and Android differ. |
| as needed | `architecture/data-pipeline.md` | Generator concepts, determinism, dataset format, session recordings. |

## Documentation outside this directory

Each topic has one home, so a fact is changed in one place and linked from everywhere else.

| Where | What it covers |
|---|---|
| `README.md` (repository root) | The entry point to the repository. |
| `CLAUDE.md` (repository root) | Instructions for AI agents working in the repository. |
| `src/training/pipeline.md` | The training run in the cloud: GitHub Actions, the RunPod pod, Azure Blob Storage. |
| `src/training/README.md` | Local training, the model variants, fetching a run and the export for both apps. |
| `src/tools/dataset/README.md` | Reference of the dataset CLI and manual of the Dataset Tool. |
| `src/app/ios/README.md`, `src/app/android/README.md` | Build, run, test and release per platform, and the map of the code. |
| `src/app/ios/store/listing.md`, `src/app/android/store/listing.md` | What is entered in App Store Connect and the Play Console. |
| `src/web/privacy.md` | The privacy policy, published on the project website. |
| `src/web/README.md` | The project website, https://jasscardeye.yarx.ch. |
| `doc/README.md` | The thesis and the work journal (LaTeX). |

## Directory layout

```
context/
├── README.md                          # this document
├── vision.md
├── conventions.md
├── glossary.md
├── architecture/
│   ├── overview.md
│   ├── recognition.md
│   └── data-pipeline.md
```

## Conventions for this directory

**Language:** all documents are written in English.

**File names** in lower case with hyphens, e.g. `data-pipeline.md`.



## Maintenance

This directory goes stale quickly if it is not maintained, and stale context is more harmful than
missing context. When a change to the project makes a statement here outdated, update the document in
the same work step.
