#!/usr/bin/env bash
# Builds FFmpeg for 64-bit Windows (cross-compiled with mingw-w64 on Linux) for KATALOG: LGPL (no --enable-gpl, no
# external libraries but zlib) and trimmed to what KATALOG needs – ffprobe reads every format FFmpeg knows (all
# demuxers, decoders and parsers), ffmpeg makes JPEG previews (scale, mjpeg) and, for KATALOG's self-test, an invented
# video (lavfi test picture and tone into Matroska). Needs: mingw-w64, nasm, libz-mingw-w64-dev.
#   tools/ffmpeg/build-ffmpeg.sh <FFmpeg source folder> <output folder>
set -euo pipefail

source_dir="$(cd "$1" && pwd)"
output="$(mkdir -p "$2" && cd "$2" && pwd)"
build="$(mktemp -d)"
trap 'rm -rf "$build"' EXIT

cd "$build"
"$source_dir/configure" \
  --enable-cross-compile --cross-prefix=x86_64-w64-mingw32- --arch=x86_64 --target-os=mingw32 \
  --disable-autodetect --enable-zlib --enable-w32threads \
  --disable-everything \
  --enable-decoders --enable-demuxers --enable-parsers --enable-bsfs \
  --enable-protocol=file,pipe \
  --enable-encoder=mjpeg,pcm_s16le \
  --enable-muxer=image2,matroska,null \
  --enable-indev=lavfi \
  --enable-filter=buffer,buffersink,abuffer,abuffersink,format,aformat,null,anull,copy,acopy,scale,aresample,setsar,trim,atrim,setpts,asetpts,fps,testsrc,sine \
  --disable-network --disable-ffplay --disable-doc --disable-debug \
  --extra-ldflags=-static
make -j"$(nproc 2>/dev/null || echo 4)"
cp ffmpeg.exe ffprobe.exe "$output/"
# an LGPL build: neither GPL nor non-free parts
grep -E '^#define (CONFIG_GPL|CONFIG_NONFREE|CONFIG_VERSION3) ' config.h | tee "$output/ffmpeg-license-config.txt"
if ! grep -qE '^#define CONFIG_GPL 0$' config.h || ! grep -qE '^#define CONFIG_NONFREE 0$' config.h; then echo "not an LGPL build" >&2; exit 1; fi
sed -n 's/^#define FFMPEG_CONFIGURATION "\(.*\)"$/\1/p' config.h > "$output/ffmpeg-configuration.txt"
ls -l "$output"
