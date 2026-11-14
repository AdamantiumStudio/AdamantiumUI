#!/bin/bash
# Builds our CEF for Linux x64 from a checkout CEF's automate-git.py fetched, and its minimal distribution: the steps
# automate-git takes - CEF's patches and GN files (gclient_hook), our patches, the build, make_distrib. Official build
# with PGO against the Debian bullseye sysroot CEF's own builds use (not the host's glibc), proprietary codecs behind
# the ProprietaryCodecs feature, FFmpeg as a library of its own (libffmpeg.so, with Chrome's codecs), no OpenH264.
# The FFmpeg without codecs comes after it from ffmpeg-without-codecs-linux.sh, the release archive from pack.sh.
# Usage: build-linux.sh [jobs] - CEF_ROOT is the checkout's root (default ~/cef); CEF_RESUME=1 continues a build that
# stopped, keeping the patches and the GN files it has.
set -e
unset DBUS_SESSION_BUS_ADDRESS
root="${CEF_ROOT:-$HOME/cef}"
jobs="${1:-16}"
here="$(cd "$(dirname "$0")" && pwd)"
export PATH="$root/depot_tools:$PATH"
export DEPOT_TOOLS_UPDATE=0
# No OpenH264: proprietary_codecs would link its software H.264 encoder into libcef itself.
export GN_DEFINES="is_official_build=true proprietary_codecs=true ffmpeg_branding=Chrome is_component_ffmpeg=true media_use_openh264=false use_sysroot=true"
export CEF_ARCHIVE_FORMAT=tar.bz2
export GN_OUT_CONFIGS=Release_GN_x64
src="$root/code/chromium/src"

apply_once() {
  if git -C "$1" apply --reverse --check "$2" 2>/dev/null; then
    echo "applied already: $(basename "$2")"
  else
    git -C "$1" apply "$2"
    echo "applied: $(basename "$2")"
  fi
}

echo "== start $(date '+%d.%m %H:%M')"
if [ "$CEF_RESUME" != 1 ]; then
  (cd "$src/cef" && python3 tools/gclient_hook.py)
fi
apply_once "$src" "$here/patches/chromium-proprietary-codecs-gate.patch"
apply_once "$src/cef" "$here/patches/cef-make-distrib-ffmpeg.patch"
echo "== args"
cat "$src/out/Release_GN_x64/args.gn"
echo "== build $(date '+%d.%m %H:%M') with $jobs jobs"
(cd "$src" && autoninja -j "$jobs" -C out/Release_GN_x64 cefsimple chrome_sandbox)
echo "== distrib $(date '+%d.%m %H:%M')"
(cd "$src/cef/tools" && python3 make_distrib.py --output-dir="$src/cef/binary_distrib" --ninja-build --x64-build \
  --minimal --allow-partial)
echo "== done $(date '+%d.%m %H:%M')"
ls -la "$src/cef/binary_distrib"
