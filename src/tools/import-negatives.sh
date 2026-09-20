#!/usr/bin/env bash
# Imports real-world photos as negative source material for the generator.
#
# The photos come off an iPhone as HEIC, which neither SkiaSharp nor the labelling tool can read, so
# they are converted once here: centre-cropped to a square (the generator and the app both work on a
# square frame) and written as JPEG at a size that leaves headroom above the 640-pixel training
# resolution.
#
# macOS only - it uses `sips`, which ships with the system and is the only dependable HEIC decoder
# available without extra tooling. The result is committed, so no other platform needs to repeat it.
#
#   src/tools/import-negatives.sh ~/Library/Mobile\ Documents/com~apple~CloudDocs/JassCardEye/RealWorldNegatives
#   src/tools/import-negatives.sh <source-dir> [target-dir]

set -euo pipefail

SOURCE=${1:?"usage: import-negatives.sh <source-dir> [target-dir]"}
TARGET=${2:-data/negatives}
SIZE=1024

[ -d "$SOURCE" ] || { echo "Source folder not found: $SOURCE" >&2; exit 1; }
mkdir -p "$TARGET"

count=0
for file in "$SOURCE"/*.[Hh][Ee][Ii][Cc] "$SOURCE"/*.[Jj][Pp][Gg] "$SOURCE"/*.[Jj][Pp][Ee][Gg]; do
    [ -e "$file" ] || continue
    stem=$(basename "$file"); stem=${stem%.*}
    out="$TARGET/$(echo "$stem" | tr '[:upper:]' '[:lower:]').jpg"

    width=$(sips -g pixelWidth "$file" | awk '/pixelWidth/ {print $2}')
    height=$(sips -g pixelHeight "$file" | awk '/pixelHeight/ {print $2}')
    side=$(( width < height ? width : height ))

    # Two passes on purpose: sips applies its options in a fixed internal order, not in the order
    # they are given, so combining -c and -Z in one call silently resizes before cropping and lands
    # at the wrong size. Crop to the centred square first, scale second.
    sips -s format jpeg -s formatOptions 88 -c "$side" "$side" "$file" --out "$out" >/dev/null
    sips -Z "$SIZE" "$out" >/dev/null
    count=$((count + 1))
done

echo "$count negatives written to $TARGET (${SIZE}x${SIZE} JPEG, centre-cropped)"
