using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Writing;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Pdf;

// Paint: a fill or a stroke with a solid color or a gradient. A gradient is a shading laid over the shape's bounds as
// the renderer lays it - its points a fraction of them - and its stops' alpha a soft mask of the same shape.
public sealed partial class PdfContentSession
{
    /// <summary>How many stops a gradient is drawn through when its colors run in OKLab: each span between two stops is
    /// cut into this many, the color of each cut worked out in OKLab, and PDF runs straight between them.</summary>
    private const int OklabSteps = 16;

    /// <summary>How many repeats of a repeated or reflected gradient are written at most.</summary>
    private const int MaxRepeats = 64;

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
                wrote |= Fill(brush, curves, place, gradientBox ?? curves.Bounds);
            }

            if (strokes)
            {
                wrote |= Stroke(pen, width, curves, place, reach, gradientBox ?? curves.Bounds);
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
        content.FillColor(fill.Color.R / 255.0, fill.Color.G / 255.0, fill.Color.B / 255.0);
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

    private bool Fill(Brush brush, PathCurves curves, PdfMatrix place, Rect gradientBox)
    {
        var content = Content;
        var evenOdd = curves.FillRule == FillRule.EvenOdd;
        content.SaveState();
        if (brush is SolidColorBrush solid)
        {
            Opacity(1, Alpha(solid));
            content.FillColor(solid.Color.R / 255.0, solid.Color.G / 255.0, solid.Color.B / 255.0);
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
            var tiles = Tiles(tile, gradientBox);
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

    private bool Stroke(Pen pen, double width, PathCurves curves, PdfMatrix place, double reach, Rect gradientBox)
    {
        var content = Content;
        content.SaveState();
        Shading gradient = null;
        var solid = pen.Brush as SolidColorBrush;
        // Ends drawn as shapes are filled with the stroke's own alpha.
        var alpha = solid != null ? Alpha(solid) : Math.Clamp(pen.Brush.Opacity, 0, 1);
        var asIs = StrokesAsIs(pen);
        PdfObject pattern = null;
        if (pen.Brush is TileBrush tile)
        {
            var tiles = Tiles(tile, gradientBox);
            if (tiles == null)
            {
                content.RestoreState();
                return false;
            }

            pattern = tiles.Pattern(place);
        }
        else if (solid == null)
        {
            gradient = Gradient((GradientBrush)pen.Brush, gradientBox);
            if (gradient == null)
            {
                content.RestoreState();
                return false;
            }

            AlphaMask(gradient, place, Box(curves.Bounds, place, reach));
            pattern = PdfShadings.Pattern(gradient.Color, gradient.ToShape.Then(place));
        }

        if (pattern != null)
        {
            content.StrokePattern(pattern);
        }

        Opacity(alpha, asIs ? 1 : alpha);
        Transform(place);
        Pen(pen, width, solid?.Color);
        bool wrote;
        if (asIs)
        {
            wrote = Write(curves, false, PdfMatrix.Identity);
            if (wrote)
            {
                content.Stroke();
            }
            else
            {
                content.EndPath();
            }
        }
        else
        {
            wrote = StrokeRuns(pen, curves, width, () =>
            {
                if (solid != null)
                {
                    content.FillColor(solid.Color.R / 255.0, solid.Color.G / 255.0, solid.Color.B / 255.0);
                }
                else
                {
                    content.FillPattern(pattern);
                }

                content.Fill();
            });
        }

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
            content.StrokeColor(solid.R / 255.0, solid.G / 255.0, solid.B / 255.0);
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
    private static Shading Gradient(GradientBrush brush, Rect bounds)
    {
        if (!(bounds.Width > 0) || !(bounds.Height > 0))
        {
            return null;
        }

        var stops = Stops(brush);
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

                var first = 0;
                var last = 1;
                if (brush.SpreadMethod != GradientSpreadMethod.Pad)
                {
                    var low = double.PositiveInfinity;
                    var high = double.NegativeInfinity;
                    foreach (var corner in Corners)
                    {
                        var along = ((corner.X - start.X) * axis.X + (corner.Y - start.Y) * axis.Y) / squared;
                        low = Math.Min(low, along);
                        high = Math.Max(high, along);
                    }

                    (first, last) = Repeats(low, high);
                }

                var periods = Repeated(stops, brush.SpreadMethod, first, last);
                var from = start + axis * first;
                var to = start + axis * last;
                return new Shading(
                    PdfShadings.Axial(from.X, from.Y, to.X, to.Y, Colors(periods)),
                    Opaque(periods) ? null : PdfShadings.Axial(from.X, from.Y, to.X, to.Y, Alphas(periods), true),
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
                var last = 1;
                if (brush.SpreadMethod != GradientSpreadMethod.Pad)
                {
                    last = 1;
                    while (last < MaxRepeats && !Covers(origin, center, startRadius, endRadius, last, squash))
                    {
                        last++;
                    }
                }

                var periods = Repeated(stops, brush.SpreadMethod, 0, last);
                var farCenter = origin + (center - origin) * last;
                var farRadius = startRadius + (endRadius - startRadius) * last;
                toShape = new PdfMatrix(bounds.Width, 0, 0, bounds.Height / squash, bounds.X, bounds.Y);
                return new Shading(
                    PdfShadings.Radial(origin.X, origin.Y, startRadius, farCenter.X, farCenter.Y, farRadius,
                        Colors(periods)),
                    Opaque(periods)
                        ? null
                        : PdfShadings.Radial(origin.X, origin.Y, startRadius, farCenter.X, farCenter.Y, farRadius,
                            Alphas(periods), true),
                    toShape);
            }
            default:
                return null;
        }
    }

    private static readonly Vector2<double>[] Corners = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];

    private static (int First, int Last) Repeats(double low, double high)
    {
        var first = (int)Math.Floor(Math.Max(low, -MaxRepeats));
        var last = (int)Math.Ceiling(Math.Min(high, MaxRepeats));
        if (last <= first)
        {
            last = first + 1;
        }

        if (last - first > MaxRepeats)
        {
            var middle = (first + last) / 2;
            first = middle - MaxRepeats / 2;
            last = first + MaxRepeats;
        }

        return (first, last);
    }

    /// <summary>Whether the circle <paramref name="times"/> repeats out reaches every corner of the bounds.</summary>
    private static bool Covers(Vector2<double> origin, Vector2<double> center, double startRadius, double endRadius,
        int times, double squash)
    {
        var at = origin + (center - origin) * times;
        var radius = startRadius + (endRadius - startRadius) * times;
        foreach (var corner in Corners)
        {
            var offset = new Vector2<double>(corner.X, corner.Y * squash) - at;
            if (Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y) > radius)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The stops the renderer draws - the first <see cref="GradientBrush.MaxRenderedStops"/> by offset - as
    /// colors 0..1, cut finer where they run in OKLab.</summary>
    private static List<Stop> Stops(GradientBrush brush)
    {
        var sorted = new List<GradientStop>(brush.GradientStops);
        sorted.Sort((one, other) => one.Offset.CompareTo(other.Offset));
        if (sorted.Count > GradientBrush.MaxRenderedStops)
        {
            sorted.RemoveRange(GradientBrush.MaxRenderedStops, sorted.Count - GradientBrush.MaxRenderedStops);
        }

        var stops = new List<Stop>(sorted.Count);
        foreach (var stop in sorted)
        {
            stops.Add(new Stop(Math.Clamp(stop.Offset, 0, 1), stop.Color.R / 255.0, stop.Color.G / 255.0,
                stop.Color.B / 255.0, stop.Color.A / 255.0));
        }

        if (brush.ColorInterpolationMode != ColorInterpolationMode.Oklab || stops.Count < 2)
        {
            return stops;
        }

        var fine = new List<Stop>(stops.Count * OklabSteps);
        for (var index = 0; index + 1 < stops.Count; index++)
        {
            for (var step = 0; step < OklabSteps; step++)
            {
                fine.Add(Oklab.Between(stops[index], stops[index + 1], step / (double)OklabSteps));
            }
        }

        fine.Add(stops[^1]);
        return fine;
    }

    /// <summary>The stops laid out over the repeats from <paramref name="first"/> to <paramref name="last"/>, each
    /// reflected repeat backwards, as offsets 0..1 of the whole. Each repeat holds its end colors to its own ends, so
    /// one meets the next at a hard edge, as on screen.</summary>
    private static List<Stop> Repeated(List<Stop> stops, GradientSpreadMethod spread, int first, int last)
    {
        if (spread == GradientSpreadMethod.Pad || last - first <= 1 && first == 0)
        {
            return stops;
        }

        if (stops[0].Offset > 0 || stops[^1].Offset < 1)
        {
            var whole = new List<Stop>(stops.Count + 2);
            if (stops[0].Offset > 0)
            {
                whole.Add(stops[0] with { Offset = 0 });
            }

            whole.AddRange(stops);
            if (stops[^1].Offset < 1)
            {
                whole.Add(stops[^1] with { Offset = 1 });
            }

            stops = whole;
        }

        var span = (double)(last - first);
        var all = new List<Stop>(stops.Count * (last - first));
        for (var repeat = first; repeat < last; repeat++)
        {
            var backwards = spread == GradientSpreadMethod.Reflect && (repeat & 1) != 0;
            for (var index = 0; index < stops.Count; index++)
            {
                var stop = stops[backwards ? stops.Count - 1 - index : index];
                var offset = backwards ? 1 - stop.Offset : stop.Offset;
                all.Add(stop with { Offset = (repeat - first + offset) / span });
            }
        }

        return all;
    }

    private static List<PdfGradientStop> Colors(List<Stop> stops)
    {
        var colors = new List<PdfGradientStop>(stops.Count);
        foreach (var stop in stops)
        {
            colors.Add(new PdfGradientStop(stop.Offset, stop.Red, stop.Green, stop.Blue));
        }

        return colors;
    }

    private static List<PdfGradientStop> Alphas(List<Stop> stops)
    {
        var alphas = new List<PdfGradientStop>(stops.Count);
        foreach (var stop in stops)
        {
            alphas.Add(PdfGradientStop.Gray(stop.Offset, stop.Alpha));
        }

        return alphas;
    }

    private static bool Opaque(List<Stop> stops)
    {
        foreach (var stop in stops)
        {
            if (stop.Alpha < 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A gradient's color, 0..1, straight alpha.</summary>
    private readonly record struct Stop(double Offset, double Red, double Green, double Blue, double Alpha);

    /// <summary>A gradient as PDF draws it: its color shading, its alpha's (null when opaque), and the matrix from
    /// their space to the shape's units.</summary>
    private sealed record Shading(PdfDictionary Color, PdfDictionary Alpha, PdfMatrix ToShape);

    /// <summary>Colors mixed in OKLab (Björn Ottosson, 2020), as the renderer mixes them when asked: sRGB to linear to
    /// LMS to OKLab, mixed, and back.</summary>
    private static class Oklab
    {
        public static Stop Between(Stop from, Stop to, double share)
        {
            var one = ToLab(from);
            var two = ToLab(to);
            var mixed = new Vector3<double>(
                one.X + (two.X - one.X) * share,
                one.Y + (two.Y - one.Y) * share,
                one.Z + (two.Z - one.Z) * share);
            var color = ToRgb(mixed);
            return new Stop(from.Offset + (to.Offset - from.Offset) * share, color.X, color.Y, color.Z,
                from.Alpha + (to.Alpha - from.Alpha) * share);
        }

        private static Vector3<double> ToLab(Stop stop)
        {
            var red = Linear(stop.Red);
            var green = Linear(stop.Green);
            var blue = Linear(stop.Blue);
            var longCone = Math.Cbrt(0.4122214708 * red + 0.5363325363 * green + 0.0514459929 * blue);
            var middleCone = Math.Cbrt(0.2119034982 * red + 0.6806995451 * green + 0.1073969566 * blue);
            var shortCone = Math.Cbrt(0.0883024619 * red + 0.2817188376 * green + 0.6299787005 * blue);
            return new Vector3<double>(
                0.2104542553 * longCone + 0.7936177850 * middleCone - 0.0040720468 * shortCone,
                1.9779984951 * longCone - 2.4285922050 * middleCone + 0.4505937099 * shortCone,
                0.0259040371 * longCone + 0.7827717662 * middleCone - 0.8086757660 * shortCone);
        }

        private static Vector3<double> ToRgb(Vector3<double> lab)
        {
            var longCone = Math.Pow(lab.X + 0.3963377774 * lab.Y + 0.2158037573 * lab.Z, 3);
            var middleCone = Math.Pow(lab.X - 0.1055613458 * lab.Y - 0.0638541728 * lab.Z, 3);
            var shortCone = Math.Pow(lab.X - 0.0894841775 * lab.Y - 1.2914855480 * lab.Z, 3);
            return new Vector3<double>(
                Encoded(4.0767416621 * longCone - 3.3077115913 * middleCone + 0.2309699292 * shortCone),
                Encoded(-1.2684380046 * longCone + 2.6097574011 * middleCone - 0.3413193965 * shortCone),
                Encoded(-0.0041960863 * longCone - 0.7034186147 * middleCone + 1.7076147010 * shortCone));
        }

        private static double Linear(double value) =>
            value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

        private static double Encoded(double value)
        {
            value = Math.Clamp(value, 0, 1);
            return value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
        }
    }
}
