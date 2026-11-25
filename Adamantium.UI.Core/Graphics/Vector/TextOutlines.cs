using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Core.Graphics.Vector;

/// <summary>Laid-out text as runs of glyphs whose outlines a vector writer fills: one run for each stretch in one font
/// and size, each glyph where the layout put it.</summary>
internal static class TextOutlines
{
    /// <summary>The runs of <paramref name="layout"/>, and - when <paramref name="whole"/> is asked for - the box of all
    /// their outlines, for one gradient or tile across the whole text however many fonts and sizes it is set in.
    /// </summary>
    public static List<GlyphRun> RunsOf(TextRenderingParameters parameters, TextLayout layout, out Rect? whole)
    {
        var runs = new List<GlyphRun>();
        whole = null;
        if (layout == null)
        {
            return runs;
        }

        var area = parameters?.TextArea ?? default;
        var glyphs = new List<PositionedGlyph>();
        IFont font = null;
        var size = 0.0;
        foreach (var word in layout.GetTextData())
        {
            var glyph = word.Glyph;
            if (glyph == null || word.Font == null || !glyph.HasOutlines || word.Rect.Width <= 0 || !(word.FontSize > 0))
            {
                continue;
            }

            if (!ReferenceEquals(word.Font, font) || word.FontSize != size)
            {
                Close(font, size, glyphs, runs);
                font = word.Font;
                size = word.FontSize;
            }

            var box = glyph.BoundingRectangle;
            var scale = word.FontSize / word.Font.UnitsPerEm;
            glyphs.Add(new PositionedGlyph(glyph.Index, word.Rect.X + area.X - box.X * scale,
                word.Rect.Y + area.Y + (box.Y + box.Height) * scale));
        }

        Close(font, size, glyphs, runs);
        foreach (var run in runs)
        {
            if (run.Outline?.CurvesForDrawing is { Figures.Count: > 0 } curves)
            {
                whole = whole is { } sofar ? Union(sofar, curves.Bounds) : curves.Bounds;
            }
        }

        return runs;
    }

    private static void Close(IFont font, double size, List<PositionedGlyph> glyphs, List<GlyphRun> runs)
    {
        if (font != null && glyphs.Count > 0)
        {
            runs.Add(new GlyphRun(font, size, [.. glyphs]));
        }

        glyphs.Clear();
    }

    private static Rect Union(Rect one, Rect other)
    {
        var left = Math.Min(one.X, other.X);
        var top = Math.Min(one.Y, other.Y);
        return new Rect(left, top, Math.Max(one.Right, other.Right) - left, Math.Max(one.Bottom, other.Bottom) - top);
    }
}
