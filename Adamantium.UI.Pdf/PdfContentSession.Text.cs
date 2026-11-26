using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Writing;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf;

// Text: set in fonts the document carries where it is written with them in one color; by its outlines otherwise.
public sealed partial class PdfContentSession
{
    /// <summary>The document being written, which carries the fonts; null where none is - an annotation's appearance.
    /// </summary>
    private readonly PdfDocumentWriter _document;

    private void Text(TextRenderingParameters parameters, TextLayout layout, Brush foreground, Matrix4x4F transform)
    {
        if (layout == null)
        {
            return;
        }

        var glyphs = TextGlyph.Of(parameters, layout, said: true);

        // One gradient or tile across the whole text, however many fonts and sizes it is set in.
        var whole = foreground is GradientBrush or TileBrush ? TextOutlines.InkOf(glyphs) : null;
        Set(glyphs, foreground, transform, whole);
    }

    private static List<TextGlyph> GlyphsOf(GlyphRun run)
    {
        var glyphs = new List<TextGlyph>(run.Glyphs.Count);
        foreach (var glyph in run.Glyphs)
        {
            if (double.IsFinite(glyph.X) && double.IsFinite(glyph.Y))
            {
                glyphs.Add(TextGlyph.At(run.Font, run.FontSize, glyph.Index, glyph.X, glyph.Y, null));
            }
        }

        return glyphs;
    }

    /// <summary>The glyphs a font of the document can draw in one color are set in it, stretch by stretch; the others
    /// are filled by their outlines.</summary>
    private void Set(List<TextGlyph> glyphs, Brush foreground, Matrix4x4F transform, Rect? whole = null)
    {
        var place = Through(transform);
        if (!Invertible(place))
        {
            return;
        }

        var fonts = new PdfEmbeddedFont[glyphs.Count];
        for (var at = 0; at < glyphs.Count; at++)
        {
            fonts[at] = FontOf(glyphs[at], foreground);
        }

        for (var start = 0; start < glyphs.Count;)
        {
            var first = glyphs[start];
            var font = fonts[start];
            var end = start + 1;
            while (end < glyphs.Count && ReferenceEquals(fonts[end], font) && glyphs[end].Color == first.Color &&
                   glyphs[end].Thickening == first.Thickening)
            {
                end++;
            }

            var stretch = glyphs.GetRange(start, end - start);
            var fill = TextOutlines.Paint(first.Color, foreground);
            if (font == null)
            {
                TextOutlines.Draw(this, TextOutlines.Of(stretch), foreground, Paints,
                    (paint, thickener, outline) => Figures(paint, thickener, outline, transform, whole));
            }
            else if (!Paints(fill))
            {
                // Nothing to see is not written, nor its text: a see-through bold as a see-through plain face.
            }
            else if (TextOutlines.FadesThickened(first.Thickening, fill, out var opaque, out var alpha) &&
                     stretch.Exists(glyph => glyph.Ink != null))
            {
                // A group of no ink is dropped, and would take blanks' text with it: those are set as they are.
                PushGroup(new DrawingGroupOptions { Opacity = alpha });
                Set(font, stretch, (SolidColorBrush)opaque, place);
                PopGroup();
            }
            else
            {
                Set(font, stretch, (SolidColorBrush)fill, place);
            }

            start = end;
        }
    }

    // Set in a font of the document: a glyph it can draw, in one color.
    private PdfEmbeddedFont FontOf(TextGlyph glyph, Brush foreground)
    {
        if (_document == null || glyph.Color == null && foreground is not SolidColorBrush ||
            !PdfEmbeddedFont.CanEmbed(glyph.Font))
        {
            return null;
        }

        var font = _document.Font(glyph.Font);
        return font.CanDraw(glyph.Index) ? font : null;
    }

    private void Set(PdfEmbeddedFont font, List<TextGlyph> glyphs, SolidColorBrush brush, PdfMatrix place)
    {
        if (!Paints(brush))
        {
            return;
        }

        var content = Content;
        var thickening = glyphs[0].Thickening;
        content.SaveState();

        // Set where the layout is: a thickening's width is in its units and turns and stretches with the glyphs, as an
        // outline's stroke does.
        content.Transform(place.A, place.B, place.C, place.D, place.E, place.F);
        FillColor(brush.Color);
        if (thickening > 0)
        {
            // A synthesized bold: the glyphs stroked as well, as wide as both sides grow, in their own color.
            Opacity(Alpha(brush), Alpha(brush));
            StrokeColor(brush.Color);
            content.LineWidth(2 * thickening).LineJoin(1).StrokesText(true);
        }
        else
        {
            Opacity(1, Alpha(brush));
        }

        content.BeginText(font);
        foreach (var glyph in glyphs)
        {
            // The font is set at one unit to the em: its units are scaled back to the em.
            var units = glyph.Font.UnitsPerEm;
            var at = glyph.Place;
            content.Glyph(glyph.Index, at.M11 * units, at.M12 * units, at.M21 * units, at.M22 * units, at.M31, at.M32,
                glyph.Text);
            if (glyph.Ink is { } ink)
            {
                // A thickened glyph reaches past its outline: the stroke's half on each side.
                GrowBox(ink, place, glyph.Thickening);
            }
        }

        content.EndText();
        content.RestoreState();
    }
}
