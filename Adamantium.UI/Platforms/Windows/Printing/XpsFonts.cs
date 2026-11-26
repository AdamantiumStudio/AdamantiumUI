using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Fonts;
using Adamantium.Fonts.Subsetting;
using Adamantium.Win32.Printing;
using Adamantium.Win32.Printing.Xps;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>The fonts the pages of an XPS package set their text in: for each font of a page, the glyphs the page uses,
/// written as an OpenType subset once the page is drawn - a restricted font, as its license allows it to be printed,
/// obfuscated in a part named by a GUID (XPS 1.0, 9.1.7.3). The page's glyph runs are written with their fonts' own glyph
/// indices and given the subset's once it is known.</summary>
internal sealed class XpsFonts(IXpsOMObjectFactory factory) : IDisposable
{
    private const XpsFontEmbedding Embedding = XpsFontEmbedding.Restricted;

    private readonly Dictionary<IFont, XpsPageFont> _page = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IFont, Dictionary<uint, bool>> _settable = new(ReferenceEqualityComparer.Instance);
    private int _subsets;

    /// <summary>Whether <paramref name="glyph"/> of <paramref name="font"/> can be set in an embedded subset: the font
    /// allows it to be embedded and cut down, and the glyph's outline fits a CFF program - worked out once a glyph.
    /// </summary>
    public bool CanSet(IFont font, uint glyph)
    {
        if (font.Embedding is not { IsAllowed: true, MaySubset: true })
        {
            return false;
        }

        if (!_settable.TryGetValue(font, out var known))
        {
            known = [];
            _settable[font] = known;
        }

        if (!known.TryGetValue(glyph, out var settable))
        {
            settable = CffSubset.CanHold(font, glyph);
            known[glyph] = settable;
        }

        return settable;
    }

    /// <summary>The resource of <paramref name="font"/> on this page, added the first time.</summary>
    public IXpsOMFontResource ResourceOf(IFont font)
    {
        if (_page.TryGetValue(font, out var used))
        {
            return used.Resource;
        }

        var name = "/Resources/Fonts/" + Guid.NewGuid().ToString("D").ToUpperInvariant() + ".odttf";
        XpsGeometryPath.Check(factory.CreatePartUri(name, out var part));
        try
        {
            // Its program is written once the page knows which glyphs it uses.
            XpsGeometryPath.Check(factory.CreateFontResource(new ComStream(new MemoryStream()), Embedding, part, 0,
                out var created));
            used = new XpsPageFont(created, part);
        }
        catch
        {
            Marshal.ReleaseComObject(part);
            throw;
        }

        _page[font] = used;
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

        used.Runs.Add(new XpsGlyphRun(glyphs, indices));
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

            var program = OpenTypeSubset.Write(font, subset, NameOf(font, _subsets++));
            XpsGeometryPath.Check(used.Resource.SetContent(new ComStream(new MemoryStream(program)), Embedding, used.Part));
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

    // As a subset is named (ISO 32000-2, 9.9.2): six capitals of its own - two subsets of one font are two fonts - then
    // the font's PostScript name, or its family's.
    private static string NameOf(IFont font, int subset)
    {
        Span<char> tag = stackalloc char[6];
        for (var at = 5; at >= 0; at--)
        {
            tag[at] = (char)('A' + subset % 26);
            subset /= 26;
        }

        var own = font.GetName(6);
        return new string(tag) + "+" + (string.IsNullOrWhiteSpace(own) ? font.GetName(1) ?? "Font" : own);
    }
}
