# Third-party notices

Parts of this project are derived from other people's work, each under the licence its section below names — most of
it the MIT licence, which permits use, modification and distribution, including inside a closed product, on one
condition: the copyright notice and the licence text travel with the code. The per-file headers in the sources are that
notice and must not be removed; this file carries the licence texts they refer to.

Listed here is what was *taken in*, edited or, where its section says so, unchanged. Packages consumed as dependencies
are not listed: they carry their own licences with them. Code the engine took in is listed in the engine repository's
own notices.

**On modifications.** The code taken in has been changed, some of it heavily; what a section calls unchanged has not. That is what the licence allows, and it does not
transfer anything: the original notice stays with a derived file for as long as any of the original remains in it, and
the changes themselves belong to this project. Both statements are meant to be read together — the header says whose
work it started as, this file says it did not stay that way.

---

## Color test fonts

Not code, but files shipped beside it, unchanged, each with its licence text in the same folder:

- `Adamantium.UI.Sandbox.Content/Fonts/` and `Tests/Adamantium.UITests/Fonts/` — `NotoColorEmoji.subset.ttf`, the
  subset of Noto Color Emoji HarfBuzz's tests carry (https://github.com/harfbuzz/harfbuzz/tree/main/test/api/fonts),
  copyright Google LLC, under the SIL Open Font License 1.1 (`LICENSE-NotoColorEmoji.txt`).
- The same folders — `samples-sbix.ttf`, `samples-picosvg.ttf`, `samples-glyf_colr_1.ttf` and
  `test_glyphs-glyf_colr_1.ttf`, from https://github.com/googlefonts/color-fonts, copyright Google LLC, under the
  Apache License 2.0 (`LICENSE-color-fonts.txt`).
- `Tests/Adamantium.UITests/Fonts/RobotoFlex-Variable.ttf`, also linked into the sandbox's content `Fonts/`, the
  variable font from https://github.com/google/fonts/tree/main/ofl/robotoflex, copyright 2017 The Roboto Flex Project
  Authors, under the SIL Open Font License 1.1 (`LICENSE-RobotoFlex.txt`).

---

## Script sample fonts

Shown on the sandbox's text page, unchanged, each with its licence text in the same folder:

- `Adamantium.UI.Sandbox.Content/Fonts/NotoNastaliqUrdu-Regular.ttf`, from https://github.com/notofonts/nastaliq,
  copyright 2022 The Noto Project Authors, under the SIL Open Font License 1.1 (`LICENSE-NotoNastaliqUrdu.txt`).
- The same folder — `PonomarUnicode.otf` (copyright 2013-2020), `MonomakhUnicode.otf` (2006-2020) and
  `VertogradUnicode.otf` (2019-2020), Aleksandr Andreev and Nikita Simmons, from the fonts-churchslavonic release 2.2.1
  of the Slavonic Computing Initiative (https://github.com/typiconman/fonts-cu), under the SIL Open Font License 1.1
  (`LICENSE-ChurchSlavonic.txt`).

---

## Chromium Embedded Framework C API

**Where:** `Adamantium.UI.WebView/native/cef/` — the C API headers of CEF 156.0.26 (chromium 156.0.8078.88), from the
minimal distribution at https://cef-builds.spotifycdn.com, unchanged; and `Adamantium.UI.WebView/Generated/`, generated
from them.

Copyright (c) Marshall A. Greenblatt, portions copyright Google Inc., under the BSD 3-Clause licence
(`native/cef/LICENSE.txt`). CEF itself, which the
WebView loads at run time, is downloaded by `native/fetch-cef` and not part of this repository: on Windows and Linux
our build of it from this repository's releases, made by `native/cef-build` (its patches to Chromium and CEF are under
their BSD licenses; FFmpeg in it is under the LGPL, with its source named in `FFMPEG.txt` of the archive), on macOS
CEF's own build.
