using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media;
using Adamantium.Win32.Printing.Xps;
using static Adamantium.UI.Platforms.Windows.Printing.XpsGeometryPath;

namespace Adamantium.UI.Platforms.Windows.Printing;

// Text: set as glyphs of fonts the page embeds where they can be, in one color and not thickened; by its outlines
// otherwise.
internal sealed partial class XpsDrawingSession
{
    private void Text(TextRenderingParameters parameters, TextLayout layout, Brush foreground, Matrix4x4F transform)
    {
        var glyphs = TextGlyph.Of(parameters, layout, said: false);

        // One gradient or tile across the whole text, however many fonts and sizes it is set in.
        var whole = foreground is GradientBrush or TileBrush ? TextOutlines.InkOf(glyphs) : null;
        for (var start = 0; start < glyphs.Count;)
        {
            var first = glyphs[start];
            var sets = Sets(first, foreground);
            var end = start + 1;
            while (end < glyphs.Count && Sets(glyphs[end], foreground) == sets &&
                   (sets ? SameRun(first, glyphs[end]) : glyphs[end].Color == first.Color &&
                                                         glyphs[end].Thickening == first.Thickening))
            {
                end++;
            }

            var stretch = glyphs.GetRange(start, end - start);
            if (sets)
            {
                Glyphs(stretch, (SolidColorBrush)TextOutlines.Paint(first.Color, foreground), transform);
            }
            else
            {
                TextOutlines.Draw(this, TextOutlines.Of(stretch), foreground, Paints,
                    (fill, thickener, outline) => Figures(fill, thickener, outline, transform, whole));
            }

            start = end;
        }
    }

    // Set as glyphs: a font the page may embed, one solid color seen, nothing thickened.
    private static bool Sets(TextGlyph glyph, Brush foreground) =>
        glyph.Thickening == 0 && TextOutlines.Paint(glyph.Color, foreground) is SolidColorBrush solid && Paints(solid) &&
        XpsFonts.CanSet(glyph.Font, glyph.Index);

    // One run of glyphs: one font, one color, the same size, stretch, turn and slant.
    private static bool SameRun(TextGlyph one, TextGlyph other)
    {
        const double close = 1e-9;
        var a = one.Place;
        var b = other.Place;
        var scale = Math.Max(Math.Abs(a.M11) + Math.Abs(a.M12), Math.Abs(a.M21) + Math.Abs(a.M22));
        return ReferenceEquals(one.Font, other.Font) && one.Color == other.Color &&
               Math.Abs(a.M11 - b.M11) <= close * scale && Math.Abs(a.M12 - b.M12) <= close * scale &&
               Math.Abs(a.M21 - b.M21) <= close * scale && Math.Abs(a.M22 - b.M22) <= close * scale;
    }

    /// <summary>Writes a run of glyphs as one XPS Glyphs element: its em at the size the first glyph is drawn, placed by
    /// a transform that turns, stretches and slants it as its glyphs are; each glyph where it is drawn, by its pen and its
    /// offset in hundredths of the em.</summary>
    private void Glyphs(List<TextGlyph> run, SolidColorBrush brush, Matrix4x4F transform)
    {
        var font = run[0].Font;
        var place = run[0].Place;
        var units = (double)font.UnitsPerEm;

        // From the glyph's own frame - an em of `size`, y down as XPS sets a glyph upright - to where it is drawn.
        var x = new Vector2<double>(place.M11 * units, place.M12 * units);
        var y = new Vector2<double>(-place.M21 * units, -place.M22 * units);
        var size = Math.Sqrt(x.X * x.X + x.Y * x.Y);
        var det = x.X * y.Y - x.Y * y.X;
        if (!(size > 0) || Math.Abs(det) < 1e-12 * size * size || !Invertible(transform))
        {
            return;
        }

        var turn = new Matrix3x2<double>(x.X / size, x.Y / size, y.X / size, y.Y / size, 0, 0);
        var back = Matrix3x2<double>.Invert(turn);
        // Each glyph by its font's own index; the page's fonts give the run its subset's once the page is drawn.
        var indices = new XpsGlyphIndex[run.Count];
        var origin = Vector2.Transform(new Vector2<double>(place.M31, place.M32), back);
        var pen = origin.X;
        for (var i = 0; i < run.Count; i++)
        {
            var at = Vector2.Transform(new Vector2<double>(run[i].Place.M31, run[i].Place.M32), back);
            var advance = i + 1 < run.Count
                ? Math.Max(0, Vector2.Transform(new Vector2<double>(run[i + 1].Place.M31, run[i + 1].Place.M32), back).X - at.X)
                : font.GetAdvanceWidth(run[i].Index) / units * size;
            indices[i] = new XpsGlyphIndex((int)run[i].Index, (float)(advance / size * 100),
                (float)((at.X - pen) / size * 100), (float)(-(at.Y - origin.Y) / size * 100));
            pen += advance;
        }

        Check(_factory.CreateGlyphs(_fonts.ResourceOf(font), out var glyphs));
        var kept = false;
        try
        {
            Check(glyphs.SetFontRenderingEmSize((float)size));
            Check(glyphs.SetOrigin(new XpsPoint((float)origin.X, (float)origin.Y)));
            Check(glyphs.GetGlyphsEditor(out var editor));
            try
            {
                Check(editor.SetGlyphIndices((uint)indices.Length, indices));
                Check(editor.ApplyEdits());
            }
            finally
            {
                Marshal.ReleaseComObject(editor);
            }

            var fill = Solid(brush.Color, brush.Opacity);
            try
            {
                Check(glyphs.SetFillBrushLocal(fill));
            }
            finally
            {
                Marshal.ReleaseComObject(fill);
            }

            Matrix4x4F placed = new Matrix4x4F((float)turn.M11, (float)turn.M12, 0, 0, (float)turn.M21, (float)turn.M22, 0, 0,
                0, 0, 1, 0, 0, 0, 0, 1) * transform;
            if (!IsIdentity(placed))
            {
                Check(_factory.CreateMatrixTransform(ToXps(placed), out var matrix));
                try
                {
                    Check(glyphs.SetTransformLocal(matrix));
                }
                finally
                {
                    Marshal.ReleaseComObject(matrix);
                }
            }

            Check(Current.Visuals.Append((IXpsOMVisual)glyphs));
            _fonts.Keep(font, glyphs, indices);
            kept = true;
        }
        finally
        {
            if (!kept)
            {
                Marshal.ReleaseComObject(glyphs);
            }
        }
    }
}
