#!/usr/bin/env bash
# One training run, start to finish: generate the dataset once, then train each requested variant on
# it in turn, and collect the results.
#
# The same script runs on a laptop and on a rented GPU. That is the point - a run can be rehearsed in
# miniature before it costs anything:
#
#   src/scripts/run_training.sh --variant c --count 500 --epochs 3      # a few minutes on a Mac
#   src/scripts/run_training.sh --variant all --count 500 --epochs 3    # all four, still minutes
#   src/scripts/run_training.sh --variant all                           # the real thing
#
# The dataset is generated on the machine that trains rather than stored anywhere. That is
# affordable because the generator is deterministic: the same seed produces bit-identical images on
# any machine, so seed plus commit are the dataset's identity and every variant sees exactly the
# same data - which is precisely what a comparison between them requires.
#
# The results are the opposite case and do go to Azure Blob Storage, under <run id>/, piece by piece
# as they appear. Nobody watches a run from beginning to end, so that folder has to be the place a
# run lives: log, manifest and each finished variant - see src/training/pipeline.md.
#
# The variants share one dataset, so it is generated once no matter how many are trained. Training
# them in sequence on one machine costs less GPU time than one machine each, and the only thing it
# costs is wall-clock time.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO"

VARIANTS=c
SIZE=n
COUNT=200000
SEED=1
EPOCHS=20
IMGSZ=640
BATCH=64
WORKERS=""
CLOSE_MOSAIC=""
WARMUP_EPOCHS=""
DEVICE=auto
OUT="$REPO/artifacts"
# Both filled in once the arguments are known - see below.
RUN_ID=""
UPLOAD=1
PYTHON=""

usage() {
    sed -n '2,23p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    echo "Options: --variant (comma separated, or 'all') --size --count --seed --epochs --imgsz"
    echo "         --batch --workers --close-mosaic --warmup-epochs --device --out --run-id --python"
    echo "         --no-upload (keep the results local even when Azure is configured)"
    exit "${1:-0}"
}

while [ $# -gt 0 ]; do
    case "$1" in
        --variant|--variants) VARIANTS=$2; shift 2 ;;
        --size)          SIZE=$2; shift 2 ;;
        --count)         COUNT=$2; shift 2 ;;
        --seed)          SEED=$2; shift 2 ;;
        --epochs)        EPOCHS=$2; shift 2 ;;
        --imgsz)         IMGSZ=$2; shift 2 ;;
        --batch)         BATCH=$2; shift 2 ;;
        --workers)       WORKERS=$2; shift 2 ;;
        --close-mosaic)  CLOSE_MOSAIC=$2; shift 2 ;;
        --warmup-epochs) WARMUP_EPOCHS=$2; shift 2 ;;
        --device)        DEVICE=$2; shift 2 ;;
        --out)           OUT=$2; shift 2 ;;
        --run-id)        RUN_ID=$2; shift 2 ;;
        --python)        PYTHON=$2; shift 2 ;;
        --no-upload)     UPLOAD=0; shift ;;
        -h|--help)       usage 0 ;;
        *) echo "Unknown option: $1" >&2; usage 1 ;;
    esac
done

# "all" is the three approaches of the thesis; B needs two runs, the oriented localisation and the
# classifier for the rectified crop.
[ "$VARIANTS" = "all" ] && VARIANTS="c,b1,b2,a"
VARIANT_LIST=$(echo "$VARIANTS" | tr ',' ' ')

# Prefer the project's virtual environment when it exists, so a laptop run needs no arguments.
if [ -z "$PYTHON" ]; then
    if [ -x "$REPO/src/training/.venv/bin/python" ]; then PYTHON="$REPO/src/training/.venv/bin/python"
    else PYTHON=python3; fi
fi

DATASET="$REPO/output/dataset"
mkdir -p "$OUT"

GIT_SHA=$(git -C "$REPO" rev-parse HEAD 2>/dev/null || echo unknown)
# <date>-<commit>: sortable, readable, and it says which code produced the run - the three things a
# folder name in object storage has to do. In UTC, so a laptop and a pod in another region sort into
# one order. The Actions run id goes into the manifest rather than the name, where nobody has to
# recognise it as a number.
[ -n "$RUN_ID" ] || RUN_ID="$(date -u +%Y%m%d-%H%M)-$(printf '%s' "$GIT_SHA" | cut -c1-7)"
GEN_SECONDS=0

# Everything this run prints is kept as a file as well. Nobody watches a live stream of it, so the
# log is an artefact like any other, uploaded as it grows so that a run which dies at three in the
# morning still explains itself.
LOG="$OUT/train.log"
: > "$LOG"
exec > >(tee -a "$LOG") 2>&1

manifest() {  # <state> [variant currently training] - the parameters came with --init
    "$PYTHON" "$REPO/src/scripts/write_manifest.py" --artifacts "$OUT" \
        --state "$1" ${2:+--variant "$2"}
}

upload() {  # <local path> <name inside the run folder>
    # Whether there is a cloud at all is upload_run.sh's decision, not this one's - it does nothing
    # and succeeds when no account is set. --no-upload is the only thing this level owns.
    [ "$UPLOAD" = 1 ] || return 0
    # A failed upload must not end a run that is otherwise healthy: the results are still on this
    # machine, and one unreachable moment is not worth the hours already paid for.
    "$REPO/src/scripts/upload_run.sh" "$1" "${RUN_ID}/$2" \
        || echo "!  ${2} did not reach the cloud - it is still in ${OUT}"
}

# The log is growing under `tee` the whole time, and azcopy refuses a file whose size changes while
# it is being read, so a copy goes up instead of the live file. The copy is also what makes the
# uploaded log complete: it is taken after the line we care about was written, not while it is being
# written.
snapshot_log() {
    cp -f "$LOG" "$OUT/.train.log.snapshot" 2>/dev/null || return 1
    upload "$OUT/.train.log.snapshot" "train.log"
}

checkpoint() {  # <state> [variant] - the manifest, and the log as far as it has got
    manifest "$@"
    upload "$OUT/manifest.json" "manifest.json"
    snapshot_log
}

notify() {  # <kind> [extra line] - never fatal, see src/training/notify.py
    "$PYTHON" "$REPO/src/training/notify.py" --manifest "$OUT/manifest.json" --kind "$1" \
        ${2:+--text "$2"} || true
}

# A run that stops early must not leave a folder claiming to be still running - that is exactly the
# state a watchdog would have to tell apart from a machine that is simply slow.
ENDED=0
VERIFIED=0
on_exit() {
    STATUS=$?
    [ "$ENDED" = 1 ] && return 0
    [ "$STATUS" = 0 ] && return 0
    echo "!  the run stopped early (exit ${STATUS})"
    # Nothing to write into if the way out was never open - retrying an upload that has already
    # failed its check would only add a minute of retries to a run that is over.
    [ "$VERIFIED" = 1 ] || return 0
    checkpoint failed || true
    # The one message that must never be missed: a run that ended without reaching its own summary.
    notify failed "stopped early with exit ${STATUS}" || true
}
# TERM as well as EXIT, because the pod puts a ceiling on the whole run with `timeout`. Without a
# handler the shell is simply killed, the EXIT trap never runs, and a run stopped by its own ceiling
# would leave a folder in the cloud still claiming to be training.
trap 'exit 143' TERM
trap 'exit 130' INT
trap on_exit EXIT

echo "== JassCardEye: ${RUN_ID} =="
echo "   variants:${VARIANT_LIST} at size ${SIZE}"
echo "   ${COUNT} images (seed ${SEED}), ${EPOCHS} epochs at ${IMGSZ}px"

# ---------------------------------------------------------------- 0. can the results get out?
# The first thing that happens, and the one upload that is allowed to end the run. A wrong SAS found
# at the end of a three-hour training costs the whole training; found here it costs seconds. Same
# reasoning as checking the validation labels before generating the dataset.
# The parameters of a run do not change while it runs, so they are given once here and the manifest
# is updated in place from then on - by the checkpoints below and by the training loop itself.
"$PYTHON" "$REPO/src/scripts/write_manifest.py" --artifacts "$OUT" --init \
    --run-id "$RUN_ID" --variants "$VARIANT_LIST" --size "$SIZE" \
    --count "$COUNT" --seed "$SEED" --epochs "$EPOCHS" --imgsz "$IMGSZ" --batch "$BATCH" \
    --git-sha "$GIT_SHA"
if [ "$UPLOAD" = 1 ] && [ -n "${AZURE_STORAGE_ACCOUNT:-}" ]; then
    echo "== check the way out =="
    "$REPO/src/scripts/upload_run.sh" "$OUT/manifest.json" "${RUN_ID}/manifest.json"
    echo "   results will appear under ${AZURE_STORAGE_CONTAINER:-training-runs}/${RUN_ID}/"
else
    echo "   no AZURE_STORAGE_ACCOUNT - this run stays in ${OUT}"
fi
VERIFIED=1

# Said once, here, instead of by every checkpoint. Whether anybody is listening is worth knowing at
# the start of a run; being told four more times that a laptop has no Telegram bot is not.
if [ -n "${TELEGRAM_BOT_TOKEN:-}" ]; then echo "   reporting to Telegram"
else echo "   no TELEGRAM_BOT_TOKEN - this run is silent"; fi
notify started

# ---------------------------------------------------------------- 1. validation set
# Only the hand-made part of the validation set is version-controlled: the images, the labels and
# classes.txt. Everything derived from those - the classification folders and the per-task image
# links - is rebuilt here, so a fresh checkout is complete without anyone remembering a step.
#
# It also has to happen after every label correction: a frame that moved to a different class would
# otherwise keep a stale crop under its old one, and the model would be measured against a label
# nobody holds.
#
# Deliberately before generating: a bad validation label should cost seconds, not the 25 minutes of
# rented machine that rendering 200 000 training images takes.
echo "== rebuild and check validation set =="
dotnet run --project src/tools/dataset/Cli -c Release -- rebuild --dataset "$REPO/data/real/val"
"$PYTHON" src/tools/check_dataset.py "$REPO/data/real/val"

# ---------------------------------------------------------------- 2. dataset, once
echo "== generate dataset =="
checkpoint generating
GEN_START=$(date +%s)
# Cleared first, not written over. The generator replaces the samples it writes, but a previous run
# with a larger count would leave the surplus behind - and training would silently use a dataset of
# a size nobody asked for.
rm -rf "$DATASET"
dotnet run --project src/tools/dataset/Cli -c Release -- generate \
    --out "$DATASET" --count "$COUNT" --seed "$SEED" --size "$IMGSZ" --tasks all
GEN_SECONDS=$(( $(date +%s) - GEN_START ))
echo "   generated in ${GEN_SECONDS}s"
"$PYTHON" "$REPO/src/scripts/write_manifest.py" --artifacts "$OUT" --generate-seconds "$GEN_SECONDS"
# What the dataset actually costs on disk, and what is left. A rented pod has a fixed container disk
# and the standard run is two hundred thousand images; running out of room mid-training is a
# failure that would otherwise only be diagnosed by guessing.
echo "   dataset on disk: $(du -sh "$DATASET" 2>/dev/null | cut -f1), $(df -h "$DATASET" | awk 'NR==2 {print $4}') free"

"$PYTHON" src/tools/check_dataset.py "$DATASET"
checkpoint running

# ---------------------------------------------------------------- 3. train each variant
SUMMARY=""

for VARIANT in $VARIANT_LIST; do
    echo
    echo "== train variant ${VARIANT} =="
    VARIANT_RUN="${RUN_ID}-${VARIANT}"
    VARIANT_OUT="${OUT}/${VARIANT}"
    # Cleared rather than written over, for the same reason the dataset is: an earlier run in the
    # same folder would otherwise leave figures behind that this run never produced, and they would
    # travel to the cloud as if it had.
    rm -rf "$VARIANT_OUT"
    mkdir -p "$VARIANT_OUT"
    checkpoint running "$VARIANT"

    TRAIN_START=$(date +%s)
    # A failing variant must not take the remaining ones with it: the earlier results are already
    # paid for, and a partial comparison still says something.
    set +e
    "$PYTHON" src/training/train.py \
        --variant "$VARIANT" \
        --run-id "$RUN_ID" --artifacts "$OUT" \
        --size "$SIZE" \
        --train-data "$DATASET" \
        --val-data "$REPO/data/real/val" \
        --epochs "$EPOCHS" \
        --imgsz "$IMGSZ" \
        --batch "$BATCH" \
        --device "$DEVICE" \
        --name "$VARIANT_RUN" \
        ${WORKERS:+--workers "$WORKERS"} \
        ${CLOSE_MOSAIC:+--close-mosaic "$CLOSE_MOSAIC"} \
        ${WARMUP_EPOCHS:+--warmup-epochs "$WARMUP_EPOCHS"}
    TRAIN_EXIT=$?
    set -e
    TRAIN_SECONDS=$(( $(date +%s) - TRAIN_START ))
    # Only reported here. Whether the variant finished follows from what it leaves behind, once run.json
    # is written below: a variant can complete and still exit non-zero.
    echo "   variant ${VARIANT} exited ${TRAIN_EXIT} after ${TRAIN_SECONDS}s"

    # Collected even on failure - a run that died halfway still says something about why.
    RUN_DIR="$REPO/output/training/${VARIANT_RUN}"
    [ -d "$RUN_DIR" ] && cp -R "$RUN_DIR/." "$VARIANT_OUT/"

    # Only what this variant achieved. Everything about the run it belongs to - commit, seed,
    # image count, GPU - is in the manifest one folder up, written once instead of once per variant.
    "$PYTHON" src/scripts/write_run_json.py \
        --out "$VARIANT_OUT/run.json" \
        --results "$RUN_DIR/results.csv" \
        --variant "$VARIANT" \
        --train-seconds "$TRAIN_SECONDS" \
        --exit-code "$TRAIN_EXIT" >/dev/null

    # Sent now rather than at the end: a later variant can still take the machine down with it, and
    # what this one produced has already been paid for.
    upload "$VARIANT_OUT" "$VARIANT"
    checkpoint running "$VARIANT"
    notify variant "${VARIANT} exited ${TRAIN_EXIT} after ${TRAIN_SECONDS}s"

    SUMMARY="${SUMMARY}   ${VARIANT}: exit ${TRAIN_EXIT}, ${TRAIN_SECONDS}s\n"
done

# ---------------------------------------------------------------- 4. report
echo
echo "== done =="
printf "%b" "$SUMMARY"
echo "   dataset generated once in ${GEN_SECONDS}s and shared by all variants"
echo "   results in ${OUT}"
[ "$UPLOAD" = 1 ] && [ -n "${AZURE_STORAGE_ACCOUNT:-}" ] \
    && echo "   and under ${AZURE_STORAGE_CONTAINER:-training-runs}/${RUN_ID}/"

# Last of all, and it is what makes the folder readable without guesswork: "running" with a
# timestamp that stopped moving is a run that died, and "failed" means at least one variant did not
# leave the results of a complete training - the manifest's variants say which. Not the exit codes
# above: a variant that wrote everything and then aborted while Python shut down is finished, with a
# note beside its exit code. write_manifest.py decides it and prints the verdict.
#
# The order matters. The manifest is written first because the notification reads it, and the log
# goes up last because everything above has to be in it - including the line saying the last message
# was sent.
VERDICT=$("$PYTHON" "$REPO/src/scripts/write_manifest.py" --artifacts "$OUT" --finish)
FINAL=${VERDICT% *}
FAILURES=${VERDICT#* }
echo "   the run is ${FINAL}"
if [ "$FINAL" = finished ]; then notify finished; else notify failed "${FAILURES} variant(s) did not finish"; fi
upload "$OUT/manifest.json" "manifest.json"
snapshot_log
ENDED=1

exit "$FAILURES"
