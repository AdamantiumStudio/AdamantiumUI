# Our CEF build

CEF 156.0.26 (Chromium 156.0.8078.88) for Windows x64 and Linux x64, built as CEF's own builds are, with these
differences:

- **H.264, HEVC and AAC are in, behind a feature that is off.** `proprietary_codecs=true`, and
  `patches/chromium-proprietary-codecs-gate.patch` puts every use of the patented codecs - the type lists
  (`canPlayType`, MediaCapabilities, MSE, EME), FFmpeg's decoders, the GPU's and the system's decoders and encoders -
  behind Chromium's `ProprietaryCodecs` feature, off by default. `--enable-features=ProprietaryCodecs` turns it on.
- **FFmpeg is a library of its own** (`is_component_ffmpeg=true`), in two variants: the one built with Chrome's codecs
  (H.264 and AAC decoders) and one built with Chromium's (without them). They export the same functions, so either
  stands in as the file. The release archive carries the one without; the one with is kept apart (see below).
- **No OpenH264**: `media_use_openh264=false`, or `proprietary_codecs` would link its H.264 encoder into libcef itself.
- `make_distrib` ships FFmpeg (`patches/cef-make-distrib-ffmpeg.patch`).

The WebView turns the feature on when the FFmpeg beside CEF decodes H.264 and AAC.

## Building

A machine with at least 32 cores, 64 GB and 300 GB free per system takes about 3 hours a system from scratch.

1. Fetch the sources with CEF's `automate-git.py --no-build --branch=8078
   --checkout=8ea0c73b5dbed1389c8dafb911a98c41246bb8cb` (see CEF's BranchesAndBuilding) - the commit the bindings and
   `fetch-cef` are for - then `gclient sync --with_branch_heads` in `code/chromium` and check that every dependency is
   at the revision DEPS names.
   Set `checkout_pgo_profiles: True` in `.gclient`'s custom_vars: the official build needs the PGO profiles.
2. Windows: Visual Studio Build Tools with the Windows SDK and its Debugging Tools, ATL/MFC.
   Linux: `build/install-build-deps.sh` of the Chromium checkout.
3. `build-windows.ps1 -Root <checkout root> -Jobs <N>` or `CEF_ROOT=<checkout root> build-linux.sh <N>` - CEF's
   patches and GN files, ours, the build and the minimal distribution in `code/chromium/src/cef/binary_distrib`.
4. `ffmpeg-without-codecs-windows.ps1 -Root <checkout root>` or `ffmpeg-without-codecs-linux.sh` - the FFmpeg
   without codecs in `out/Release_GN_x64_FreeCodecs`, a minute or two.
5. `CEF_ROOT=<checkout root> pack.sh <windows64|linux64> cef-<CEF version>-<build number> <output folder>` (Git Bash
   for Windows, e.g. `CEF_ROOT=/d/cef`) - the release archive with the FFmpeg without codecs and its SHA-256. Upload it
   to a release of that name, and write its name and SHA-256 into `native/fetch-cef.ps1` and `native/fetch-cef.sh`.
   A new build of the same CEF takes the next build number.

The FFmpeg with codecs is `out/Release_GN_x64/ffmpeg.dll` or `libffmpeg.so`. It carries software H.264 and AAC
decoders, which need a patent license to be given to others, so it is not published: `fetch-cef -Codecs <file>`
stages a copy kept by hand.

## Checking

`codec-probe/` - a page that asks for each codec (`canPlayType`, MediaCapabilities), plays it and decodes its audio.
`make-samples.sh` makes the samples, `run-probe.sh <name> [switches]` runs it in a Linux build's `cefsimple` on a hidden
X display. With the feature off, H.264, HEVC and AAC must be refused everywhere.
