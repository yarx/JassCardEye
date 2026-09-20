#!/usr/bin/env bash
# One command from a checkout to a build in TestFlight.
#
#   src/scripts/release_ios.sh --build <n>          # archive, export and upload
#   src/scripts/release_ios.sh --no-upload          # everything but the upload, for a rehearsal
#   src/scripts/release_ios.sh --version 0.2.0      # a new marketing version
#
# The three steps are archive, export and upload, and each one is a plain xcodebuild or altool
# call - the same commands the GitHub workflow runs, so a failure can always be reproduced here.
#
# What the app ships with is decided before the archive: the release bundles ONE model variant,
# not the four a comparison build carries. The script checks that and refuses to build a 30 MB app
# by accident.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO"
source src/scripts/lib/release.sh

VARIANT=c
VERSION=""
BUILD=""
UPLOAD=1
OUT="$REPO/artifacts/ios"
# The provisioning profile to archive with, by its name in the developer portal. With one, signing
# is manual; without, it stays automatic - which is what a developer Mac holding both a development
# and a distribution certificate wants, and what a build machine holding only the latter cannot use.
PROFILE="${PROVISIONING_PROFILE_NAME:-}"

usage() {
    sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    echo "Options: --variant <c|a|b1|b2> --version <x.y.z> --build <n> --out <dir>"
    echo "         --profile <name of the provisioning profile> --no-upload"
    exit "${1:-0}"
}

while [ $# -gt 0 ]; do
    case "$1" in
        --variant)  VARIANT=$2; shift 2 ;;
        --version)  VERSION=$2; shift 2 ;;
        --build)    BUILD=$2; shift 2 ;;
        --out)      OUT=$2; shift 2 ;;
        --profile)   PROFILE=$2; shift 2 ;;
        --no-upload) UPLOAD=0; shift ;;
        -h|--help)  usage 0 ;;
        *) echo "Unknown option: $1" >&2; usage 1 ;;
    esac
done

command -v xcodegen >/dev/null || { echo "xcodegen is missing: brew install xcodegen"; exit 1; }

# ---------------------------------------------------------------- 1. what goes in the bundle
# The app decides at launch which variants it can offer from the models actually present, so the
# contents of src/app/ios/Models are the release decision - checked in src/scripts/lib/release.sh, shared with
# the Android release.
release_require_one_variant src/app/ios/Models mlpackage "$VARIANT" \
    "src/training/.venv-export/bin/python src/training/fetch_run.py --run-id <id> --variant ${VARIANT} --weights-only --export"

# ---------------------------------------------------------------- 2. version and build number
# The same version and the same build number the Android build of a release gets. How they are chosen,
# and why a local upload has to name its build number while a rehearsal takes any, is written down in
# src/scripts/lib/release.sh.
REHEARSAL=$(( 1 - UPLOAD ))
VERSION=$(release_version "$VERSION")
BUILD=$(release_build "$BUILD" "$REHEARSAL" "$VERSION")
GIT_SHA=$(git rev-parse --short=7 HEAD)

echo "== JassCardEye ${VERSION} (${BUILD}) from ${GIT_SHA}, variant ${VARIANT} =="
if [ -n "$PROFILE" ]; then echo "   signing manually with '${PROFILE}'"; else echo "   signing automatically"; fi
git diff --quiet || echo "!  the working tree is dirty - this build is not reproducible from ${GIT_SHA}"

# ---------------------------------------------------------------- 3. archive
(cd src/app/ios && xcodegen generate >/dev/null)
ARCHIVE="$OUT/JassCardEye-${VERSION}-${BUILD}.xcarchive"
mkdir -p "$OUT"
rm -rf "$ARCHIVE"

# -allowProvisioningUpdates lets Xcode fetch or create the distribution profile through the API
# key, which is why no profile has to be exported and kept in sync by hand.
xcodebuild archive \
    -project src/app/ios/JassCardEye.xcodeproj \
    -scheme JassCardEye \
    -configuration Release \
    -destination 'generic/platform=iOS' \
    -archivePath "$ARCHIVE" \
    -allowProvisioningUpdates \
    ${APPSTORE_API_KEY_ID:+-authenticationKeyID "$APPSTORE_API_KEY_ID"} \
    ${APPSTORE_ISSUER_ID:+-authenticationKeyIssuerID "$APPSTORE_ISSUER_ID"} \
    ${APPSTORE_API_KEY_PATH:+-authenticationKeyPath "$APPSTORE_API_KEY_PATH"} \
    ${PROFILE:+CODE_SIGN_STYLE=Manual} \
    ${PROFILE:+CODE_SIGN_IDENTITY="Apple Distribution"} \
    ${PROFILE:+PROVISIONING_PROFILE_SPECIFIER="$PROFILE"} \
    MARKETING_VERSION="$VERSION" \
    CURRENT_PROJECT_VERSION="$BUILD"

# ---------------------------------------------------------------- 4. export
IPA_DIR="$OUT/ipa-${VERSION}-${BUILD}"
rm -rf "$IPA_DIR"

# The checked-in options describe automatic signing. With a profile they get a copy that names it,
# because the export has to re-sign with the same one the archive used.
OPTIONS=src/app/ios/ExportOptions.plist
if [ -n "$PROFILE" ]; then
    OPTIONS="$OUT/ExportOptions-${BUILD}.plist"
    cp src/app/ios/ExportOptions.plist "$OPTIONS"
    /usr/libexec/PlistBuddy -c "Set :signingStyle manual" \
        -c "Add :provisioningProfiles dict" \
        -c "Add :provisioningProfiles:ch.yarx.JassCardEye string ${PROFILE}" "$OPTIONS" >/dev/null
fi

xcodebuild -exportArchive \
    -archivePath "$ARCHIVE" \
    -exportPath "$IPA_DIR" \
    -exportOptionsPlist "$OPTIONS" \
    -allowProvisioningUpdates \
    ${APPSTORE_API_KEY_ID:+-authenticationKeyID "$APPSTORE_API_KEY_ID"} \
    ${APPSTORE_ISSUER_ID:+-authenticationKeyIssuerID "$APPSTORE_ISSUER_ID"} \
    ${APPSTORE_API_KEY_PATH:+-authenticationKeyPath "$APPSTORE_API_KEY_PATH"}

IPA=$(find "$IPA_DIR" -name '*.ipa' | head -1)
[ -n "$IPA" ] || { echo "No .ipa was exported."; exit 1; }
echo "   ${IPA} ($(du -h "$IPA" | cut -f1))"

# ---------------------------------------------------------------- 5. upload
if [ "$UPLOAD" = 0 ]; then
    echo "== done, not uploaded =="
    exit 0
fi
: "${APPSTORE_API_KEY_ID:?set APPSTORE_API_KEY_ID, APPSTORE_ISSUER_ID and put the .p8 in ~/private_keys}"
: "${APPSTORE_ISSUER_ID:?set APPSTORE_ISSUER_ID}"

echo "== upload to App Store Connect =="
xcrun altool --upload-app --type ios --file "$IPA" \
    --apiKey "$APPSTORE_API_KEY_ID" --apiIssuer "$APPSTORE_ISSUER_ID"

echo "== done =="
echo "   ${VERSION} (${BUILD}) is processing in App Store Connect; TestFlight shows it in a few minutes."
