#!/usr/bin/env bash
# Downloads the CEF build the bindings are generated for and stages its runtime under runtimes/<rid>/native/cef.
# Windows and Linux: our build (native/cef-build) from AdamantiumUI's releases, checked against the SHA-256 written
# here - H.264 and AAC behind the ProprietaryCodecs feature, FFmpeg without them. A second argument puts an FFmpeg with
# them (libffmpeg.so or ffmpeg.dll of our build) in its place; WebView then turns the feature on. macOS: CEF's own
# build from cef-builds.spotifycdn.com, checked against its SHA-1. Its licence and Chromium's credits are staged beside.
# Usage:  native/fetch-cef.sh [linux-x64|osx-arm64|win-x64] [FFmpeg with codecs]
set -euo pipefail

version="156.0.26+g8ea0c73+chromium-156.0.8078.88"
release="cef-156.0.26-1"
rid="${1:-linux-x64}"
codecs="${2:-}"
case "$rid" in
  win-x64) platform=windows64; sha256=c044612f98253f9fc072c281e4495910b5ecc8b7ecffea1f5748c60dcc93c15e; ffmpeg=ffmpeg.dll ;;
  linux-x64) platform=linux64; sha256=2e366cf6b5c75c83cab40398c0c69fd83494222452364926067b0eaf4a453b42; ffmpeg=libffmpeg.so ;;
  osx-arm64) platform=macosarm64; sha256= ;;
  *) echo "Unknown runtime identifier '$rid'." >&2; exit 1 ;;
esac
if [ -n "$codecs" ] && [ -z "$sha256" ]; then
  echo "An FFmpeg with codecs is for our builds - Windows and Linux." >&2
  exit 1
fi
if [ -n "$codecs" ] && [ ! -f "$codecs" ]; then
  echo "No FFmpeg at $codecs." >&2
  exit 1
fi

native="$(cd "$(dirname "$0")" && pwd)"
project="$(dirname "$native")"
if [ -n "$sha256" ]; then
  name="$release-$platform"
  url="https://github.com/AdamantiumStudio/AdamantiumUI/releases/download/$release/$name.tar.bz2"
else
  name="cef_binary_${version}_${platform}_minimal"
  url="https://cef-builds.spotifycdn.com/${name//+/%2B}.tar.bz2"
fi
work="$native/download"
archive="$work/$name.tar.bz2"
mkdir -p "$work"

if [ ! -f "$archive" ]; then
  echo "Downloading $name"
  curl -fL -o "$archive.part" "$url"
  mv "$archive.part" "$archive"
fi

if [ -n "$sha256" ]; then
  expected="$sha256"
  actual="$(sha256sum "$archive" 2>/dev/null || shasum -a 256 "$archive")"
  source="this script says"
else
  if [ ! -f "$archive.sha1" ]; then
    curl -fsL -o "$archive.sha1.part" "$url.sha1"
    mv "$archive.sha1.part" "$archive.sha1"
  fi
  expected="$(tr -d '[:space:]' < "$archive.sha1")"
  actual="$(sha1sum "$archive" 2>/dev/null || shasum -a 1 "$archive")"
  source="the CDN says"
fi
actual="${actual%% *}"
if [ "$actual" != "$expected" ]; then
  rm -f "$archive" "$archive.sha1"
  echo "The checksum of $name is $actual, $source $expected - removed it, run again." >&2
  exit 1
fi

# Unpacked aside and renamed when complete, so an interrupted run is never taken for a finished one.
if [ ! -d "$work/$name" ]; then
  rm -rf "$work/unpacking"
  mkdir -p "$work/unpacking"
  tar -xjf "$archive" -C "$work/unpacking"
  mv "$work/unpacking/$name" "$work/$name"
  rm -rf "$work/unpacking"
fi

stage="$project/runtimes/$rid/native/cef"
rm -rf "$stage"
mkdir -p "$stage"
cp -R "$work/$name/Release/." "$stage/"
[ -d "$work/$name/Resources" ] && cp -R "$work/$name/Resources/." "$stage/"
cp "$work/$name/LICENSE.txt" "$stage/"
[ -f "$work/$name/CREDITS.html" ] && cp "$work/$name/CREDITS.html" "$stage/"
rm -f "$stage"/*.lib "$stage"/bootstrap*.exe
if [ -n "$codecs" ]; then
  cp "$codecs" "$stage/$ffmpeg"
  echo "FFmpeg with H.264 and AAC from $codecs"
elif [ -f "$work/$name/FFMPEG.txt" ]; then
  cp "$work/$name/FFMPEG.txt" "$stage/"
fi
echo "OK: CEF $version -> $stage"
