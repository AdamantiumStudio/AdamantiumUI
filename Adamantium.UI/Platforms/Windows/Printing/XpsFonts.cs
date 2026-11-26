using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Fonts;
using Adamantium.Fonts.Subsetting;
using Adamantium.Win32.Printing;
using Adamantium.Win32.Printing.Xps;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>The fonts a page of an XPS package sets its text in: for each font, the glyphs the page uses, written as an
/// OpenType subset once the page is drawn, obfuscated in a part named by a GUID (XPS 1.0, 9.1.7.3). The page's glyph
/// runs are written with their fonts' own glyph indices and given the subset's once it is known.</summary>
internal sealed class XpsFonts(IXpsOMObjectFactory factory) : IDisposable
{
    private readonly Dictionary<IFont, PageFont> _page = new(ReferenceEqualityComparer.Instance);

    // A font of the page: its resource in the package, the glyphs used, and the runs set in it.
    private sealed class PageFont(IXpsOMFontResource resource, object part)
    {
        public IXpsOMFontResource Resource { get; } = resource;

        public object Part { get; } = part;

        public HashSet<uint> Used { get; } = [];

        public List<Run> Runs { get; } = [];
    }

    // A run of glyphs written, and its indices - each its font's own glyph until the page is written.
    private sealed record Run(IXpsOMGlyphs Glyphs, XpsGlyphIndex[] Indices);

    /// <summary>Whether <paramref name="glyph"/> of <paramref name="font"/> can be set in an embedded subset: the font
    /// allows it to be embedded and cut down, and the glyph's outline fits a CFF program.</summary>
    public static bool CanSet(IFont font, uint glyph) =>
        font.Embedding is { IsAllowed: true, MaySubset: true } && CffSubset.CanHold(font, glyph);

    /// <summary>The resource of <paramref name="font"/> on this page, added the first time.</summary>
    public IXpsOMFontResource ResourceOf(IFont font)
    {
        if (!_page.TryGetValue(font, out var used))
        {
            var name = "/Resources/Fonts/" + Guid.NewGuid().ToString("D").ToUpperInvariant() + ".odttf";
            XpsGeometryPath.Check(factory.CreatePartUri(name, out var part));

            // Its program is written once the page knows which glyphs it uses.
            XpsGeometryPath.Check(factory.CreateFontResource(new ComStream(new MemoryStream()), XpsFontEmbedding.Obfuscated,
                part, 0, out var created));
            used = new PageFont(created, part);
            _page[font] = used;
        }

        return used.Resource;
    }

    /// <summary>Takes <paramref name="glyphs"/>, set in <paramref name="font"/> with <paramref name="indices"/> of the
    /// font's own glyphs, to give them the subset's indices when the page is written; released with the page.</summary>
    public void Keep(IFont font, IXpsOMGlyphs glyphs, XpsGlyphIndex[] indices)
    {
        ResourceOf(font);
        var used = _page[font];
        foreach (var index in indices)
        {
            used.Used.Add((uint)index.Index);
        }

        used.Runs.Add(new Run(glyphs, indices));
    }

    /// <summary>Writes each font of the page as the subset of the glyphs it used, and gives its runs the subset's
    /// indices - a glyph's index is its place in <see cref="CffSubset.IndicesOf"/>; called before the page is written.
    /// </summary>
    public void WritePage()
    {
        foreach (var (font, used) in _page)
        {
            var subset = CffSubset.IndicesOf(font, used.Used);
            var place = new Dictionary<uint, int>(subset.Count);
            for (var at = 0; at < subset.Count; at++)
            {
                place[subset[at]] = at;
            }

            foreach (var run in used.Runs)
            {
                var indices = (XpsGlyphIndex[])run.Indices.Clone();
                for (var at = 0; at < indices.Length; at++)
                {
                    indices[at].Index = place[(uint)indices[at].Index];
                }

                XpsGeometryPath.Check(run.Glyphs.GetGlyphsEditor(out var editor));
                try
                {
                    XpsGeometryPath.Check(editor.SetGlyphIndices((uint)indices.Length, indices));
                    XpsGeometryPath.Check(editor.ApplyEdits());
                }
                finally
                {
                    Marshal.ReleaseComObject(editor);
                }
            }

            var program = OpenTypeSubset.Write(font, subset, NameOf(font));
            XpsGeometryPath.Check(used.Resource.SetContent(new ComStream(new MemoryStream(program)),
                XpsFontEmbedding.Obfuscated, used.Part));
        }
    }

    /// <summary>Lets go of the page's fonts and runs; called once the page is written.</summary>
    public void EndPage()
    {
        foreach (var used in _page.Values)
        {
            foreach (var run in used.Runs)
            {
                Marshal.ReleaseComObject(run.Glyphs);
            }

            Marshal.ReleaseComObject(used.Resource);
            Marshal.ReleaseComObject(used.Part);
        }

        _page.Clear();
    }

    public void Dispose() => EndPage();

    // As a subset is named (ISO 32000-2, 9.9.2): six capitals, then the font's PostScript name, or its family's.
    private static string NameOf(IFont font)
    {
        var own = font.GetName(6);
        return "XPSSUB+" + (string.IsNullOrWhiteSpace(own) ? font.GetName(1) ?? "Font" : own);
    }
}
