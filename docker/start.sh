#!/usr/bin/env bash
# The pod's whole life, start to finish, without anybody holding its hand.
#
# The pod does the work itself: it clones the commit it was told to, runs the training, reports as
# it goes, and gives itself back. Nothing connects to it and nothing waits for it - an Actions job
# lives six hours at most, a run can take longer, and the job that created the pod is gone within
# minutes.
#
# Everything arrives as environment variables at creation time:
#
#   RUN_ID            set = do the run and then terminate; unset = idle, so a pod somebody opened
#                     from this image by hand stays alive for RunPod's own console
#   REPO_SLUG         owner/repo
#   REPO_TOKEN        clone credential. Short-lived on purpose - it is used in the first seconds
#                     and the launch job stays alive until it has been.
#   GIT_SHA           the exact commit; not a branch, so the code that trains is the code that ran
#   TRAIN_ARGS        the rest of src/scripts/run_training.sh's arguments, verbatim
#   MAX_RUN_SECONDS   hard ceiling for the training itself
#   POD_TERMINATE_KEY the RunPod key, so the pod can delete itself. Deliberately NOT named
#                     RUNPOD_API_KEY: RunPod populates the RUNPOD_* namespace itself, and a variable
#                     of ours in it is not reliably the one that arrives.
#   AZURE_* TELEGRAM_*  passed straight through; each is optional and silent when absent
#   GITHUB_RUN_ID     the Actions run that created the pod, recorded in the manifest
#
# Deliberately not `set -e`. An error must reach the teardown at the bottom, and a script that
# exits on the first failure would leave the GPU running.
set -uo pipefail

log() { echo "[$(date -u +%H:%M:%S)] $*"; }

# The pod's own account of itself, kept as a file and uploaded with the run. The container's stdout
# alone would not do: nobody can read it once the pod is gone - and the pod is gone precisely when
# something went wrong with the part that happens after the training.
mkdir -p /work
POD_LOG=/work/pod.log
exec > >(tee -a "$POD_LOG") 2>&1

# ---------------------------------------------------------------- giving the GPU back
# Layer one of four. The other three are elsewhere on purpose, because this one cannot cover
# everything: it runs when the script ends, and a container that is killed outright never gets here.
# The heartbeat sweep in cleanup.yml and its absolute age cap catch that - see src/training/pipeline.md.

# The one alarm the pod can raise for itself. There is no completion event, so if the pod cannot
# delete itself the next thing that would notice is the sweep, every half hour and often later -
# and an A100 bills until then. A message on a phone turns it into a problem somebody can act on in
# minutes.
shout() {
    [ -n "${TELEGRAM_BOT_TOKEN:-}" ] && [ -n "${TELEGRAM_CHAT_ID:-}" ] || return 0
    BODY=$(python -c 'import json,sys; print(json.dumps({"chat_id": sys.argv[1], "text": sys.argv[2]}))' \
             "${TELEGRAM_CHAT_ID}" "$1" 2>/dev/null) || return 0
    curl -sf -X POST "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/sendMessage" \
         -H 'Content-Type: application/json' -d "$BODY" >/dev/null 2>&1 || true
}

# Sent before the pod disappears, because after that there is nothing left to ask. A snapshot, not
# the live file: azcopy refuses a file that is still being written to.
ship_pod_log() {
    [ -x /work/src/scripts/upload_run.sh ] || return 0
    [ -n "${RUN_ID:-}" ] || return 0
    cp -f "$POD_LOG" /work/pod.log.snapshot 2>/dev/null || return 0
    /work/src/scripts/upload_run.sh /work/pod.log.snapshot "${RUN_ID}/pod.log" || true
}

# Ten attempts over five minutes, because the alternative to a retry is an A100 nobody is watching.
terminate_self() {
    ship_pod_log
    KEY="${POD_TERMINATE_KEY:-${RUNPOD_API_KEY:-}}"
    if [ -z "$KEY" ] || [ -z "${RUNPOD_POD_ID:-}" ]; then
        log "cannot terminate myself: key ${#KEY} chars, pod id '${RUNPOD_POD_ID:-}'"
        log "leaving it to the sweep in cleanup.yml - the GPU keeps billing until then"
        shout "ACHTUNG ${RUN_ID}: Pod ${RUNPOD_POD_ID:-?} kann sich nicht selbst beenden (kein Key). Die GPU laeuft weiter bis der Sweep greift."
        return 0
    fi
    log "terminating pod ${RUNPOD_POD_ID}"
    for attempt in $(seq 10); do
        CODE=$(curl -s -o /tmp/delete.out -w '%{http_code}' -X DELETE \
                 "https://rest.runpod.io/v1/pods/${RUNPOD_POD_ID}" \
                 -H "Authorization: Bearer ${KEY}")
        case "$CODE" in
            200|201|202|204|404|410) log "pod terminated (HTTP ${CODE})"; return 0 ;;
        esac
        log "termination attempt ${attempt} got HTTP ${CODE}: $(head -c 200 /tmp/delete.out)"
        # Shipped again on the first failure. The copy above was taken before the attempt, so
        # without this the one line that says *why* a pod is still alive would never leave it - and
        # a pod that failed to terminate is exactly the pod nobody can open afterwards.
        if [ "$attempt" = 1 ]; then
            ship_pod_log
            shout "ACHTUNG ${RUN_ID}: Pod ${RUNPOD_POD_ID} laesst sich nicht beenden (HTTP ${CODE}). Die GPU laeuft weiter."
        fi
        sleep 30
    done
    log "could not terminate myself after ten attempts - the sweep in cleanup.yml will catch it"
    shout "ACHTUNG ${RUN_ID}: Pod ${RUNPOD_POD_ID} nach zehn Versuchen nicht beendet. Bitte in RunPod pruefen."
}

if [ -z "${RUN_ID:-}" ]; then
    # No run to do: somebody opened a pod from this image to look around in. Idle rather than exit,
    # or the container would stop the moment it started and there would be nothing to attach to.
    log "no RUN_ID - idling. Nothing will be trained and nothing terminated."
    tail -f /dev/null
fi

trap terminate_self EXIT

# ---------------------------------------------------------------- the code
log "run ${RUN_ID}, commit ${GIT_SHA:-?}"
KEYLEN=${POD_TERMINATE_KEY:-}
log "pod ${RUNPOD_POD_ID:-<unset>}, termination key ${#KEYLEN} chars, ceiling ${MAX_RUN_SECONDS:-72000}s"
cd /work || exit 1
git init -q
git remote add origin "https://x-access-token:${REPO_TOKEN}@github.com/${REPO_SLUG}.git"
if ! git fetch --depth 1 -q origin "${GIT_SHA}"; then
    log "could not fetch ${GIT_SHA} - nothing can run"
    exit 1
fi
git checkout -q FETCH_HEAD
# The token has no business staying in a config file, and it is about to expire anyway.
git remote set-url origin "https://github.com/${REPO_SLUG}.git"
log "checked out $(git rev-parse --short HEAD)"

command -v dotnet >/dev/null || { log "dotnet missing from PATH: $PATH"; exit 1; }
command -v python >/dev/null || { log "python missing from PATH: $PATH"; exit 1; }
command -v azcopy >/dev/null || log "azcopy missing - the run will fall back to the Azure CLI"

# ---------------------------------------------------------------- the run
# Under `timeout`, so a training that hangs cannot hold the GPU until somebody notices. The signal
# is the polite one first: run_training.sh traps it and still writes its final manifest, so even a
# run cut off by its own ceiling leaves a folder that says what happened.
# Exported, not just assigned: the manifest records it so the watchdog can judge this run by the
# limit it was actually given, and that record is written by a grandchild of this script.
export MAX_RUN_SECONDS=${MAX_RUN_SECONDS:-72000}
log "starting the training, ceiling ${MAX_RUN_SECONDS}s"
# shellcheck disable=SC2086 - TRAIN_ARGS is a deliberate word list
timeout --signal=TERM --kill-after=300 "${MAX_RUN_SECONDS}" \
    src/scripts/run_training.sh --run-id "${RUN_ID}" --out /work/artifacts --python python ${TRAIN_ARGS:-}
STATUS=$?
[ "$STATUS" = 124 ] && log "the training hit its ${MAX_RUN_SECONDS}s ceiling and was stopped"
log "training finished with ${STATUS}"

# The trap does the rest.
exit "$STATUS"
