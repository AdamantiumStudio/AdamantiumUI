using System;
using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>Glyphs of one font at one size that the caller has placed itself - a PDF page's text: each by its index in
/// the font and its pen position on the baseline, y down. Nothing is shaped or snapped; one matrix places the whole run
/// (<see cref="Graphics.IDrawingSession.DrawGlyphs"/>). Immutable, so a page drawn again draws the same run.</summary>
public sealed class GlyphRun
{
    private Geometry _outline;

    public GlyphRun(IFont font, double fontSize, IReadOnlyList<PositionedGlyph> glyphs)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(glyphs);
        if (!double.IsFinite(fontSize) || fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "A size above zero.");
        }

        Font = font;
        FontSize = fontSize;
        Glyphs = [.. glyphs];
        Layout = new TextLayout(font.Typeface, font);
        Layout.ProcessGlyphs(Glyphs, fontSize);
        Bounds = InkOf(Layout);
    }

    /// <summary>Where the glyphs' ink lies in the run's space; empty for a run that draws none.</summary>
    public Rect Bounds { get; }

    public IFont Font { get; }

    public double FontSize { get; }

    public IReadOnlyList<PositionedGlyph> Glyphs { get; }

    /// <summary>The layout the glyphs are drawn through; not to be changed.</summary>
    public TextLayout Layout { get; }

    /// <summary>The glyphs' outlines as one path in the run's space, filled by the nonzero rule: what strokes the letters
    /// and what clips by them where the distance field cannot.</summary>
    public Geometry Outline => _outline ??= BuildOutline();

    private static Rect InkOf(TextLayout layout)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var glyph in layout.GetTextData())
        {
            if (glyph.Rect.Width <= 0 && glyph.Rect.Height <= 0) continue;

            left = Math.Min(left, glyph.Rect.Left);
            top = Math.Min(top, glyph.Rect.Top);
            right = Math.Max(right, glyph.Rect.Right);
            bottom = Math.Max(bottom, glyph.Rect.Bottom);
        }

        return left > right ? Rect.Empty : new Rect(left, top, right - left, bottom - top);
    }

    private Geometry BuildOutline()
    {
        var geometry = new StreamGeometry { FillRule = FillRule.NonZero };
        var context = geometry.Open();
        var scale = FontSize / Font.UnitsPerEm;
        foreach (var placed in Glyphs)
        {
            var contours = Font.GetGlyphByIndex(placed.Index).GetContours();
            foreach (var contour in contours)
            {
                var figure = context.BeginFigure(At(contour[0][0]), true, true);
                foreach (var segment in contour)
                {
                    switch (segment.Length)
                    {
                        case 2:
                            figure.LineTo(At(segment[1]));
                            break;
                        case 3:
                            figure.QuadraticBezierTo(At(segment[1]), At(segment[2]));
                            break;
                        default:
                            figure.CubicBezierTo(At(segment[1]), At(segment[2]), At(segment[3]));
                            break;
                    }
                }

                figure.CloseFigure();
            }

            Vector2<double> At(Vector2<double> point) =>
                new(placed.X + point.X * scale, placed.Y - point.Y * scale);
        }

        geometry.RecalculateBounds();
        return geometry;
    }
}
