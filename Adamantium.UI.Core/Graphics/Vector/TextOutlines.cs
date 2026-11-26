using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Core.Graphics.Vector;

/// <summary>A stretch of text drawn by its glyphs' outlines: one path in one paint and one thickening.</summary>
/// <param name="Outline">The outlines, each where its glyph is drawn, filled by the nonzero rule.</param>
/// <param name="Color">The stretch's own color; null for the text's brush.</param>
/// <param name="Thickening">How far a synthesized bold thickens each side; 0 for none.</param>
internal readonly record struct TextOutline(Geometry Outline, Color? Color, double Thickening)
{
    /// <summary>What thickens a synthesized bold - a line as wide as both sides, in the fill, with round corners, as an
    /// outline grown evenly; null for none.</summary>
    public Pen Thickener(Brush fill) =>
        Thickening > 0 ? new Pen(fill, 2 * Thickening, penLineJoin: PenLineJoin.Round) : null;
}

/// <summary>Laid-out text as outlines a vector writer fills: each glyph where the layout draws it, as the screen draws it
/// - stretched, turned and slanted.</summary>
internal static class TextOutlines
{
    /// <summary>What a stretch of text is filled with: its own color, or the text's brush.</summary>
    public static Brush Paint(Color? own, Brush text) => own is { } color ? new SolidColorBrush(color) : text;

    /// <summary>Draws <paramref name="outlines"/> into <paramref name="session"/> by the writer's own
    /// <paramref name="figures"/>: each in its own color or the text's brush, thickened where it is bold.</summary>
    public static void Draw(IDrawingSession session, IReadOnlyList<TextOutline> outlines, Brush text,
        Func<Brush, bool> paints, Action<Brush, Pen, Geometry> figures)
    {
        foreach (var outline in outlines)
        {
            var fill = Paint(outline.Color, text);
            if (!paints(fill))
            {
                continue;
            }

            if (FadesThickened(outline.Thickening, fill, out var opaque, out var alpha))
            {
                session.PushGroup(new DrawingGroupOptions { Opacity = alpha });
                figures(opaque, outline.Thickener(opaque), outline.Outline);
                session.PopGroup();
            }
            else
            {
                figures(fill, outline.Thickener(fill), outline.Outline);
            }
        }
    }

    /// <summary>Whether a thickened stretch in <paramref name="fill"/> is see-through: its fill and its thickening, laid
    /// one over the other, would show darker where they meet; drawn in <paramref name="opaque"/> in a group faded by
    /// <paramref name="alpha"/>, they are one ink as on the screen. A color's alpha and a brush's opacity are taken out;
    /// the alpha of a gradient's own stops stays.</summary>
    public static bool FadesThickened(double thickening, Brush fill, out Brush opaque, out double alpha)
    {
        opaque = null;
        alpha = 1;
        if (thickening <= 0 || fill == null)
        {
            return false;
        }

        alpha = Math.Clamp(fill.Opacity, 0, 1) * (fill is SolidColorBrush solid ? solid.Color.A / 255.0 : 1);
        if (alpha >= 1)
        {
            return false;
        }

        if (fill is SolidColorBrush color)
        {
            opaque = new SolidColorBrush(new Color(color.Color.R, color.Color.G, color.Color.B, (byte)255));
        }
        else
        {
            opaque = fill.Copy();
            opaque.Opacity = 1;
        }

        return true;
    }

    /// <summary>The outlines of <paramref name="glyphs"/>, a path for each stretch in one font, one color and one
    /// thickening - fonts wind their outlines either way, and two of them in one path would cancel where they overlap;
    /// glyphs without outlines are left out.</summary>
    public static List<TextOutline> Of(IReadOnlyList<TextGlyph> glyphs)
    {
        var outlines = new List<TextOutline>();
        StreamGeometry geometry = null;
        StreamGeometryContext context = null;
        IFont font = null;
        Color? color = null;
        var thickening = 0.0;
        foreach (var glyph in glyphs)
        {
            var drawn = glyph.Font.GetGlyphByIndex(glyph.Index);
            if (!drawn.HasOutlines)
            {
                continue;
            }

            if (geometry == null || !ReferenceEquals(glyph.Font, font) || glyph.Color != color ||
                glyph.Thickening != thickening)
            {
                Close(geometry, color, thickening, outlines);
                geometry = new StreamGeometry { FillRule = FillRule.NonZero };
                context = geometry.Open();
                font = glyph.Font;
                color = glyph.Color;
                thickening = glyph.Thickening;
            }

            Trace(context, drawn, glyph.Place);
        }

        Close(geometry, color, thickening, outlines);
        return outlines;
    }

    /// <summary>The box of the outlines of the glyphs in the text's brush, for one gradient or tile across the whole text
    /// however many fonts and sizes it is set in; null for none.</summary>
    public static Rect? InkOf(IReadOnlyList<TextGlyph> glyphs)
    {
        Rect? whole = null;
        foreach (var glyph in glyphs)
        {
            if (glyph.Color == null && glyph.Ink is { } ink)
            {
                whole = whole is { } sofar ? Union(sofar, ink) : ink;
            }
        }

        return whole;
    }

    /// <summary>Traces the contours of <paramref name="glyph"/> into <paramref name="context"/>, carried by
    /// <paramref name="place"/> from its font's units.</summary>
    public static void Trace(StreamGeometryContext context, Glyph glyph, Matrix3x2<double> place)
    {
        foreach (var contour in glyph.GetContours())
        {
            var figure = context.BeginFigure(At(contour.Segments[0].Start), true, true);
            foreach (var segment in contour.Segments)
            {
                switch (segment.Kind)
                {
                    case GlyphSegmentKind.Line:
                        figure.LineTo(At(segment.End));
                        break;
                    case GlyphSegmentKind.Quadratic:
                        figure.QuadraticBezierTo(At(segment.Control1), At(segment.End));
                        break;
                    default:
                        figure.CubicBezierTo(At(segment.Control1), At(segment.Control2), At(segment.End));
                        break;
                }
            }

            figure.CloseFigure();
        }

        Vector2<double> At(Vector2<double> point) => Vector2.Transform(point, place);
    }

    private static void Close(StreamGeometry geometry, Color? color, double thickening, List<TextOutline> outlines)
    {
        if (geometry == null)
        {
            return;
        }

        geometry.RecalculateBounds();
        outlines.Add(new TextOutline(geometry, color, thickening));
    }

    private static Rect Union(Rect one, Rect other)
    {
        var left = Math.Min(one.X, other.X);
        var top = Math.Min(one.Y, other.Y);
        return new Rect(left, top, Math.Max(one.Right, other.Right) - left, Math.Max(one.Bottom, other.Bottom) - top);
    }
}
