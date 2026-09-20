#!/usr/bin/env bash
# What the two release scripts and the release workflow have in common: the version and the build
# number a release ships under, the rule that it bundles one model variant, and the check that an
# Android bundle is really signed with the upload key. Written once, so iOS and Android cannot work
# them out side by side and arrive at different build numbers.
#
# Sourced by src/scripts/release_ios.sh and src/scripts/release_android.sh. Callers that are not bash - the
# `version` job of .github/workflows/release.yml, the Gradle build - run it as a command:
#
#   src/scripts/lib/release.sh marketing-version            # MARKETING_VERSION from src/app/ios/project.yml
#   src/scripts/lib/release.sh resolve [--version <v>] [--build <n>] [--rehearsal]
#   src/scripts/lib/release.sh verify-aab <bundle.aab>      # signed with the upload key?

RELEASE_REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

# The certificate of the Play upload key (`keytool -list -v -keystore upload.jks`). Not a secret - it
# travels in every bundle we upload - and kept here rather than among the repository secrets on
# purpose: whoever can replace the keystore secret could replace a fingerprint stored next to it, while
# a change to this line goes through a pull request. A new upload key, reset through the Play Console,
# means a new line here. ANDROID_UPLOAD_CERT_SHA256 overrides it for one run.
RELEASE_UPLOAD_CERT_SHA256=F6:EE:B2:49:2C:68:41:57:32:9A:07:83:30:24:0D:14:E6:93:F5:80:79:AA:6F:04:75:92:C0:EF:20:F1:2B:C1

# On stderr, so a remark never ends up in a value a caller captures; in Actions as an annotation, so it
# shows on the run page and not only somewhere in the log.
release_warn() {
    if [ -n "${GITHUB_ACTIONS:-}" ]; then echo "::warning::$*" >&2; else echo "!  $*" >&2; fi
}
release_error() {
    if [ -n "${GITHUB_ACTIONS:-}" ]; then echo "::error::$*" >&2; else echo "$*" >&2; fi
}

# ---------------------------------------------------------------- version
# One app on two platforms carries one version. It lives in src/app/ios/project.yml as MARKETING_VERSION, so
# a release that does not change it needs no argument - and the Gradle build reads it through here as
# well, which is why a debug build on Android shows the same number under *Über* as the iPhone.
release_marketing_version() {
    local version
    version=$(sed -n 's/^ *MARKETING_VERSION: *"\{0,1\}\([0-9.]*\)"\{0,1\} *$/\1/p' "$RELEASE_REPO/src/app/ios/project.yml" | head -1)
    [ -n "$version" ] || { release_error "src/app/ios/project.yml has no MARKETING_VERSION."; return 1; }
    echo "$version"
}

# "v1.2", "1.2.3" or "V1" to MAJOR.MINOR.PATCH. App Store Connect takes one to three integers and
# nothing else, so a tag that is not a version stops here, in the first seconds of a run, rather than
# at the upload.
release_normalise_version() {
    local version=${1#[vV]}
    case "$version" in
        *.*.*) ;;
        *.*)   version="$version.0" ;;
        *)     version="$version.0.0" ;;
    esac
    if ! [[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        release_error "'$1' is not a version - expected MAJOR.MINOR.PATCH, optionally with a leading v."
        return 1
    fi
    echo "$version"
}

# The version asked for; else the tag of the GitHub release that started the run - a release ships
# under its own tag, deliberately not under the newest tag in the repository, or releasing an older
# one would ship under the wrong number; else the one in project.yml.
release_version() {  # [version asked for]
    local marketing version
    marketing=$(release_marketing_version) || return 1
    marketing=$(release_normalise_version "$marketing") || return 1
    if [ -n "${1:-}" ]; then
        version=$(release_normalise_version "$1") || return 1
    elif [ -n "${RELEASE_TAG:-}" ]; then
        version=$(release_normalise_version "$RELEASE_TAG") || return 1
    else
        version=$marketing
    fi
    [ "$version" = "$marketing" ] \
        || release_warn "shipping ${version} while src/app/ios/project.yml says ${marketing} - bump MARKETING_VERSION, or a local build shows the old one"
    echo "$version"
}

# ---------------------------------------------------------------- build number
# One number serves as the iOS build number and the Android versionCode. Both stores refuse a number
# that is not higher than the ones already uploaded, and say so only at the upload, after the build.
#
# In Actions it is the run number of the release workflow plus an offset, decided once by its `version`
# job and handed to both platforms - so one release carries one number on both. Why not the commit
# count: it is monotonic on a branch and not across a squash merge. A branch with many commits becomes
# one commit on main, so the first release after merging can compute a number below one that is
# already in TestFlight.
#
# Outside Actions there is no run to take a number from, and one made up on a laptop is a number the
# workflow does not know was spent - so a real release from here asks for --build. A rehearsal takes the
# commit count: nothing is uploaded, so any number will do.
release_build() {  # <build asked for, or empty> <rehearsal: 0|1> [version, for the message]
    local build=${1:-} rehearsal=${2:-0} version=${3:-}
    if [ -n "$build" ]; then
        [[ $build =~ ^[1-9][0-9]*$ ]] || { release_error "'$build' is not a build number."; return 1; }
        echo "$build"
    elif [ "$rehearsal" = 1 ]; then
        git -C "$RELEASE_REPO" rev-list --count HEAD
    else
        release_error "No build number to use."
        {
            echo "The Release workflow passes one. From here, pass --build <n> with a number higher than every"
            echo "build already uploaded${version:+ for ${version}} - App Store Connect lists them under TestFlight,"
            echo "the Play Console in its app bundle explorer."
        } >&2
        return 1
    fi
}

# ---------------------------------------------------------------- what goes in the bundle
# The app decides at launch which variants it can offer from the models it finds, so the contents of
# the models folder are the release decision. A build with all four is the comparison build from the
# thesis and six times the download, and this refuses to make one by accident.
release_require_one_variant() {  # <models folder> <extension> <variant> <command that exports it>
    local dir=$1 ext=$2 variant=$3 export_command=$4 found extra="" v
    found=$(find "$RELEASE_REPO/$dir" -maxdepth 1 -name "JassCardEye-*.${ext}" -exec basename {} ".${ext}" \; 2>/dev/null \
            | sed 's/^JassCardEye-//' | sort | tr '\n' ' ' | sed 's/ $//')
    if [ "$found" != "$variant" ]; then
        for v in $found; do [ "$v" = "$variant" ] || extra="$extra $dir/JassCardEye-$v.*"; done
        {
            echo "$dir holds: ${found:-nothing}"
            echo "A release bundles exactly one variant (${variant}). Export it and remove the rest:"
            echo "  $export_command"
            [ -z "$extra" ] || echo "  rm -rf$extra"
        } >&2
        return 1
    fi
    [ -f "$RELEASE_REPO/$dir/models.json" ] \
        || release_warn "$dir/models.json is missing - the build will not record the run its model came from"
}

# ---------------------------------------------------------------- the signature of an Android bundle
# `jarsigner -verify` is not the check it looks like: on a bundle with no signature at all it notes
# that the jar is unsigned and exits 0, -strict included. So this reads the certificates out of the
# bundle and insists on the upload key's.
#
# keytool exits 0 for an unsigned file too, and says so in the language of the machine, so the verdict
# rests on the fingerprint lines alone: "SHA256:" is a label keytool does not translate. user.language
# only keeps the rest of a log readable.
release_verify_aab() {  # <bundle.aab>
    local aab=$1 expected keytool=keytool out fingerprints
    expected=${ANDROID_UPLOAD_CERT_SHA256:-$RELEASE_UPLOAD_CERT_SHA256}
    expected=$(printf '%s' "$expected" | tr -d '[:space:]' | tr '[:lower:]' '[:upper:]')
    if [ -n "${JAVA_HOME:-}" ] && [ -x "$JAVA_HOME/bin/keytool" ]; then keytool="$JAVA_HOME/bin/keytool"; fi
    [ -f "$aab" ] || { release_error "There is no bundle at $aab."; return 1; }

    out=$("$keytool" -J-Duser.language=en -printcert -jarfile "$aab" 2>&1) \
        || { echo "$out" >&2; release_error "keytool could not read $aab."; return 1; }
    fingerprints=$(printf '%s\n' "$out" | sed -n 's/^[[:space:]]*SHA256:[[:space:]]*//p' \
                   | tr -d '[:space:]' | tr '[:lower:]' '[:upper:]')
    if [ -z "$fingerprints" ]; then
        release_error "$aab is not signed - Play refuses an unsigned bundle."
        return 1
    fi
    # One signer with one certificate: a second fingerprint would glue onto the first and not match.
    if [ "$fingerprints" != "$expected" ]; then
        release_error "$aab is not signed with the upload key."
        {
            echo "   expected SHA-256 ${expected}"
            printf '%s\n' "$out" | sed -n 's/^[[:space:]]*SHA256:[[:space:]]*/   found    SHA-256 /p'
            echo "   A new upload key belongs in RELEASE_UPLOAD_CERT_SHA256 in src/scripts/lib/release.sh."
        } >&2
        return 1
    fi
    echo "   signed with the upload key, SHA-256 ${expected}"
}

# ---------------------------------------------------------------- as a command
if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    set -euo pipefail
    command=${1:-}
    [ $# -eq 0 ] || shift
    case "$command" in
        marketing-version)
            release_marketing_version ;;
        resolve)
            version="" build="" rehearsal=0
            while [ $# -gt 0 ]; do
                case "$1" in
                    --version)   version=$2; shift 2 ;;
                    --build)     build=$2; shift 2 ;;
                    --rehearsal) rehearsal=1; shift ;;
                    *) echo "Unknown option: $1" >&2; exit 1 ;;
                esac
            done
            version=$(release_version "$version")
            build=$(release_build "$build" "$rehearsal" "$version")
            # The format of $GITHUB_OUTPUT, so the workflow can append it as it is.
            echo "version=$version"
            echo "build-number=$build" ;;
        verify-aab)
            release_verify_aab "${1:?verify-aab needs the path of a bundle}" ;;
        *)
            sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2
            exit 1 ;;
    esac
fi
