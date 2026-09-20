#!/usr/bin/env bash
# One command from a checkout to an Android App Bundle ready for Google Play.
#
#   src/scripts/release_android.sh --build <n>          # test, then build the bundle signed with the upload key
#   src/scripts/release_android.sh --rehearsal          # any versionCode, and unsigned if there is no upload key
#   src/scripts/release_android.sh --version 0.2.0      # a version other than the one in src/app/ios/project.yml
#
# The Android counterpart of src/scripts/release_ios.sh, and the same command the `Release`
# workflow runs, so a failure can always be reproduced here. It stops at the signed .aab: the upload
# to Play happens in the workflow (or by hand in the Play Console), because the Play Developer API
# needs a service account that has no business on a developer machine.
#
# What the app ships with is decided before the build: a release bundles ONE model variant, not the
# four a comparison build carries.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO"
source src/scripts/lib/release.sh

VARIANT=c
VERSION=""
BUILD=""
REHEARSAL=0
OUT="$REPO/artifacts/android"
MODELS_DIR=src/app/android/app/src/main/assets/models

usage() {
    sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    echo "Options: --variant <c|a|b1|b2> --version <x.y.z> --build <n> --out <dir> --rehearsal"
    exit "${1:-0}"
}

while [ $# -gt 0 ]; do
    case "$1" in
        --variant)   VARIANT=$2; shift 2 ;;
        --version)   VERSION=$2; shift 2 ;;
        --build)     BUILD=$2; shift 2 ;;
        --out)       OUT=$2; shift 2 ;;
        --rehearsal) REHEARSAL=1; shift ;;
        -h|--help)   usage 0 ;;
        *) echo "Unknown option: $1" >&2; usage 1 ;;
    esac
done

# Gradle needs a JDK 17 or newer. On a Mac with Android Studio and no JAVA_HOME, use the one it brings.
if [ -z "${JAVA_HOME:-}" ] && [ -d "/Applications/Android Studio.app/Contents/jbr/Contents/Home" ]; then
    export JAVA_HOME="/Applications/Android Studio.app/Contents/jbr/Contents/Home"
fi

# ---------------------------------------------------------------- 1. what goes in the bundle
# The models in the assets are the release decision, exactly as src/app/ios/Models is for iOS - and checked by
# the same code, in src/scripts/lib/release.sh.
release_require_one_variant "$MODELS_DIR" tflite "$VARIANT" \
    "src/training/.venv-export-android/bin/python src/training/fetch_run.py --run-id <id> --variant ${VARIANT} --weights-only --export --format litert"
[ -f "$MODELS_DIR/JassCardEye-${VARIANT}.labels.txt" ] || { echo "$MODELS_DIR/JassCardEye-${VARIANT}.labels.txt is missing - the app cannot name a class without it"; exit 1; }

# ---------------------------------------------------------------- 2. version, versionCode and key
# The same version and the same number the iOS build of a release gets; how they are chosen, and why a
# real release from a desk has to name its versionCode, is written down in src/scripts/lib/release.sh.
#
# A rehearsal is what --no-upload is on iOS: a bundle not meant for Play. Any number will do, and
# without an upload key it stays unsigned. Anything else needs the key, and says so before Gradle has
# spent minutes on a bundle Play would refuse.
VERSION=$(release_version "$VERSION")
BUILD=$(release_build "$BUILD" "$REHEARSAL" "$VERSION")
GIT_SHA=$(git rev-parse --short=7 HEAD)

SIGNED=0
if [ -n "${ANDROID_KEYSTORE_PATH:-}" ] || [ -f src/app/android/keystore.properties ]; then SIGNED=1; fi
GRADLE_ARGS=(-Pjasscardeye.versionName="$VERSION" -Pjasscardeye.versionCode="$BUILD")

echo "== JassCardEye ${VERSION} (${BUILD}) from ${GIT_SHA}, variant ${VARIANT} =="
if [ "$SIGNED" = 1 ]; then
    echo "   signing with the upload key"
elif [ "$REHEARSAL" = 1 ]; then
    echo "!  no upload key - a rehearsal, so the bundle stays unsigned and Play would refuse it"
    GRADLE_ARGS+=(-Pjasscardeye.allowUnsigned=true)
else
    echo "No upload key (ANDROID_KEYSTORE_PATH or src/app/android/keystore.properties) - Play refuses an unsigned bundle." >&2
    echo "See \"Releasing to Google Play\" in src/app/android/README.md; --rehearsal builds one anyway." >&2
    exit 1
fi
git diff --quiet || echo "!  the working tree is dirty - this build is not reproducible from ${GIT_SHA}"

# ---------------------------------------------------------------- 3. test and build
# The scoring check first: a wrong total is the failure nobody sees on screen.
# mergeReleaseNativeDebugMetadata writes native-debug-symbols.zip - bundleRelease alone only puts the symbols
# inside the bundle, and an APK build is the only one that asks for the zip by itself.
(cd src/app/android && ./gradlew --no-daemon :app:testDebugUnitTest :app:bundleRelease :app:mergeReleaseNativeDebugMetadata "${GRADLE_ARGS[@]}")

# ---------------------------------------------------------------- 4. collect and verify
mkdir -p "$OUT"
AAB="$OUT/JassCardEye-${VERSION}-${BUILD}.aab"
cp src/app/android/app/build/outputs/bundle/release/app-release.aab "$AAB"
# R8 renames classes; Play needs this file to turn a crash report back into readable stack traces.
MAPPING=src/app/android/app/build/outputs/mapping/release/mapping.txt
[ -f "$MAPPING" ] && cp "$MAPPING" "$OUT/mapping-${VERSION}-${BUILD}.txt"
# The symbol tables of LiteRT's native libraries, as AGP zips them. The bundle carries them too, but Play
# does not take them from there on an upload through the API, so the workflow uploads this zip on its
# own, next to mapping.txt.
SYMBOLS=src/app/android/app/build/outputs/native-debug-symbols/release/native-debug-symbols.zip
if [ -f "$SYMBOLS" ]; then
    cp "$SYMBOLS" "$OUT/native-debug-symbols-${VERSION}-${BUILD}.zip"
elif [ "$REHEARSAL" = 1 ]; then
    echo "!  no native-debug-symbols.zip - Play would list this build without native symbols"
else
    # Refused rather than warned about: without this a release goes up quietly without them.
    echo "No native-debug-symbols.zip at $SYMBOLS - Play would list this build without native symbols." >&2
    exit 1
fi
echo "   ${AAB} ($(du -h "$AAB" | cut -f1))"

# Not jarsigner -verify, which passes an unsigned bundle - see release_verify_aab.
if [ "$SIGNED" = 1 ]; then release_verify_aab "$AAB"; fi
echo "== done =="
