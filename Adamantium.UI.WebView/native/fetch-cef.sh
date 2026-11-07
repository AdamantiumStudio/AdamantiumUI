#!/usr/bin/env bash
# Downloads the CEF build the bindings are generated for and stages its runtime under runtimes/<rid>/native/cef.
# The minimal distribution from cef-builds.spotifycdn.com, checked against its SHA-1; its licence and Chromium's
# credits are staged beside it.
# Usage:  native/fetch-cef.sh [linux-x64|osx-arm64|win-x64]
set -euo pipefail

version="156.0.26+g8ea0c73+chromium-156.0.8078.88"
rid="${1:-linux-x64}"
case "$rid" in
  win-x64) platform=windows64 ;;
  linux-x64) platform=linux64 ;;
  osx-arm64) platform=macosarm64 ;;
  *) echo "Unknown runtime identifier '$rid'." >&2; exit 1 ;;
esac

native="$(cd "$(dirname "$0")" && pwd)"
project="$(dirname "$native")"
name="cef_binary_${version}_${platform}_minimal"
url="https://cef-builds.spotifycdn.com/${name//+/%2B}.tar.bz2"
work="$native/download"
archive="$work/$name.tar.bz2"
mkdir -p "$work"

if [ ! -f "$archive" ]; then
  echo "Downloading $name"
  curl -fL -o "$archive.part" "$url"
  mv "$archive.part" "$archive"
fi

if [ ! -f "$archive.sha1" ]; then
  curl -fsL -o "$archive.sha1.part" "$url.sha1"
  mv "$archive.sha1.part" "$archive.sha1"
fi
expected="$(tr -d '[:space:]' < "$archive.sha1")"
actual="$(sha1sum "$archive" 2>/dev/null || shasum -a 1 "$archive")"
actual="${actual%% *}"
if [ "$actual" != "$expected" ]; then
  rm -f "$archive" "$archive.sha1"
  echo "SHA-1 of $name is $actual, the CDN says $expected - removed it, run again." >&2
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
echo "OK: CEF $version -> $stage"
