#!/usr/bin/env bash
# Prepares the "ffmpeg" folder installed with KATALOG (owner, 8. 11. 2026: FFmpeg also in KATALOG's installer, so it
# works without UNIVERSUM; FFmpeg of UNIVERSUM or a folder chosen in the settings is preferred):
#   ffmpeg.exe, ffprobe.exe  FFmpeg (LGPL 2.1+) built here from its official source at a fixed commit
#   COPYING.LGPLv2.1, LICENSE.md, SOURCE.txt  its licence and where its source is
#   manifest.json            SHA-256 of every file; KATALOG starts its copy only when they all match
# and, for the release, the complete FFmpeg source with the build script (the LGPL asks for it to be offered with the
# program).
#   tools/ffmpeg/prepare.sh <ffmpeg folder> <source folder>
# Needs (Ubuntu): mingw-w64 nasm libz-mingw-w64-dev xz-utils python3 git
set -euo pipefail

FFMPEG_VERSION=9.0.2
FFMPEG_COMMIT=946fcce07b6dcd0331c8cc609192aeff5e1924f8

here="$(cd "$(dirname "$0")" && pwd)"
out="$(mkdir -p "$1" && cd "$1" && pwd)"
source_out="$(mkdir -p "$2" && cd "$2" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# FFmpeg: official repository (mirror of git.ffmpeg.org) at the release commit
git -c advice.detachedHead=false clone -q --depth 1 --branch "n$FFMPEG_VERSION" https://github.com/FFmpeg/FFmpeg "$work/ffmpeg"
[ "$(git -C "$work/ffmpeg" rev-parse HEAD)" = "$FFMPEG_COMMIT" ] || { echo "FFmpeg n$FFMPEG_VERSION is not commit $FFMPEG_COMMIT" >&2; exit 1; }
"$here/build-ffmpeg.sh" "$work/ffmpeg" "$work/build"
cp "$work/build/ffmpeg.exe" "$work/build/ffprobe.exe" "$out/"
cp "$work/ffmpeg/COPYING.LGPLv2.1" "$work/ffmpeg/LICENSE.md" "$out/"
zlib_version="$(sed -n 's/^#define ZLIB_VERSION "\(.*\)"$/\1/p' /usr/x86_64-w64-mingw32/include/zlib.h)"
cat > "$out/SOURCE.txt" <<TXT
FFmpeg $FFMPEG_VERSION (https://ffmpeg.org), licence: GNU Lesser General Public License 2.1 or later (COPYING.LGPLv2.1).
Built for KATALOG from the official source, commit $FFMPEG_COMMIT (tag n$FFMPEG_VERSION),
with mingw-w64 (GCC) and zlib $zlib_version (zlib licence) linked statically, by tools/ffmpeg/build-ffmpeg.sh.

The complete source of this build (FFmpeg and the build script) is attached to every KATALOG release on GitHub
as ffmpeg-$FFMPEG_VERSION-source.tar.xz: https://github.com/tarzanislav/katalog/releases
FFmpeg's own source: https://git.ffmpeg.org/ffmpeg.git (commit $FFMPEG_COMMIT), https://ffmpeg.org/download.html

configure: $(cat "$work/build/ffmpeg-configuration.txt")
TXT

# the source offered with the program: FFmpeg at the commit plus the build scripts
git -C "$work/ffmpeg" archive --format=tar --prefix="ffmpeg-$FFMPEG_VERSION/" HEAD > "$work/source.tar"
mkdir -p "$work/scripts/ffmpeg-$FFMPEG_VERSION/katalog-build"
cp "$here/build-ffmpeg.sh" "$here/prepare.sh" "$work/scripts/ffmpeg-$FFMPEG_VERSION/katalog-build/"
tar -rf "$work/source.tar" -C "$work/scripts" "ffmpeg-$FFMPEG_VERSION/katalog-build"
xz -9 -T0 -c "$work/source.tar" > "$source_out/ffmpeg-$FFMPEG_VERSION-source.tar.xz"

# the manifest: KATALOG checks every file before it starts its copy of FFmpeg
FFMPEG_VERSION="$FFMPEG_VERSION" python3 - "$out" <<'PY'
import hashlib, json, os, sys
folder = sys.argv[1]
files = {}
for name in sorted(os.listdir(folder)):
    if name == "manifest.json":
        continue
    with open(os.path.join(folder, name), "rb") as f:
        files[name] = hashlib.sha256(f.read()).hexdigest().upper()
json.dump({"ffmpeg": os.environ.get("FFMPEG_VERSION", ""), "files": files}, open(os.path.join(folder, "manifest.json"), "w"), indent=2)
PY
ls -l "$out" "$source_out"
