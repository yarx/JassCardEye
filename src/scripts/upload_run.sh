#!/usr/bin/env bash
# Puts one file or one folder into a run's folder in Azure Blob Storage.
#
# Called repeatedly during a run rather than once at the end, and that is the whole reason the
# results live in a folder instead of a zip: the log, the manifest and each finished variant go up
# as soon as they exist, so a run that dies in its third hour still leaves behind everything the
# first two produced. A zip is only ever complete at the one moment we cannot rely on.
#
#   src/scripts/upload_run.sh artifacts/c             <run-id>/c
#   src/scripts/upload_run.sh artifacts/manifest.json <run-id>/manifest.json
#
# Configured through three environment variables, named the way the Azure tools name them
# themselves - so `az` picks two of them up without being told:
#
#   AZURE_STORAGE_ACCOUNT    the storage account. Unset means "no cloud": every call does nothing,
#                          silently, and reports success. This is the only place that decides that,
#                          so callers do not repeat the check and cannot drift from it.
#   AZURE_STORAGE_CONTAINER  the container, default training-runs
#   AZURE_STORAGE_SAS_TOKEN  a container-scoped SAS; without one, az and azcopy use their own login
#   AZURE_STORAGE_BLOB_ENDPOINT  overrides the endpoint the account name implies. Azure never needs
#                          it; a local emulator does, and that is the only way the success path here
#                          can be rehearsed without a real storage account. A wrong flag would
#                          otherwise surface on a rented GPU.
#
# Doing nothing when the account is unset is the point, not an oversight: the same run script has to
# work on a laptop that has no cloud account at all.
set -euo pipefail

SOURCE=${1:-}
PREFIX=${2:-}
[ -n "$SOURCE" ] && [ -n "$PREFIX" ] || {
    echo "usage: $(basename "$0") <file-or-folder> <path inside the container>" >&2
    exit 2
}

ACCOUNT=${AZURE_STORAGE_ACCOUNT:-}
CONTAINER=${AZURE_STORAGE_CONTAINER:-training-runs}
# A SAS is copied out of the portal sometimes with and sometimes without its leading question mark.
# Both are the same token, and only one of them works when pasted into a URL.
SAS=${AZURE_STORAGE_SAS_TOKEN:-}
SAS=${SAS#\?}

# Silent, not chatty: a run reaches this a dozen times, and a laptop has no account by definition.
# run_training.sh says once at the start where the results will end up.
[ -n "$ACCOUNT" ] || exit 0
if [ ! -e "$SOURCE" ]; then
    echo "   nothing at ${SOURCE} - nothing to upload for ${PREFIX}"
    exit 0
fi
# An empty folder is not an error - a variant that died before writing anything simply has nothing
# to send - but azcopy treats it as one, so it never gets that far.
if [ -d "$SOURCE" ] && [ -z "$(ls -A "$SOURCE")" ]; then
    echo "   ${SOURCE} is empty - nothing to upload for ${PREFIX}"
    exit 0
fi

SERVICE="${AZURE_STORAGE_BLOB_ENDPOINT:-https://${ACCOUNT}.blob.core.windows.net}"
ENDPOINT="${SERVICE%/}/${CONTAINER}"

# Written as --opt=value rather than two words, so the SAS stays a single argument no matter what
# characters it contains.
az_auth() {
    if [ -n "$SAS" ]; then printf '%s' "--sas-token=$SAS"
    else printf '%s' "--auth-mode=login"; fi
}

# The endpoint flag has to be absent rather than empty when there is nothing to override - an empty
# argument is not the same as no argument to the CLI.
run_az() {
    if [ -n "${AZURE_STORAGE_BLOB_ENDPOINT:-}" ]; then
        az "$@" --blob-endpoint "$AZURE_STORAGE_BLOB_ENDPOINT"
    else
        az "$@"
    fi
}

# azcopy first where it exists - it is what the pod image carries and it moves a folder of weights
# far faster than the CLI - and `az` otherwise, which is what a laptop is likely to have. The two
# are interchangeable here, which is what makes a cloud upload testable from a desk.
put() {
    if command -v azcopy >/dev/null 2>&1; then
        DEST="${ENDPOINT}/${PREFIX}"
        [ -n "$SAS" ] && DEST="${DEST}?${SAS}"
        if [ -d "$SOURCE" ]; then
            # The trailing /* means "the contents of", and it is quoted on purpose: azcopy expands
            # it itself. Without it azcopy adds one more level and the run folder reads <run>/c/c/…
            azcopy copy "${SOURCE%/}/*" "$DEST" --recursive --overwrite=true --output-level=essential
        else
            azcopy copy "$SOURCE" "$DEST" --overwrite=true --output-level=essential
        fi
    elif command -v az >/dev/null 2>&1; then
        if [ -d "$SOURCE" ]; then
            run_az storage blob upload-batch --account-name "$ACCOUNT" --destination "$CONTAINER" \
                --destination-path "$PREFIX" --source "$SOURCE" --overwrite --only-show-errors \
                "$(az_auth)" >/dev/null
        else
            run_az storage blob upload --account-name "$ACCOUNT" --container-name "$CONTAINER" \
                --name "$PREFIX" --file "$SOURCE" --overwrite --only-show-errors \
                "$(az_auth)" >/dev/null
        fi
    else
        echo "!  neither azcopy nor az is installed - cannot upload ${PREFIX}" >&2
        return 1
    fi
}

# Three attempts with a growing pause. A rented pod on a shared network drops a connection now and
# then, and the results of a three-hour run are worth more than the seconds this costs.
# The tools' own output is kept and printed only when an attempt fails. azcopy writes a
# twenty-five line summary per transfer, and a run makes dozens of transfers - printing all of it
# buries the run's actual log, while throwing it away is how a failure becomes undiagnosable.
ATTEMPT=1
while :; do
    if OUTPUT=$(put 2>&1); then
        echo "   uploaded ${PREFIX}"
        exit 0
    fi
    echo "!  upload of ${PREFIX} failed (attempt ${ATTEMPT})" >&2
    printf '%s\n' "$OUTPUT" | sed 's/^/   | /' >&2
    [ "$ATTEMPT" -ge 3 ] && break
    sleep $((ATTEMPT * 10))
    ATTEMPT=$((ATTEMPT + 1))
done

echo "!  upload of ${PREFIX} failed three times" >&2
exit 1
