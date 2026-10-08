# Third-party notices

Parts of this project are derived from other people's work. All of it is under the MIT licence, which permits use,
modification and distribution — including inside a closed product — on one condition: the copyright notice and the
licence text travel with the code. The per-file headers in the sources are that notice and must not be removed; this
file carries the licence texts they refer to.

Listed here is code that was *taken in and edited*, not packages consumed as dependencies — those carry their own
licences with them. Code the engine took in is listed in the engine repository's own notices.

**On modifications.** All of this has been changed, some of it heavily. That is what the licence allows, and it does not
transfer anything: the original notice stays with a derived file for as long as any of the original remains in it, and
the changes themselves belong to this project. Both statements are meant to be read together — the header says whose
work it started as, this file says it did not stay that way.

---

## webgl-noise — simplex noise

**Where:** `Adamantium.UI.FX/Includes/NoiseMath.fxh` (the 2D simplex noise function, ported to Slang)

```
Copyright (C) 2011 by Ashima Arts (Simplex noise)
Copyright (C) 2011-2016 by Stefan Gustavson (Classic noise and others)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Color test fonts

Not code, but files shipped beside it, unchanged, each with its licence text in the same folder:

- `Adamantium.UI.Sandbox/Fonts/` and `Tests/Adamantium.UITests/Fonts/` — `NotoColorEmoji.subset.ttf`, the subset of
  Noto Color Emoji HarfBuzz's tests carry (https://github.com/harfbuzz/harfbuzz/tree/main/test/api/fonts), copyright
  Google LLC, under the SIL Open Font License 1.1 (`LICENSE-NotoColorEmoji.txt`).
- The same folders — `samples-sbix.ttf`, `samples-picosvg.ttf`, `samples-glyf_colr_1.ttf` and
  `test_glyphs-glyf_colr_1.ttf`, from https://github.com/googlefonts/color-fonts, copyright Google LLC, under the
  Apache License 2.0 (`LICENSE-color-fonts.txt`).
