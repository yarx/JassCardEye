# Training

Trains the approaches to recognising the top card of a pile. One script, `train.py`, covers all of
them and runs on CUDA, Apple MPS or CPU - whichever the machine offers. How the training data is made
and what the variants' label formats are is in
[data-pipeline.md](../../context/architecture/data-pipeline.md); how a run happens on a rented GPU is
in [src/training/pipeline.md](pipeline.md).

The thesis compares three approaches, A, B and C, on the same data. After the thesis **only C, the
detector, is pursued.** A and B stay in the code and stay trainable - `train.py`,
`src/scripts/run_training.sh` and the `train` workflow accept them - but only C is developed further,
and only C ships in the apps. Their documentation stays, because the thesis rests on it.

| `--variant` | Approach | Task | Classes | Data | After the thesis |
|---|---|---|---|---|---|
| `c` | C: detector | detect | 72 | `detect/` | pursued - the model both apps ship |
| `b1` | B, stage 1: oriented localisation | obb | 1 (`card`) | `locate/` | trainable, not pursued |
| `b2` | B, stage 2: classify the rectified crop | classify | 72 | `classify_crop/` | trainable, not pursued |
| `a` | A: classify the whole image | classify | 73, including `none` | `classify_full/` | trainable, not pursued |

Each starts from Ultralytics' pretrained YOLO11 weights of the chosen size - `yolo11n.pt`,
`yolo11n-obb.pt` and `yolo11n-cls.pt` for size `n` - which Ultralytics downloads into the working
directory on first use. They are not versioned.

By default **the synthetic dataset trains and the real photos validate**. That split is deliberate:
measuring on real frames is the only number that says anything about the domain gap. If the
validation set is missing, `train.py` warns and validates on the training data instead, and the
number it reports then says nothing about that gap.

## Setup

Prerequisites: .NET 10 (for the dataset) and Python 3.10 or newer.

```bash
# Python environment (macOS / Linux)
python3 -m venv src/training/.venv
src/training/.venv/bin/python -m pip install -r src/training/requirements.txt
```

```powershell
# Python environment (Windows)
py -m venv src\training\.venv
src\training\.venv\Scripts\python -m pip install -r src\training\requirements.txt
```

**NVIDIA GPU (Windows/Linux):** install the CUDA build of PyTorch *before* the requirements,
otherwise pip pulls the CPU-only wheel:

```bash
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124
```

CUDA 12.4 is also what the pipeline's container image is built on.

**Apple Silicon:** nothing extra - the default wheel supports MPS and `--device auto` picks it up.

Ultralytics is pinned to the version the container image installs, so a run on a laptop and a run in
the cloud stay comparable.

### Data

`train.py` expects a generated dataset in `output/dataset/` and the real validation set in
`data/real/val/` with its derived parts rebuilt. How to generate a dataset and rebuild one is in
[src/tools/dataset/README.md](../tools/dataset/README.md). The shortest way to all of it is the
pipeline's own script, which generates, rebuilds, checks and trains in one go:

```bash
src/scripts/run_training.sh --variant c --count 500 --epochs 3 --no-upload
```

It **deletes `output/dataset/` first** and puts a dataset of the requested size in its place. It is a
bash script and has been run on macOS and Linux only.

## Running

```bash
src/training/.venv/bin/python src/training/train.py --variant c
```

Runs 20 epochs unless told otherwise - the same default the pipeline uses. The batch is not the
same: 16 here, 64 in `src/scripts/run_training.sh` and the `train` workflow.

| Option | Default | |
|---|---|---|
| `--variant` | required | `c`, `b1`, `b2` or `a` |
| `--epochs` | `20` | |
| `--size` | `n` | model size `n`, `s`, `m` or `l`; the `train` workflow offers `n`, `s` and `m` |
| `--model` | | explicit weights; overrides `--size` |
| `--imgsz` | `640` | |
| `--batch` | `16` | |
| `--device` | `auto` | `auto`, `cpu`, `mps`, or a CUDA device such as `0` |
| `--workers` | Ultralytics' | dataloader processes; Ultralytics uses 0 on MPS, which makes decoding the bottleneck - try 8 there |
| `--close-mosaic` | Ultralytics' (10) | epochs at the end that run without mosaic augmentation |
| `--warmup-epochs` | Ultralytics' (3) | epochs spent ramping the learning rate up |
| `--train-data` | `output/dataset` | |
| `--val-data` | `data/real/val` | |
| `--name` | the variant | the name of the output folder |
| `--smoke` | off | one epoch at 160 px and batch 4, only to prove that everything runs |
| `--run-id`, `--artifacts` | | the pipeline's hook for its per-epoch report; not needed by hand |

`--close-mosaic` and `--warmup-epochs` count epochs, not fractions, and are handed to Ultralytics
only when given. On a short run that matters: with 10 epochs, the default of 10 switches mosaic off
for the whole training, and three warm-up epochs are 30 % of it - pass about 2-3 and 1.5 there.

Results land in `output/training/<name>/` (git-ignored): `<variant>` by default, `<run id>-<variant>`
when `src/scripts/run_training.sh` calls it, which then copies the folder to `artifacts/<variant>/`.
The datasets are staged for Ultralytics under `output/training/_stage/<variant>/`.

## Figures for the documentation

Runs of the box heads (`c`, `b1`) write publication-ready plots into their output folder:

| File | Shows |
|---|---|
| `results.png` | losses and metrics over the epochs (train and validation) |
| `confusion_matrix.png`, `confusion_matrix_normalized.png` | which cards get confused with which |
| `PR_curve.png`, `P_curve.png`, `R_curve.png`, `F1_curve.png` | precision/recall behaviour over the confidence threshold |
| `val_batch*_pred.jpg` vs `val_batch*_labels.jpg` | predictions next to the ground truth - good qualitative examples |
| `labels.jpg` | class distribution of the training data |
| `results.csv` | the raw numbers |

The classifiers (`a`, `b2`) write only `results.csv` and their weights: Ultralytics 8.3.155 cannot
plot classification training batches, so `train.py` turns plots off for them.

## Exporting for the apps

Both apps run the same weights: `export.py` turns one `best.pt` into Core ML for iOS and macOS and
into LiteRT for Android, and puts each into the folder its app bundles.

| `--format` | Writes into | Files | |
|---|---|---|---|
| `coreml` (default) | `src/app/ios/Models/` | `JassCardEye-<variant>.mlpackage` | NMS is requested for the box heads: built in for the detector, so the app receives ready boxes, while Ultralytics leaves it out for the oriented box and the app decodes that tensor itself; a model with NMS is FP16 even without `--half` (Ultralytics does that), `--half` asks for FP16 for the others |
| `litert` | `src/app/android/app/src/main/assets/models/` | `JassCardEye-<variant>.tflite` and `.labels.txt` | no NMS - the app decodes the raw tensor; always FP32, so `--half` is refused |

Each format has an environment of its own, separate from the training one, because each exporter
needs a different PyTorch: the Core ML set an older one with a `coremltools` that matches it, the
LiteRT set a far newer one for `litert-torch`. Both are pinned, verified on Python 3.13, and use
Ultralytics 8.4.108 while training uses 8.3.155 - the weights file is the same either way. The
comments in the two requirements files record how each set was verified and which combination fails.

```bash
python3.13 -m venv src/training/.venv-export
src/training/.venv-export/bin/pip install -r src/training/requirements-export.txt
src/training/.venv-export/bin/python src/training/export.py --variant c

python3.13 -m venv src/training/.venv-export-android
src/training/.venv-export-android/bin/pip install -r src/training/requirements-export-android.txt
src/training/.venv-export-android/bin/python src/training/export.py --format litert --variant c
```

By default `export.py` takes `output/training/<variant>/weights/best.pt`, which is where a direct
`train.py` run leaves it. Weights from anywhere else need `--weights <path>` with a single
`--variant` - or come through `src/training/fetch_run.py --export`, which downloads a training run
and exports it in one step. Only that way also writes `models.json`, the record of which run a
bundled model came from ([src/training/pipeline.md](pipeline.md)). `--out <dir>` exports somewhere
else, without touching what an app bundles.

The models carry fixed names because the apps look for them: `JassCardEye-c`, `JassCardEye-a`, and
for the two-stage B `JassCardEye-b1` plus `JassCardEye-b2`. A release bundles exactly one variant -
`c` - and the app READMEs describe how one is prepared.

## Proposing a label for the Dataset Tool

`propose.py` lets the Dataset Tool have a label placed by variant B instead of clicked:
B₁ finds the oriented box, B₂ names the card on the rectified crop. The tool starts it once and keeps
it alive, and talks to it in JSON lines - one request, one answer:

```bash
src/training/.venv/bin/python src/training/propose.py --serve --b1 <best.pt> --b2 <best.pt>
# and, to see whether a set of weights answers at all:
src/training/.venv/bin/python src/training/propose.py --op locate --image photo.jpg --b1 … --b2 …
```

It runs the models and nothing else. Which box to take, how to order its corners, which way up the
card is, whether a confidence is good enough to preselect a class - all of that is the tool's
decision and lives in `src/tools/dataset/Viewer/Predict/`, where CI can check it without weights.
The weights come from a run that trained both stages, for example
`fetch_run.py --run-id <run-id> --variant b1 b2 --weights-only`; the tool takes the newest
downloaded run holding both. On CPU by default, which is fast enough for one photo at a time.

## In the cloud and in CI

`src/scripts/run_training.sh` runs the whole thing: the dataset once, every requested variant, the
results collected in `artifacts/` and uploaded to Azure when that is configured. The `train`
workflow runs the same script on a rented GPU. Its inputs, where the results end up and how to fetch
a run back are in [src/training/pipeline.md](pipeline.md).

`ci.yml` trains nothing - GitHub-hosted runners have no GPU - but on every pull request and on main
it generates a small dataset and checks it, so a change that breaks what a training needs fails there
first.

## Notes

- Horizontal and vertical flips are disabled (`fliplr=0`, `flipud=0`). A mirrored card is a different
  card, so those augmentations would teach the model wrong labels.
- **Class names.** The detector (`c`) takes them from the dataset's `classes.txt`, in the class-ID
  order that `JassCardEye.Dataset.Cards` defines, so its indices are the class IDs. `b1` has the
  single class `card`. The classifiers (`a`, `b2`) take their classes from the class folders, which
  Ultralytics numbers alphabetically, so their indices are *not* the class IDs. Every exported model
  carries its own names - in the Core ML metadata, in the LiteRT `.labels.txt` - and the apps read
  those, so the labels match end to end either way.
- **Hard links, not symlinks.** The staged datasets point at the generated images with hard links, or
  copies where a link is impossible. Ultralytics resolves a symlink before it derives a
  classification class from the parent folder, which would throw the class away.
- **Both splits of a classifier get the same class folders**, empty ones included. Ultralytics
  numbers the classes from the training folders alone, so a class that only occurs in validation
  would otherwise be out of range; `train.py` warns when validation holds a class the training data
  lacks.

## Files

| File | Purpose |
|---|---|
| `train.py` | trains one variant |
| `export.py` | Core ML and LiteRT export, with one table of formats |
| `fetch_run.py` | lists training runs in Azure, fetches one, and exports it with its provenance |
| `notify.py` | the pipeline's Telegram messages |
| `propose.py` | runs B₁ and B₂ for the Dataset Tool's label proposals |
| `requirements.txt` | the training environment, `.venv` |
| `requirements-export.txt` | the Core ML export environment, `.venv-export` |
| `requirements-export-android.txt` | the LiteRT export environment, `.venv-export-android` |
