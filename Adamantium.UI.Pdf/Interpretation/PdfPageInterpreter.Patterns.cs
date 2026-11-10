using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Shadings;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf.Interpretation;

// Shadings and patterns (ISO 32000-2, 8.7). An area painted with a pattern is a group cut to the area; inside it the
// pattern is laid over everything the cut can let through - the page's box - in pattern space. A shading is drawn in a
// space of its own where its brush lies along the axes: an axial one along its axis, a function one over its domain.
internal sealed partial class PdfPageInterpreter
{
    // How far a sampled gradient may stray from its function before another stop is put in, in sRGB; and the most
    // stops one gradient gets.
    private const double StopTolerance = 1.0 / 255;
    private const int MaxStops = 256;
    private const int FunctionSamples = 256;

    private Rect _pageBox;

    private void PaintShading(string name, PdfFrame frame)
    {
        var shading = PdfShading.Create(frame.Resources.Shading(name), frame.Resources, _document);
        if (shading == null)
        {
            Tell($"The shading '{name}' could not be read.");
            return;
        }

        // sh paints the shading wherever the clip lets it, in user space, with no background.
        var grouped = BeginObjectGroup(frame.Output);
        DrawShading(shading, _state.Ctm, _state.FillAlpha, frame.Output);
        EndObjectGroup(grouped, frame.Output);
    }

    private void PaintPattern(PdfPaint paint, Geometry area, Matrix4x4F areaMatrix, PdfFrame frame, bool stroking)
    {
        var alpha = stroking ? _state.StrokeAlpha : _state.FillAlpha;
        OpenGroup(new PdfPushGroupStep { Clip = area, ClipMatrix = areaMatrix }, frame.Output);
        var space = paint.Pattern.Matrix.Then(paint.PatternBase);
        switch (paint.Pattern)
        {
            case PdfShadingPattern shading:
                var patternAlpha = alpha * (shading.Parameters?.FillAlpha ?? 1);
                if (shading.Shading.Background is { } background)
                {
                    Span<double> rgb = stackalloc double[3];
                    shading.Shading.ColorSpace.ToRgb(background, rgb);
                    Write(new PdfGeometryStep(new RectangleGeometry(_pageBox), Solid((rgb[0], rgb[1], rgb[2]),
                        patternAlpha), null, Matrix4x4F.Identity), frame.Output);
                }

                DrawShading(shading.Shading, space, patternAlpha, frame.Output);
                break;
            case PdfTilingPattern tiling when paint.Cell != null:
                Write(new PdfGeometryStep(new RectangleGeometry(Cover(space)), TileBrush(tiling, paint.Cell, alpha), null,
                    PdfMatrices.ToDrawing(space)), frame.Output);
                break;
        }

        CloseGroupsDownTo(_openGroups - 1, frame.Output);
    }

    private void DrawShading(PdfShading shading, PdfMatrix space, double alpha, List<PdfDrawStep> output)
    {
        var cut = shading.BBox is { } box;
        if (cut)
        {
            var b = shading.BBox.Value;
            OpenGroup(new PdfPushGroupStep
            {
                Clip = new RectangleGeometry(new Rect(Math.Min(b.Left, b.Right), Math.Min(b.Bottom, b.Top),
                    Math.Abs(b.Width), Math.Abs(b.Height))),
                ClipMatrix = PdfMatrices.ToDrawing(space),
            }, output);
        }

        switch (shading)
        {
            case PdfAxialShading axial:
                DrawAxial(axial, space, alpha, output);
                break;
            case PdfRadialShading radial:
                DrawRadial(radial, space, alpha, output);
                break;
            case PdfFunctionShading function:
                DrawFunction(function, space, alpha, output);
                break;
            default:
                Tell($"A shading of type {shading.Type} is not drawn yet.");
                break;
        }

        if (cut)
        {
            CloseGroupsDownTo(_openGroups - 1, output);
        }
    }

    // In axis space u runs from 0 at the start to 1 at the end, and v across in the shading's units.
    private void DrawAxial(PdfAxialShading shading, PdfMatrix space, double alpha, List<PdfDrawStep> output)
    {
        var (dx, dy) = (shading.End.X - shading.Start.X, shading.End.Y - shading.Start.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-12)
        {
            return;
        }

        var axis = new PdfMatrix(dx, dy, -dy / length, dx / length, shading.Start.X, shading.Start.Y).Then(space);
        var cover = Cover(axis);
        var from = shading.ExtendStart ? Math.Min(cover.X, 0) : 0;
        var to = shading.ExtendEnd ? Math.Max(cover.Right, 1) : 1;
        if (to <= from)
        {
            return;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Vector2<double>(-from / (to - from), 0.5),
            EndPoint = new Vector2<double>((1 - from) / (to - from), 0.5),
            SpreadMethod = GradientSpreadMethod.Pad,
        };
        AddStops(brush, shading, alpha);
        Write(new PdfGeometryStep(new RectangleGeometry(new Rect(from, cover.Y, to - from, cover.Height)),
            brush.CaptureBase(), null, PdfMatrices.ToDrawing(axis)), output);
    }

    // A radial shading the brush draws as it is: its start circle a point - the focus - inside the end circle.
    private void DrawRadial(PdfRadialShading shading, PdfMatrix space, double alpha, List<PdfDrawStep> output)
    {
        var (x1, y1, r1) = (shading.EndCenter.X, shading.EndCenter.Y, shading.EndRadius);
        var (fx, fy) = (shading.StartCenter.X - x1, shading.StartCenter.Y - y1);
        if (shading.StartRadius > 1e-9 || r1 <= 1e-12 || fx * fx + fy * fy > r1 * r1)
        {
            Tell("A radial shading whose start circle is not a point inside its end circle is not drawn yet.");
            return;
        }

        var area = shading.ExtendEnd ? Cover(space) : new Rect(x1 - r1, y1 - r1, 2 * r1, 2 * r1);
        area = Union(area, new Rect(x1 - r1, y1 - r1, 2 * r1, 2 * r1));
        var brush = new RadialGradientBrush
        {
            Center = Relative(area, x1, y1),
            GradientOrigin = Relative(area, x1 + fx, y1 + fy),
            RadiusX = r1 / area.Width,
            RadiusY = r1 / area.Height,
            SpreadMethod = GradientSpreadMethod.Pad,
        };
        AddStops(brush, shading, alpha);
        Geometry geometry = shading.ExtendEnd
            ? new RectangleGeometry(area)
            : new EllipseGeometry(new Rect(x1 - r1, y1 - r1, 2 * r1, 2 * r1));
        Write(new PdfGeometryStep(geometry, brush.CaptureBase(), null, PdfMatrices.ToDrawing(space)), output);
    }

    // The function sampled over its domain into a picture: a smooth function keeps its look at any zoom.
    private void DrawFunction(PdfFunctionShading shading, PdfMatrix space, double alpha, List<PdfDrawStep> output)
    {
        var (x0, x1, y0, y1) = (shading.Domain[0], shading.Domain[1], shading.Domain[2], shading.Domain[3]);
        const int side = FunctionSamples;
        var rgba = new byte[side * side * 4];
        Span<double> input = stackalloc double[2];
        Span<double> rgb = stackalloc double[3];
        for (var row = 0; row < side; row++)
        {
            for (var column = 0; column < side; column++)
            {
                input[0] = x0 + (column + 0.5) / side * (x1 - x0);
                input[1] = y0 + (row + 0.5) / side * (y1 - y0);
                shading.ToRgb(input, rgb);
                var at = (row * side + column) * 4;
                rgba[at] = Byte(rgb[0]);
                rgba[at + 1] = Byte(rgb[1]);
                rgba[at + 2] = Byte(rgb[2]);
                rgba[at + 3] = Byte(alpha);
            }
        }

        var domain = new PdfMatrix(x1 - x0, 0, 0, y1 - y0, x0, y0).Then(shading.Matrix).Then(space);
        Write(new PdfImageStep(ToImage(side, side, rgba), PdfMatrices.ToDrawing(domain)), output);
    }

    // The gradient's colors at stops close enough that drawing straight between them strays no more than
    // StopTolerance from the function - split where it would, no more than MaxStops in all.
    private void AddStops(GradientBrush brush, PdfGradientShading shading, double alpha)
    {
        var stops = new List<(double At, (double R, double G, double B) Color)>
        {
            (0, ColorAt(shading, 0)), (1, ColorAt(shading, 1)),
        };
        for (var i = 0; i + 1 < stops.Count && stops.Count < MaxStops;)
        {
            var (a, ca) = stops[i];
            var (b, cb) = stops[i + 1];
            var middle = (a + b) / 2;
            var cm = ColorAt(shading, middle);
            var straight = ((ca.R + cb.R) / 2, (ca.G + cb.G) / 2, (ca.B + cb.B) / 2);
            if (b - a > 1.0 / MaxStops && Distance(cm, straight) > StopTolerance)
            {
                stops.Insert(i + 1, (middle, cm));
            }
            else
            {
                i++;
            }
        }

        foreach (var (at, color) in stops)
        {
            brush.GradientStops.Add(new GradientStop(ColorOf(Pack(color, alpha)), at));
        }
    }

    private static (double R, double G, double B) ColorAt(PdfGradientShading shading, double u)
    {
        Span<double> input = stackalloc double[1];
        Span<double> rgb = stackalloc double[3];
        input[0] = shading.T0 + u * (shading.T1 - shading.T0);
        shading.ToRgb(input, rgb);
        return (rgb[0], rgb[1], rgb[2]);
    }

    private static double Distance((double R, double G, double B) a, (double R, double G, double B) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    private Brush TileBrush(PdfTilingPattern tiling, PdfContentDrawing cell, double alpha)
    {
        var box = tiling.BBox;
        var tile = new Rect(Math.Min(box.Left, box.Right), Math.Min(box.Bottom, box.Top), Math.Abs(tiling.XStep),
            Math.Abs(tiling.YStep));
        return new DrawingBrush(cell)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            Viewbox = tile,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Opacity = alpha,
        }.CaptureBase();
    }

    // The page's box as seen in a space placed by `space`: all a group cut to the page can let through.
    private Rect Cover(PdfMatrix space)
    {
        if (space.Inverse() is not { } inverse)
        {
            return Rect.Empty;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var (x, y) in new[]
                 {
                     (_pageBox.X, _pageBox.Y), (_pageBox.Right, _pageBox.Y), (_pageBox.Right, _pageBox.Bottom),
                     (_pageBox.X, _pageBox.Bottom),
                 })
        {
            var point = inverse.Transform(x, y);
            (left, right) = (Math.Min(left, point.X), Math.Max(right, point.X));
            (top, bottom) = (Math.Min(top, point.Y), Math.Max(bottom, point.Y));
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private static Rect Union(Rect a, Rect b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        return new Rect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    private static Vector2<double> Relative(Rect area, double x, double y) =>
        new((x - area.X) / area.Width, (y - area.Y) / area.Height);
}
