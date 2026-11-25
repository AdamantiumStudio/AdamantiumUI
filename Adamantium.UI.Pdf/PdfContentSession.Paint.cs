using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Writing;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Pdf;

// Paint: a fill or a stroke with a solid color or a gradient. A gradient is a shading laid over the shape's bounds as
// the renderer lays it - its points a fraction of them - and its stops' alpha a soft mask of the same shape.
public sealed partial class PdfContentSession
{
    /// <param name="gradientBox">The box a gradient is laid over, in the geometry's units; null for the geometry's own
    /// bounds.</param>
    private void Figures(Brush brush, Pen pen, Geometry geometry, Matrix4x4F transform, Rect? gradientBox = null)
    {
        if (geometry?.CurvesForDrawing is not { Figures.Count: > 0 } curves)
        {
            return;
        }

        var fills = Paints(brush);
        // A gradient laid along the line has no PDF paint to go by; it waits for the raster of what has none.
        var strokes = pen != null && pen.Thickness > 0 && Paints(pen.Brush) &&
                      (pen.Brush is SolidColorBrush || pen.BrushMapping != PenBrushMapping.Along);
        var place = Through(transform);
        if ((!fills && !strokes) || !Invertible(place))
        {
            return;
        }

        var width = strokes ? pen.Thickness * PenUnit(pen, transform) : 0;
        var reach = strokes ? width / 2 * StrokeReach(pen, curves) : 0;
        // The scale a tile brush is laid out at, as the renderer gives it: the drawing's to its root.
        var toRoot = Matrix(transform).Then(_inners.Count > 0 ? _inners.Peek() : PdfMatrix.Identity);
        var tileScale = new Vector2<double>(toRoot.A, toRoot.D);
        var wrote = false;
        if (fills && strokes && brush is SolidColorBrush fill && pen.Brush is SolidColorBrush line &&
            StrokesAsIs(pen))
        {
            wrote = Solid(curves, place, fill, pen, width, line);
        }
        else
        {
            if (fills)
            {
                wrote |= Fill(brush, curves, place, gradientBox ?? curves.Bounds, tileScale);
            }

            if (strokes)
            {
                wrote |= Stroke(pen, width, curves, place, reach, gradientBox ?? curves.Bounds, tileScale);
            }
        }

        if (wrote)
        {
            GrowBox(curves.Bounds, place, reach);
        }
    }

    /// <summary>How far past the line, in half widths, the stroke reaches: a miter corner within its limit, a square
    /// end turned on the diagonal.</summary>
    private static double StrokeReach(Pen pen, PathCurves curves)
    {
        var reach = pen.PenLineJoin == PenLineJoin.Miter
            ? MiterReach(curves, Core.Media.Pen.ClampMiterLimit(pen.MiterLimit))
            : 1;
        // Square ends - of the line or of its dashes - reach furthest on the diagonal.
        return pen.StartLineCap == PenLineCap.Square || pen.EndLineCap == PenLineCap.Square ||
               (pen.DashStrokeArray?.Count ?? 0) > 0 &&
               (pen.DashStartCap == PenLineCap.Square || pen.DashEndCap == PenLineCap.Square)
            ? Math.Max(reach, Math.Sqrt(2))
            : reach;
    }

    private static bool Paints(Brush brush) => brush switch
    {
        SolidColorBrush solid => solid.Color.A > 0 && solid.Opacity > 0,
        LinearGradientBrush or RadialGradientBrush => brush.Opacity > 0 && ((GradientBrush)brush).GradientStops.Count > 0,
        TileBrush tile => PaintsTiles(tile),
        _ => false,
    };

    /// <summary>A shape filled and stroked with solid colors, in one path.</summary>
    private bool Solid(PathCurves curves, PdfMatrix place, SolidColorBrush fill, Pen pen, double width,
        SolidColorBrush line)
    {
        var content = Content;
        content.SaveState();
        Opacity(Alpha(line), Alpha(fill));
        FillColor(fill.Color);
        Transform(place);
        Pen(pen, width, line.Color);
        var wrote = Write(curves, false, PdfMatrix.Identity);
        if (wrote)
        {
            content.FillAndStroke(curves.FillRule == FillRule.EvenOdd);
        }
        else
        {
            content.EndPath();
        }

        content.RestoreState();
        return wrote;
    }

    private bool Fill(Brush brush, PathCurves curves, PdfMatrix place, Rect gradientBox, Vector2<double> tileScale)
    {
        var content = Content;
        var evenOdd = curves.FillRule == FillRule.EvenOdd;
        content.SaveState();
        if (brush is SolidColorBrush solid)
        {
            Opacity(1, Alpha(solid));
            FillColor(solid.Color);
            Transform(place);
            var filled = Write(curves, true, PdfMatrix.Identity);
            if (filled)
            {
                content.Fill(evenOdd);
            }

            content.RestoreState();
            return filled;
        }

        if (brush is TileBrush tile)
        {
            var tiles = Tiles(tile, gradientBox, tileScale);
            if (tiles == null)
            {
                content.RestoreState();
                return false;
            }

            Opacity(1, Math.Clamp(brush.Opacity, 0, 1));
            if (tiles.Repeats)
            {
                content.FillPattern(tiles.Pattern(place));
            }

            Transform(place);
            var painted = Write(curves, true, PdfMatrix.Identity);
            if (painted && tiles.Repeats)
            {
                content.Fill(evenOdd);
            }
            else if (painted)
            {
                content.Clip(evenOdd);
                Transform(tiles.ToShape);
                content.Draw(tiles.Once());
            }

            content.RestoreState();
            return painted;
        }

        var gradient = Gradient((GradientBrush)brush, gradientBox);
        if (gradient == null)
        {
            content.RestoreState();
            return false;
        }

        AlphaMask(gradient, place, Box(curves.Bounds, place, 0));
        Opacity(1, Math.Clamp(brush.Opacity, 0, 1));
        Transform(place);
        var wrote = Write(curves, true, PdfMatrix.Identity);
        if (wrote)
        {
            content.Clip(evenOdd);
            Transform(gradient.ToShape);
            content.Shade(gradient.Color);
        }

        content.RestoreState();
        return wrote;
    }

    private bool Stroke(Pen pen, double width, PathCurves curves, PdfMatrix place, double reach, Rect gradientBox,
        Vector2<double> tileScale)
    {
        var content = Content;
        content.SaveState();
        var solid = pen.Brush as SolidColorBrush;
        var alpha = solid != null ? Alpha(solid) : Math.Clamp(pen.Brush.Opacity, 0, 1);
        var asIs = StrokesAsIs(pen);
        PdfObject pattern = null;
        if (pen.Brush is TileBrush tile)
        {
            // As the renderer lays it: over the shape grown by the half width the stroke reaches past it.
            var half = width / 2;
            var tiles = Tiles(tile, new Rect(gradientBox.X - half, gradientBox.Y - half, gradientBox.Width + width,
                gradientBox.Height + width), tileScale);
            if (tiles == null)
            {
                content.RestoreState();
                return false;
            }

            pattern = tiles.Pattern(place);
        }
        else if (solid == null)
        {
            var gradient = Gradient((GradientBrush)pen.Brush, gradientBox);
            if (gradient == null)
            {
                content.RestoreState();
                return false;
            }

            AlphaMask(gradient, place, Box(curves.Bounds, place, reach));
            pattern = PdfShadings.Pattern(gradient.Color, gradient.ToShape.Then(place));
        }

        bool wrote;
        if (asIs)
        {
            Opacity(alpha, 1);
            wrote = StrokeWhole(pen, width, curves, place, solid, pattern);
        }
        else if (alpha >= 1)
        {
            wrote = StrokeInRuns(pen, width, curves, place, reach, solid, pattern);
        }
        else
        {
            // Runs and the ends drawn as shapes overlap where they meet: drawn opaque into a group of their own, faded
            // once - one coverage, as on screen.
            var level = new Level(new PdfContentBuilder(), null, true);
            _levels.Push(level);
            try
            {
                wrote = StrokeInRuns(pen, width, curves, place, reach, solid, pattern);
            }
            finally
            {
                while (_levels.Pop() != level)
                {
                }
            }

            if (wrote)
            {
                content.Opacity(alpha, alpha);
                content.Draw(level.Builder.ToFormXObject(Box(curves.Bounds, place, reach), PdfTransparencyGroup.Layer));
            }
        }

        content.RestoreState();
        return wrote;
    }

    /// <summary>The pen as PDF strokes it, in one go.</summary>
    private bool StrokeWhole(Pen pen, double width, PathCurves curves, PdfMatrix place, SolidColorBrush solid,
        PdfObject pattern)
    {
        var content = Content;
        if (pattern != null)
        {
            content.StrokePattern(pattern);
        }

        Transform(place);
        Pen(pen, width, solid?.Color);
        var wrote = Write(curves, false, PdfMatrix.Identity);
        if (wrote)
        {
            content.Stroke();
        }
        else
        {
            content.EndPath();
        }

        return wrote;
    }

    /// <summary>The pen stroked in runs, in the content's current state, its ends drawn as shapes in its paint.</summary>
    private bool StrokeInRuns(Pen pen, double width, PathCurves curves, PdfMatrix place, double reach,
        SolidColorBrush solid, PdfObject pattern)
    {
        var content = Content;
        content.SaveState();
        if (pattern != null)
        {
            content.StrokePattern(pattern);
        }

        Transform(place);
        Pen(pen, width, solid?.Color);
        var wrote = StrokeRuns(pen, curves, width, reach, () =>
        {
            if (solid != null)
            {
                FillColor(solid.Color);
            }
            else
            {
                content.FillPattern(pattern);
            }

            content.Fill();
        });
        content.RestoreState();
        return wrote;
    }

    /// <summary>From here on, what is painted is masked by the gradient's alpha - when it has any below opaque.</summary>
    private void AlphaMask(Shading gradient, PdfMatrix place, PdfRectangle box)
    {
        if (gradient.Alpha == null || !(box.Width > 0) || !(box.Height > 0))
        {
            return;
        }

        var mask = new PdfContentBuilder();
        var toPage = gradient.ToShape.Then(place);
        mask.Transform(toPage.A, toPage.B, toPage.C, toPage.D, toPage.E, toPage.F).Shade(gradient.Alpha);
        Content.SoftMask(mask.ToFormXObject(box, PdfTransparencyGroup.GrayMask), true);
    }

    private static double Alpha(SolidColorBrush brush) => brush.Color.A / 255.0 * Math.Clamp(brush.Opacity, 0, 1);

    /// <summary>The pen's width, join and miter, and - for a pen PDF strokes as it is - its cap and dashes; for one
    /// stroked in runs those are the runs' own.</summary>
    private void Pen(Pen pen, double width, Color? color)
    {
        var content = Content;
        if (color is { } solid)
        {
            var tinted = Tinted(solid);
            content.StrokeColor(tinted.R / 255.0, tinted.G / 255.0, tinted.B / 255.0);
        }

        content.LineWidth(width);
        content.LineJoin(pen.PenLineJoin switch
        {
            PenLineJoin.Round => 1,
            PenLineJoin.Bevel => 2,
            _ => 0,
        });
        content.LineCap(pen.StartLineCap switch
        {
            PenLineCap.ConvexRound => 1,
            PenLineCap.Square => 2,
            _ => 0,
        });
        content.MiterLimit(Core.Media.Pen.ClampMiterLimit(pen.MiterLimit));
    }

    /// <summary>The gradient's shadings over <paramref name="bounds"/>: its colors, its alpha (null when opaque), and the
    /// matrix from the shadings' space to the shape's units. Null when it draws nothing.</summary>
    private Shading Gradient(GradientBrush brush, Rect bounds)
    {
        if (!(bounds.Width > 0) || !(bounds.Height > 0))
        {
            return null;
        }

        var stops = GradientRamp.Of(brush, _tint);
        if (stops.Count == 0)
        {
            return null;
        }

        var toShape = new PdfMatrix(bounds.Width, 0, 0, bounds.Height, bounds.X, bounds.Y);
        switch (brush)
        {
            case LinearGradientBrush linear:
            {
                var start = linear.StartPoint;
                var end = linear.EndPoint;
                var axis = end - start;
                var squared = axis.X * axis.X + axis.Y * axis.Y;
                if (!(squared > 1e-18))
                {
                    return null;
                }

                var (first, last) = brush.SpreadMethod != GradientSpreadMethod.Pad
                    ? GradientRamp.LinearRepeats(start, end)
                    : (0, 1);

                var periods = GradientRamp.Repeated(stops, brush.SpreadMethod, first, last);
                var from = start + axis * first;
                var to = start + axis * last;
                return new Shading(
                    PdfShadings.Axial(from.X, from.Y, to.X, to.Y, Colors(periods)),
                    GradientRamp.IsOpaque(periods) ? null : PdfShadings.Axial(from.X, from.Y, to.X, to.Y, Alphas(periods), true),
                    toShape);
            }
            case RadialGradientBrush radial:
            {
                if (!(radial.RadiusX > 0) || !(radial.RadiusY > 0))
                {
                    return null;
                }

                // In a space where the ellipse is a circle: heights times RadiusX / RadiusY.
                var squash = radial.RadiusX / radial.RadiusY;
                var origin = new Vector2<double>(radial.GradientOrigin.X, radial.GradientOrigin.Y * squash);
                var center = new Vector2<double>(radial.Center.X, radial.Center.Y * squash);
                var startRadius = Math.Max(0, radial.GradientOriginRadius) * radial.RadiusX;
                var endRadius = radial.RadiusX;
                var last = brush.SpreadMethod != GradientSpreadMethod.Pad
                    ? GradientRamp.RadialRepeats(origin, center, startRadius, endRadius, squash)
                    : 1;

                var periods = GradientRamp.Repeated(stops, brush.SpreadMethod, 0, last);
                var farCenter = origin + (center - origin) * last;
                var farRadius = startRadius + (endRadius - startRadius) * last;
                toShape = new PdfMatrix(bounds.Width, 0, 0, bounds.Height / squash, bounds.X, bounds.Y);
                return new Shading(
                    PdfShadings.Radial(origin.X, origin.Y, startRadius, farCenter.X, farCenter.Y, farRadius,
                        Colors(periods)),
                    GradientRamp.IsOpaque(periods)
                        ? null
                        : PdfShadings.Radial(origin.X, origin.Y, startRadius, farCenter.X, farCenter.Y, farRadius,
                            Alphas(periods), true),
                    toShape);
            }
            default:
                return null;
        }
    }

    private static List<PdfGradientStop> Colors(List<RampStop> stops)
    {
        var colors = new List<PdfGradientStop>(stops.Count);
        foreach (var stop in stops)
        {
            colors.Add(new PdfGradientStop(stop.Offset, stop.Red, stop.Green, stop.Blue));
        }

        return colors;
    }

    private static List<PdfGradientStop> Alphas(List<RampStop> stops)
    {
        var alphas = new List<PdfGradientStop>(stops.Count);
        foreach (var stop in stops)
        {
            alphas.Add(PdfGradientStop.Gray(stop.Offset, stop.Alpha));
        }

        return alphas;
    }

    /// <summary>A gradient as PDF draws it: its color shading, its alpha's (null when opaque), and the matrix from
    /// their space to the shape's units.</summary>
    private sealed record Shading(PdfDictionary Color, PdfDictionary Alpha, PdfMatrix ToShape);
}
