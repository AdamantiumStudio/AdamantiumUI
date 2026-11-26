using Adamantium.Win32.Printing.Xps;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>A run of glyphs written on a page, and its indices - each its font's own glyph until the page's fonts are
/// written and give it the subset's.</summary>
internal sealed record XpsGlyphRun(IXpsOMGlyphs Glyphs, XpsGlyphIndex[] Indices);
