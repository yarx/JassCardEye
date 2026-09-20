#!/usr/bin/env bash
# One epoch's worth of reporting, called from inside the training loop.
#
# It exists so that src/training/train.py knows about exactly one thing instead of about Azure and about
# Telegram. Everything the pipeline does with a progress signal lives on this side of that line, and
# the training script stays a training script.
#
# Reporting between variants is far too seldom to say anything: a variant can take hours, and for
# all that time nothing else would tell a watchdog whether the machine is working or hung, or tell
# anybody watching a phone that it is making progress.
#
#   src/scripts/report_epoch.sh <run folder> <run id> <variant> <epoch> [fitness]
#
# Nothing here may fail loudly. A training that died because a chat service had a bad minute would
# be the most expensive possible way to learn about notifications.
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ARTIFACTS=${1:?run folder}
RUN_ID=${2:?run id}
VARIANT=${3:?variant}
EPOCH=${4:?epoch}
FITNESS=${5:-}
PY=${PYTHON:-python3}

"$PY" "$REPO/src/scripts/write_manifest.py" --artifacts "$ARTIFACTS" --state running \
    --variant "$VARIANT" --epoch "$EPOCH" ${FITNESS:+--fitness "$FITNESS"} || exit 0
"$REPO/src/scripts/upload_run.sh" "$ARTIFACTS/manifest.json" "${RUN_ID}/manifest.json" >/dev/null 2>&1 || true
"$PY" "$REPO/src/training/notify.py" --manifest "$ARTIFACTS/manifest.json" --kind progress || true
