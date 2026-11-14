#!/bin/bash
# Runs the probe page in cefsimple of a Linux build on a hidden X display and prints what it found.
# Usage: run-probe.sh <run name> [switches...], e.g. run-probe.sh on --enable-features=ProprietaryCodecs.
# CEF_OUT is the build's out folder (default ~/cef/code/chromium/src/out/Release_GN_x64); LD_LIBRARY_PATH pointed at
# another FFmpeg's folder swaps it in, as libcef finds it through its RUNPATH.
name="$1"; shift
# Nothing may reach the desktop: no session bus (a keyring would ask for a password there), no display but the hidden one.
unset DBUS_SESSION_BUS_ADDRESS DISPLAY WAYLAND_DISPLAY
cd "$(dirname "$0")"
rm -f "results-$name.json"
python3 serve.py "$name" & server=$!
out="${CEF_OUT:-$HOME/cef/code/chromium/src/out/Release_GN_x64}"
timeout 60 xvfb-run -a "$out/cefsimple" --no-sandbox --password-store=basic --url=http://127.0.0.1:8765/probe.html \
  --user-data-dir="/tmp/probe-profile-$name" "$@" > "cef-$name.log" 2>&1 &
for i in $(seq 1 55); do
  if [ -f "results-$name.json" ]; then break; fi
  sleep 1
done
pkill -f "probe-profile-$name"; kill $server 2>/dev/null; wait 2>/dev/null
if [ -f "results-$name.json" ]; then cat "results-$name.json"; else echo "no results; log tail:"; tail -15 "cef-$name.log"; fi
