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

// Text: set in fonts the document carries where it is written with them, in one color; by its outlines otherwise.
public sealed partial class PdfContentSession
{
    /// <summary>The document being written, which carries the fonts; null where none is - an annotation's appearance.
    /// </summary>
    private readonly PdfDocumentWriter _document;

    private void Text(TextRenderingParameters parameters, TextLayout layout, Brush foreground, Matrix4x4F transform)
    {
        if (layout == null || !Paints(foreground))
        {
            return;
        }

        if (_document != null && foreground is SolidColorBrush solid)
        {
            Set(TextGlyph.Of(parameters, layout, said: true), solid, transform);
            return;
        }

        var runs = TextOutlines.RunsOf(parameters, layout, out var whole);

        // One gradient or tile across the whole text, however many fonts and sizes it is set in.
        if (foreground is not (GradientBrush or TileBrush))
        {
            whole = null;
        }

        foreach (var run in runs)
        {
            Figures(foreground, null, run.Outline, transform, whole);
        }
    }

    private static List<TextGlyph> GlyphsOf(GlyphRun run)
    {
        var glyphs = new List<TextGlyph>(run.Glyphs.Count);
        foreach (var glyph in run.Glyphs)
        {
            if (double.IsFinite(glyph.X) && double.IsFinite(glyph.Y))
            {
                glyphs.Add(new TextGlyph(run.Font, run.FontSize, glyph.Index, glyph.X, glyph.Y, null, true));
            }
        }

        return glyphs;
    }

    /// <summary>The glyphs a font of the document can draw are set in it, stretch by stretch; the others are filled by
    /// their outlines.</summary>
    private void Set(List<TextGlyph> glyphs, SolidColorBrush brush, Matrix4x4F transform)
    {
        var place = Through(transform);
        if (!Invertible(place))
        {
            return;
        }

        for (var start = 0; start < glyphs.Count;)
        {
            var font = FontOf(glyphs[start]);
            var end = start + 1;
            while (end < glyphs.Count && ReferenceEquals(FontOf(glyphs[end]), font))
            {
                end++;
            }

            var stretch = glyphs.GetRange(start, end - start);
            if (font != null)
            {
                Set(font, stretch, brush, place);
            }
            else
            {
                foreach (var run in TextOutlines.RunsOf(stretch))
                {
                    Figures(brush, null, run.Outline, transform);
                }
            }

            start = end;
        }
    }

    private PdfEmbeddedFont FontOf(TextGlyph glyph)
    {
        if (!glyph.IsUpright || !PdfEmbeddedFont.CanEmbed(glyph.Font))
        {
            return null;
        }

        var font = _document.Font(glyph.Font);
        return font.CanDraw(glyph.Index) ? font : null;
    }

    private void Set(PdfEmbeddedFont font, List<TextGlyph> glyphs, SolidColorBrush brush, PdfMatrix place)
    {
        var content = Content;
        content.SaveState();
        Opacity(1, Alpha(brush));
        FillColor(brush.Color);
        content.BeginText(font);
        foreach (var glyph in glyphs)
        {
            var matrix = new PdfMatrix(glyph.Size, 0, 0, -glyph.Size, glyph.X, glyph.Y).Then(place);
            content.Glyph(glyph.Index, matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F, glyph.Text);
            var box = glyph.Font.GetGlyphByIndex(glyph.Index).BoundingRectangle;
            if (box.Width > 0 && box.Height > 0)
            {
                var scale = glyph.Size / glyph.Font.UnitsPerEm;
                GrowBox(new Rect(glyph.X + box.X * scale, glyph.Y - (box.Y + box.Height) * scale, box.Width * scale,
                    box.Height * scale), place, 0);
            }
        }

        content.EndText();
        content.RestoreState();
    }
}
