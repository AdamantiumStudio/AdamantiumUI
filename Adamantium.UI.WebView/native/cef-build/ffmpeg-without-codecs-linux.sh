#!/bin/bash
# Builds the FFmpeg without H.264 and AAC (Chromium's branding) for a Linux build of build-linux.sh: only the ffmpeg
# library, in an out folder of its own with the main build's arguments but Chromium's FFmpeg - it exports what libcef
# takes from the one with codecs and stands in for it as a file.
# Usage: ffmpeg-without-codecs-linux.sh [jobs] - CEF_ROOT as for build-linux.sh.
set -e
unset DBUS_SESSION_BUS_ADDRESS
root="${CEF_ROOT:-$HOME/cef}"
jobs="${1:-16}"
export PATH="$root/depot_tools:$PATH"
export DEPOT_TOOLS_UPDATE=0
src="$root/code/chromium/src"
out="$src/out/Release_GN_x64_FreeCodecs"
mkdir -p "$out"
grep -vE '^(ffmpeg_branding|proprietary_codecs)' "$src/out/Release_GN_x64/args.gn" > "$out/args.gn"
printf 'ffmpeg_branding="Chromium"\nproprietary_codecs=false\n' >> "$out/args.gn"
cd "$src"
gn gen "$out"
autoninja -j "$jobs" -C "$out" third_party/ffmpeg
ls -la "$out/libffmpeg.so"
