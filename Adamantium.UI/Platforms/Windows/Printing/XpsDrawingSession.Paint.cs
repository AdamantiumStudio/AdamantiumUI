using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using Adamantium.Win32.Printing.Xps;
using static Adamantium.UI.Platforms.Windows.Printing.XpsGeometryPath;

namespace Adamantium.UI.Platforms.Windows.Printing;

// Paint: a shape filled and stroked. XPS strokes a pen with one cap at the start, one at the end and one for its dashes,
// of four kinds; a pen with a trim, concave ends, dashes cut unlike them, or dashes round a closed contour is laid out
// as the stroke shader lays it - runs stroked with XPS's own caps where both their ends allow, the rest as shapes.
internal sealed partial class XpsDrawingSession
{
    /// <param name="gradientBox">The box a gradient or tile is laid over, in the geometry's units; null for the
    /// geometry's own bounds.</param>
    private void Figures(Brush brush, Pen pen, Geometry geometry, Matrix4x4F transform, Rect? gradientBox = null)
    {
        if (geometry?.CurvesForDrawing is not { Figures.Count: > 0 } curves)
        {
            return;
        }

        var fills = Paints(brush);
        // A gradient laid along the line has nothing in XPS to go by; it waits for the raster of what has none.
        var strokes = pen != null && pen.Thickness > 0 && Paints(pen.Brush) &&
                      (pen.Brush is SolidColorBrush || pen.BrushMapping != PenBrushMapping.Along);
        if ((!fills && !strokes) || !Invertible(transform))
        {
            return;
        }

        var box = gradientBox ?? curves.Bounds;
        var evenOdd = curves.FillRule == FillRule.EvenOdd;
        var width = strokes ? pen.Thickness * PenUnit(pen, transform) : 0;
        var native = strokes && StrokesAsIs(pen, curves);
        var together = fills && native && AllFilled(curves);
        if (fills && !together)
        {
            var path = new XpsGeometryPath();
            VectorShapes.AddCurves(path, curves, true);
            WritePath(path, evenOdd, transform, brush, box, null, 0, XpsLineCap.Flat, XpsLineJoin.Miter);
        }

        if (!strokes)
        {
            return;
        }

        var strokeBox = pen.Brush is TileBrush
            ? new Rect(box.X - width / 2, box.Y - width / 2, box.Width + width, box.Height + width)
            : box;
        if (native)
        {
            var path = new XpsGeometryPath();
            VectorShapes.AddCurves(path, curves, false);
            WriteStroked(path, evenOdd, transform, together ? brush : null, box, pen, width, strokeBox);
            return;
        }

        StrokeInRuns(pen, width, curves, transform, strokeBox);
    }

    private static bool AllFilled(PathCurves curves)
    {
        foreach (var figure in curves.Figures)
        {
            if (!figure.IsFilled)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Paints(Brush brush) => brush switch
    {
        SolidColorBrush solid => solid.Color.A > 0 && solid.Opacity > 0,
        LinearGradientBrush or RadialGradientBrush => brush.Opacity > 0 && ((GradientBrush)brush).GradientStops.Count > 0,
        TileBrush tile => PaintsTiles(tile),
        _ => false,
    };

    /// <summary>Lengths of a pen measured in the window's units, in the units of the drawing it strokes.</summary>
    private double PenUnit(Pen pen, Matrix4x4F transform)
    {
        if (pen.ThicknessUnit != PenThicknessUnit.Window)
        {
            return 1;
        }

        var toRoot = transform * Current.ToPage;
        var scale = Math.Sqrt(Math.Abs((double)toRoot.M11 * toRoot.M22 - (double)toRoot.M12 * toRoot.M21));
        return scale > 1e-12 ? 1 / scale : 1;
    }

    /// <summary>Whether XPS strokes the pen as it is: no trim, ends of XPS's kinds, and dashes - if any - with one kind
    /// of end and only along open contours, where XPS lays them as the shader does.</summary>
    private static bool StrokesAsIs(Pen pen, PathCurves curves)
    {
        if (pen.TrimStart > 0 || pen.TrimEnd < 1 || !IsNative(pen.StartLineCap) || !IsNative(pen.EndLineCap))
        {
            return false;
        }

        if ((pen.DashStrokeArray?.Count ?? 0) == 0)
        {
            return true;
        }

        if (pen.DashStartCap != pen.DashEndCap || !IsNative(pen.DashStartCap))
        {
            return false;
        }

        foreach (var figure in curves.Figures)
        {
            if (figure.IsClosed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNative(PenLineCap cap) =>
        cap is PenLineCap.Flat or PenLineCap.Square or PenLineCap.ConvexRound or PenLineCap.ConvexTriangle;

    private static XpsLineCap ToXps(PenLineCap cap) => cap switch
    {
        PenLineCap.Square => XpsLineCap.Square,
        PenLineCap.ConvexRound => XpsLineCap.Round,
        PenLineCap.ConvexTriangle => XpsLineCap.Triangle,
        _ => XpsLineCap.Flat,
    };

    private static XpsLineJoin ToXps(PenLineJoin join) => join switch
    {
        PenLineJoin.Round => XpsLineJoin.Round,
        PenLineJoin.Bevel => XpsLineJoin.Bevel,
        _ => XpsLineJoin.Miter,
    };

    /// <summary>A path stroked by the pen as XPS strokes it - its caps, dashes, join and miter - and filled with
    /// <paramref name="fill"/> when given.</summary>
    private void WriteStroked(XpsGeometryPath geometry, bool evenOdd, Matrix4x4F transform, Brush fill, Rect box,
        Pen pen, double width, Rect strokeBox)
    {
        var path = NewPath(geometry, evenOdd, transform);
        if (path == null)
        {
            return;
        }

        try
        {
            if (fill != null)
            {
                SetFill(path, fill, box, transform);
            }

            SetStroke(path, pen.Brush, strokeBox, transform, width, ToXps(pen.StartLineCap), ToXps(pen.EndLineCap),
                ToXps(pen.PenLineJoin), Pen.ClampMiterLimit(pen.MiterLimit));
            var dashes = pen.DashStrokeArray;
            if (dashes is { Count: > 0 })
            {
                Check(path.SetStrokeDashCap((XpsDashCap)ToXps(pen.DashStartCap)));
                Check(path.GetStrokeDashes(out var collection));
                try
                {
                    // XPS counts dashes in widths of the pen; an odd list goes round twice, as the shader has it.
                    var count = dashes.Count % 2 == 1 ? dashes.Count * 2 : dashes.Count;
                    var period = 0.0;
                    for (var index = 0; index < dashes.Count; index++)
                    {
                        period += Math.Max(0, dashes[index]);
                    }

                    for (var index = 0; index + 1 < count; index += 2)
                    {
                        Check(collection.Append(new XpsDash(
                            (float)(Math.Max(0, dashes[index % dashes.Count]) / pen.Thickness),
                            (float)(Math.Max(0, dashes[(index + 1) % dashes.Count]) / pen.Thickness))));
                    }

                    Check(path.SetStrokeDashOffset((float)((pen.DashOffset + pen.DashPhase * period) / pen.Thickness)));
                }
                finally
                {
                    Marshal.ReleaseComObject(collection);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(path);
        }
    }

    /// <summary>The pen stroked in runs: those whose two ends XPS has, with its caps, a path for each pair of ends; the
    /// others without caps, their convex ends filled as shapes; each run with a concave end in a canvas of its own,
    /// cut by its bites. A translucent pen is drawn opaque in a canvas faded once - one coverage where runs and ends
    /// meet, as on screen.</summary>
    private void StrokeInRuns(Pen pen, double width, PathCurves curves, Matrix4x4F transform, Rect box)
    {
        var laid = StrokeRuns.Lay(pen, curves, width);
        var runs = laid.Runs;
        if (runs.Count == 0)
        {
            return;
        }

        var paint = pen.Brush;
        var alpha = paint is SolidColorBrush solid
            ? solid.Color.A / 255.0 * Math.Clamp(solid.Opacity, 0, 1)
            : Math.Clamp(paint.Opacity, 0, 1);
        var faded = alpha < 1 && !_alphaApart;
        if (faded)
        {
            PushGroup(new DrawingGroupOptions { Opacity = alpha });
            _alphaApart = true;
        }

        try
        {
            var half = width / 2;
            var join = ToXps(pen.PenLineJoin);
            var miter = Pen.ClampMiterLimit(pen.MiterLimit);
            var groups = new Dictionary<(PenLineCap, PenLineCap), XpsGeometryPath>();
            var ends = new XpsGeometryPath();
            foreach (var run in runs)
            {
                if (run.IsConcave)
                {
                    continue;
                }

                var capped = IsNative(run.StartCap) && IsNative(run.EndCap) &&
                             (run.End > run.Start || run.StartCap is PenLineCap.ConvexRound or PenLineCap.Flat);
                var key = capped ? (run.StartCap, run.EndCap) : (PenLineCap.Flat, PenLineCap.Flat);
                if (!groups.TryGetValue(key, out var path))
                {
                    path = new XpsGeometryPath();
                    groups[key] = path;
                }

                laid.WriteRun(path, run, run.StartCap == PenLineCap.ConvexRound);
                if (!capped)
                {
                    laid.WriteEnds(ends, run, half, false);
                }
            }

            foreach (var ((start, end), path) in groups)
            {
                StrokePath(path, transform, paint, box, width, ToXps(start), ToXps(end), join, miter);
            }

            if (!ends.IsEmpty)
            {
                WritePath(ends, false, transform, paint, box, null, 0, XpsLineCap.Flat, XpsLineJoin.Miter);
            }

            foreach (var run in runs)
            {
                if (run.IsConcave)
                {
                    StrokeBitten(laid, run, curves, transform, paint, box, width, join, miter);
                }
            }
        }
        finally
        {
            if (faded)
            {
                _alphaApart = false;
                PopGroup();
            }
        }
    }

    /// <summary>A run with a concave end: in a canvas cut by everything but its bites, stroked without caps, its convex
    /// end filled.</summary>
    private void StrokeBitten(StrokeRuns laid, StrokeRun run, PathCurves curves, Matrix4x4F transform, Brush paint,
        Rect box, double width, XpsLineJoin join, double miter)
    {
        var half = width / 2;
        var reach = Math.Max(width, half * miter) * 2;
        var bounds = curves.Bounds;
        var clip = new XpsGeometryPath();
        VectorShapes.AddPolygon(clip, [new(bounds.X - reach, bounds.Y - reach), new(bounds.Right + reach, bounds.Y - reach),
            new(bounds.Right + reach, bounds.Bottom + reach), new(bounds.X - reach, bounds.Bottom + reach)], false);
        laid.WriteEnds(clip, run, half, true);
        var canvas = NewCanvas(Current.Visuals);
        try
        {
            SetClip(canvas, clip, true, transform);
            Check(canvas.GetVisuals(out var visuals));
            _levels.Push(new Level(visuals, null, Current.ToPage, owned: true));
            try
            {
                var line = new XpsGeometryPath();
                laid.WriteRun(line, run);
                StrokePath(line, transform, paint, box, width, XpsLineCap.Flat, XpsLineCap.Flat, join, miter);
                var ends = new XpsGeometryPath();
                if (laid.WriteEnds(ends, run, half, false))
                {
                    WritePath(ends, false, transform, paint, box, null, 0, XpsLineCap.Flat, XpsLineJoin.Miter);
                }
            }
            finally
            {
                PopLevel();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(canvas);
        }
    }

    private void StrokePath(XpsGeometryPath geometry, Matrix4x4F transform, Brush paint, Rect box, double width,
        XpsLineCap start, XpsLineCap end, XpsLineJoin join, double miter)
    {
        var path = NewPath(geometry, false, transform);
        if (path == null)
        {
            return;
        }

        try
        {
            SetStroke(path, paint, box, transform, width, start, end, join, miter);
        }
        finally
        {
            Marshal.ReleaseComObject(path);
        }
    }

    /// <summary>A path filled with <paramref name="fill"/> and stroked with <paramref name="stroke"/> - either may be
    /// null.</summary>
    private void WritePath(XpsGeometryPath geometry, bool evenOdd, Matrix4x4F transform, Brush fill, Rect? box,
        Brush stroke, double width, XpsLineCap cap, XpsLineJoin join)
    {
        var path = NewPath(geometry, evenOdd, transform);
        if (path == null)
        {
            return;
        }

        try
        {
            var area = box ?? default;
            if (fill != null)
            {
                SetFill(path, fill, area, transform);
            }

            if (stroke != null)
            {
                SetStroke(path, stroke, area, transform, width, cap, cap, join, 10);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(path);
        }
    }

    /// <summary>A new path of <paramref name="geometry"/> placed by <paramref name="transform"/>, appended to the
    /// canvas written into; null when the geometry has no figure.</summary>
    private IXpsOMPath NewPath(XpsGeometryPath geometry, bool evenOdd, Matrix4x4F transform)
    {
        var made = geometry.ToGeometry(_factory, evenOdd);
        if (made == null)
        {
            return null;
        }

        try
        {
            Check(_factory.CreatePath(out var path));
            Check(path.SetGeometryLocal(made));
            if (!IsIdentity(transform))
            {
                Check(_factory.CreateMatrixTransform(ToXps(transform), out var matrix));
                try
                {
                    Check(path.SetTransformLocal(matrix));
                }
                finally
                {
                    Marshal.ReleaseComObject(matrix);
                }
            }

            Check(Current.Visuals.Append((IXpsOMVisual)path));
            return path;
        }
        finally
        {
            Marshal.ReleaseComObject(made);
        }
    }

    private void SetFill(IXpsOMPath path, Brush brush, Rect box, Matrix4x4F transform)
    {
        var made = BrushOf(brush, box, transform);
        if (made == null)
        {
            return;
        }

        try
        {
            Check(path.SetFillBrushLocal(made));
        }
        finally
        {
            Marshal.ReleaseComObject(made);
        }
    }

    private void SetStroke(IXpsOMPath path, Brush brush, Rect box, Matrix4x4F transform, double width, XpsLineCap start,
        XpsLineCap end, XpsLineJoin join, double miter)
    {
        var made = BrushOf(brush, box, transform);
        if (made == null)
        {
            return;
        }

        try
        {
            Check(path.SetStrokeBrushLocal(made));
            Check(path.SetStrokeThickness((float)width));
            Check(path.SetStrokeStartLineCap(start));
            Check(path.SetStrokeEndLineCap(end));
            Check(path.SetStrokeLineJoin(join));
            Check(path.SetStrokeMiterLimit((float)Math.Max(1, miter)));
        }
        finally
        {
            Marshal.ReleaseComObject(made);
        }
    }

    /// <summary>The XPS brush of <paramref name="brush"/> laid over <paramref name="box"/>; null when it paints nothing.
    /// </summary>
    private IXpsOMBrush BrushOf(Brush brush, Rect box, Matrix4x4F transform) => brush switch
    {
        SolidColorBrush solid => Solid(solid.Color, solid.Opacity),
        GradientBrush gradient => Gradient(gradient, box),
        TileBrush tile => Tiles(tile, box, transform),
        _ => null,
    };

    private IXpsOMBrush Solid(Color color, double opacity)
    {
        var tinted = Tinted(color);
        var alpha = _alphaApart ? (byte)255 : (byte)Math.Round(tinted.A * Math.Clamp(opacity, 0, 1));
        Check(_factory.CreateSolidColorBrush(XpsColor.FromSrgb(alpha, tinted.R, tinted.G, tinted.B), IntPtr.Zero,
            out var brush));
        return (IXpsOMBrush)brush;
    }

    /// <summary>A linear or radial gradient over <paramref name="box"/> - its points fractions of it - its stops as the
    /// renderer draws them; null when it draws nothing.</summary>
    private IXpsOMBrush Gradient(GradientBrush brush, Rect box)
    {
        if (!(box.Width > 0) || !(box.Height > 0))
        {
            return null;
        }

        var stops = GradientRamp.Of(brush, _tint);
        if (stops.Count == 0)
        {
            return null;
        }

        if (stops.Count == 1)
        {
            // One color: the stop's, tinted already, its own alpha kept - only the brush's goes to a canvas apart.
            var only = stops[0];
            var opacity = _alphaApart ? 1 : Math.Clamp(brush.Opacity, 0, 1);
            Check(_factory.CreateSolidColorBrush(XpsColor.FromSrgb((byte)Math.Round(only.Alpha * opacity * 255),
                (byte)Math.Round(only.Red * 255), (byte)Math.Round(only.Green * 255), (byte)Math.Round(only.Blue * 255)),
                IntPtr.Zero, out var single));
            return (IXpsOMBrush)single;
        }

        // A repeated or reflected gradient is written out over the repeats that cover the box, then padded - as in PDF:
        // a driver turning XPS's own spreading into its output can make more of it than a reader takes.
        var spread = brush.SpreadMethod;
        List<RampStop> periods;
        Vector2<double> from;
        Vector2<double> to;
        Vector2<double> origin = default;
        Size<double> radii = default;
        switch (brush)
        {
            case LinearGradientBrush linear:
            {
                var axis = linear.EndPoint - linear.StartPoint;
                if (!(axis.X * axis.X + axis.Y * axis.Y > 1e-18))
                {
                    return null;
                }

                var (firstRepeat, lastRepeat) = spread != GradientSpreadMethod.Pad
                    ? GradientRamp.LinearRepeats(linear.StartPoint, linear.EndPoint)
                    : (0, 1);
                periods = GradientRamp.Repeated(stops, spread, firstRepeat, lastRepeat);
                from = linear.StartPoint + axis * firstRepeat;
                to = linear.StartPoint + axis * lastRepeat;
                break;
            }
            case RadialGradientBrush radial:
            {
                if (!(radial.RadiusX > 0) || !(radial.RadiusY > 0))
                {
                    return null;
                }

                // Repeats counted where the ellipse is a circle: heights times RadiusX / RadiusY.
                var squash = radial.RadiusX / radial.RadiusY;
                var startRadius = Math.Clamp(radial.GradientOriginRadius, 0, 1) * radial.RadiusX;
                var last = spread != GradientSpreadMethod.Pad
                    ? GradientRamp.RadialRepeats(
                        new Vector2<double>(radial.GradientOrigin.X, radial.GradientOrigin.Y * squash),
                        new Vector2<double>(radial.Center.X, radial.Center.Y * squash), startRadius, radial.RadiusX,
                        squash)
                    : 1;
                periods = GradientRamp.Repeated(stops, spread, 0, last);
                origin = radial.GradientOrigin;
                from = origin + (radial.Center - origin) * last;
                to = from;
                var farRadius = startRadius + (radial.RadiusX - startRadius) * last;
                radii = new Size<double>(farRadius, farRadius / squash);

                // XPS has no inner circle: the ramp starts that far out instead - as drawn where the origin is the
                // center.
                var inner = startRadius / farRadius;
                if (inner > 0)
                {
                    for (var index = 0; index < periods.Count; index++)
                    {
                        periods[index] = periods[index] with { Offset = inner + periods[index].Offset * (1 - inner) };
                    }
                }

                break;
            }
            default:
                return null;
        }

        var first = Stop(periods[0]);
        var second = Stop(periods[1]);
        try
        {
            IXpsOMBrush made;
            IXpsOMGradientStopCollection collection;
            if (brush is LinearGradientBrush)
            {
                Check(_factory.CreateLinearGradientBrush(first, second, new XpsPoint((float)from.X, (float)from.Y),
                    new XpsPoint((float)to.X, (float)to.Y), out var linearBrush));
                Check(linearBrush.SetColorInterpolationMode(XpsColorInterpolation.SrgbLinear));
                Check(linearBrush.GetGradientStops(out collection));
                SetBoxTransform(linearBrush.SetTransformLocal, box);
                made = (IXpsOMBrush)linearBrush;
            }
            else
            {
                Check(_factory.CreateRadialGradientBrush(first, second, new XpsPoint((float)from.X, (float)from.Y),
                    new XpsPoint((float)origin.X, (float)origin.Y),
                    new XpsSize((float)radii.Width, (float)radii.Height), out var radialBrush));
                Check(radialBrush.SetColorInterpolationMode(XpsColorInterpolation.SrgbLinear));
                Check(radialBrush.GetGradientStops(out collection));
                SetBoxTransform(radialBrush.SetTransformLocal, box);
                made = (IXpsOMBrush)radialBrush;
            }


            try
            {
                for (var index = 2; index < periods.Count; index++)
                {
                    var stop = Stop(periods[index]);
                    try
                    {
                        Check(collection.Append(stop));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(stop);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }

            if (brush.Opacity < 1 && !_alphaApart)
            {
                Check(made.SetOpacity((float)Math.Clamp(brush.Opacity, 0, 1)));
            }

            return made;
        }
        finally
        {
            Marshal.ReleaseComObject(first);
            Marshal.ReleaseComObject(second);
        }
    }

    private IXpsOMGradientStop Stop(RampStop stop)
    {
        var color = XpsColor.FromSrgb((byte)Math.Round(stop.Alpha * 255), (byte)Math.Round(stop.Red * 255),
            (byte)Math.Round(stop.Green * 255), (byte)Math.Round(stop.Blue * 255));
        Check(_factory.CreateGradientStop(color, IntPtr.Zero, (float)Math.Clamp(stop.Offset, 0, 1), out var made));
        return made;
    }

    /// <summary>Sets a brush's transform to take the unit square onto <paramref name="box"/>.</summary>
    private void SetBoxTransform(Func<IXpsOMMatrixTransform, int> set, Rect box)
    {
        Check(_factory.CreateMatrixTransform(new XpsMatrix((float)box.Width, 0, 0, (float)box.Height, (float)box.X,
            (float)box.Y), out var matrix));
        try
        {
            Check(set(matrix));
        }
        finally
        {
            Marshal.ReleaseComObject(matrix);
        }
    }
}
