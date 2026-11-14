#!/bin/bash
# Packs the minimal distribution of build-windows.ps1 or build-linux.sh into the archive AdamantiumUI's releases carry
# and fetch-cef stages: the FFmpeg without H.264 and AAC in place of the one with them, the Linux libraries stripped,
# and where FFmpeg's source is (its license is the LGPL). Runs in WSL for Linux, in Git Bash for Windows.
# Usage: pack.sh <windows64|linux64> <release> <output folder>, e.g. pack.sh linux64 cef-156.0.26-1 ~/cef-release.
# CEF_ROOT is the checkout's root (default ~/cef; in Git Bash the -Root of build-windows.ps1, e.g. /d/cef).
set -e
platform="$1"
release="$2"
output="$3"
case "$platform" in
  windows64) ffmpeg=ffmpeg.dll ;;
  linux64) ffmpeg=libffmpeg.so ;;
  *) echo "Unknown platform '$platform'." >&2; exit 1 ;;
esac
root="${CEF_ROOT:-$HOME/cef}"
src="$root/code/chromium/src"
distrib=$(ls -d "$src"/cef/binary_distrib/cef_binary_*_"$platform"_minimal)
name="$release-$platform"
stage="$output/$name"
rm -rf "$stage"
mkdir -p "$stage"
cp -r "$distrib"/. "$stage"/
cp "$src/out/Release_GN_x64_FreeCodecs/$ffmpeg" "$stage/Release/$ffmpeg"
if [ "$platform" = linux64 ]; then
  strip "$stage"/Release/*.so*
fi

revision=$(git -C "$src/third_party/ffmpeg" rev-parse HEAD)
chromium=$(. "$src/chrome/VERSION" && echo "$MAJOR.$MINOR.$BUILD.$PATCH")
cat > "$stage/FFMPEG.txt" <<EOF
$ffmpeg is FFmpeg, licensed under the GNU LGPL 2.1 or later (see CREDITS.html), built from
https://chromium.googlesource.com/chromium/third_party/ffmpeg/+/$revision
as part of Chromium $chromium: the one in this archive by Chromium's configuration (ffmpeg_branding="Chromium"),
without the H.264 and AAC decoders; the one with them, which may stand in its place, by Chrome's
(ffmpeg_branding="Chrome"). The build scripts and patches are in Adamantium.UI.WebView/native/cef-build of
https://github.com/AdamantiumStudio/AdamantiumUI.
EOF

tar -C "$output" -cjf "$output/$name.tar.bz2" "$name"
rm -rf "$stage"
(cd "$output" && sha256sum "$name.tar.bz2" | tee "$name.tar.bz2.sha256")
