using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using Adamantium.Pdf.Shadings;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf.Interpretation;

// Shadings and patterns (ISO 32000-2, 8.7). An area painted with a pattern is a group cut to the area; inside it the
// pattern is laid over everything the cut can let through - the page's box - in pattern space. A shading is drawn in a
// space of its own where its brush lies along the axes: an axial one along its axis, a function one over its domain.
internal sealed partial class PdfPageInterpreter
{
    // How far a gradient's brush may stray from its function, in sRGB; and how many samples a function is drawn with
    // where a brush's stops cannot follow it.
    private const double StopTolerance = 1.0 / 255;

    // How far a radial shading's brush may stray before it is told: past what an eye tells apart.
    private const double VisibleTolerance = 4.0 / 255;

    private void TellIfVisiblyNear(double strays)
    {
        if (strays > VisibleTolerance)
        {
            Tell($"A radial shading is drawn through {GradientBrush.MaxRenderedStops} stops, its colors off by up " +
                 $"to {Math.Ceiling(strays * 255)} of 255.");
        }
    }

    private const int FunctionSamples = 256;

    // The longer side of the picture a radial shading the brush cannot draw is worked out into.
    private const int RadialRasterSide = 1024;

    private readonly Dictionary<PdfObject, PdfShading> _shadings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfFunctionShading, ImageSource> _sampled = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(PdfRadialShading, Rect, double), ImageSource> _rasterized = [];
    private readonly Dictionary<(PdfMeshShading, double), ColoredMesh> _meshes = [];
    private Rect _pageBox;

    /// <summary>What a pattern may cover in the stream being walked: the page, or the cell being drawn.</summary>
    private Rect Area => _frames.Count > 0 ? _frames.Peek().Area : _pageBox;

    // A shading is read once however often it is painted.
    private PdfShading ShadingOf(PdfObject value, PdfResources resources)
    {
        if (value == null)
        {
            return null;
        }

        if (!_shadings.TryGetValue(value, out var shading))
        {
            shading = PdfShading.Create(value, resources, _document);
            _shadings[value] = shading;
        }

        return shading;
    }

    private void PaintShading(string name, PdfFrame frame)
    {
        var shading = ShadingOf(frame.Resources.Shading(name), frame.Resources);
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
        var space = paint.Pattern.Matrix.Then(paint.PatternBase);
        if (Cover(space) is not { Width: > 0, Height: > 0 } ||
            !OpenGroup(new PdfPushGroupStep { Clip = area, ClipMatrix = areaMatrix }, frame.Output))
        {
            return;
        }

        switch (paint.Pattern)
        {
            case PdfShadingPattern shading:
                var patternAlpha = alpha * (shading.Parameters?.FillAlpha ?? 1);
                if (shading.Shading.Background is { } background)
                {
                    Span<double> rgb = stackalloc double[3];
                    shading.Shading.ColorSpace.ToRgb(background, rgb);
                    Write(new PdfGeometryStep(new RectangleGeometry(Area), Solid((rgb[0], rgb[1], rgb[2]),
                        patternAlpha), null, Matrix4x4F.Identity), frame.Output);
                }

                DrawShading(shading.Shading, space, patternAlpha, frame.Output);
                break;
            case PdfTilingPattern tiling when paint.Cell != null:
                var cover = Cover(space);
                Write(new PdfGeometryStep(new RectangleGeometry(cover), TileBrush(tiling, paint.Cell, alpha, cover), null,
                    PdfMatrices.ToDrawing(space)), frame.Output);
                break;
        }

        CloseGroupsDownTo(_openGroups - 1, frame.Output);
    }

    private void DrawShading(PdfShading shading, PdfMatrix space, double alpha, List<PdfDrawStep> output)
    {
        if (Cover(space) is not { Width: > 0, Height: > 0 })
        {
            return;
        }

        var cut = shading.BBox is { } b && OpenGroup(new PdfPushGroupStep
        {
            Clip = new RectangleGeometry(new Rect(Math.Min(b.Left, b.Right), Math.Min(b.Bottom, b.Top),
                Math.Abs(b.Width), Math.Abs(b.Height))),
            ClipMatrix = PdfMatrices.ToDrawing(space),
        }, output);

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
            case PdfMeshShading mesh:
                // Cut as fine as the scale it is shown at asks; see through as one, so triangles that overlap do not
                // darken where they do.
                var scale = PdfMatrices.Scale(space);
                if (!_meshes.TryGetValue((mesh, scale), out var triangles))
                {
                    triangles = PdfMeshTessellator.Tessellate(mesh, space, out var left);
                    if (left)
                    {
                        Tell($"A mesh shading is drawn with its first {PdfMeshTessellator.MaxTriangles} triangles only.");
                    }

                    _meshes[(mesh, scale)] = triangles;
                }

                var faded = alpha < 1 && OpenGroup(new PdfPushGroupStep { Opacity = alpha }, output);
                if (alpha < 1 && !faded)
                {
                    // No group to be seen through as one: at least each triangle is, its corners carrying the opacity.
                    triangles = Faded(triangles, alpha);
                }

                Write(new PdfMeshStep(triangles, PdfMatrices.ToDrawing(space)), output);
                if (faded)
                {
                    CloseGroupsDownTo(_openGroups - 1, output);
                }

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
        if (AddStops(brush, shading, alpha) <= StopTolerance)
        {
            Write(new PdfGeometryStep(new RectangleGeometry(new Rect(from, cover.Y, to - from, cover.Height)),
                brush.CaptureBase(), null, PdfMatrices.ToDrawing(axis)), output);
            return;
        }

        // More turns than a brush's stops can follow: the function sampled along the axis into a strip, and the
        // extensions in its end colors.
        const int samples = FunctionSamples;
        var rgba = new byte[samples * 4];
        for (var i = 0; i < samples; i++)
        {
            var color = ColorAt(shading, (i + 0.5) / samples);
            rgba[i * 4] = Byte(color.R);
            rgba[i * 4 + 1] = Byte(color.G);
            rgba[i * 4 + 2] = Byte(color.B);
            rgba[i * 4 + 3] = Byte(alpha);
        }

        var band = new PdfMatrix(1, 0, 0, cover.Height, 0, cover.Y).Then(axis);
        Write(new PdfImageStep(ToImage(samples, 1, rgba), PdfMatrices.ToDrawing(band)), output);
        if (from < 0)
        {
            Write(new PdfGeometryStep(new RectangleGeometry(new Rect(from, cover.Y, -from, cover.Height)),
                Solid(ColorAt(shading, 0), alpha), null, PdfMatrices.ToDrawing(axis)), output);
        }

        if (to > 1)
        {
            Write(new PdfGeometryStep(new RectangleGeometry(new Rect(1, cover.Y, to - 1, cover.Height)),
                Solid(ColorAt(shading, 1), alpha), null, PdfMatrices.ToDrawing(axis)), output);
        }
    }

    // A radial shading the brush draws as it is: its start circle a point - the focus - inside the end circle.
    private void DrawRadial(PdfRadialShading shading, PdfMatrix space, double alpha, List<PdfDrawStep> output)
    {
        var (x1, y1, r1) = (shading.EndCenter.X, shading.EndCenter.Y, shading.EndRadius);
        var (fx, fy) = (shading.StartCenter.X - x1, shading.StartCenter.Y - y1);
        if (shading.StartRadius > 1e-9 || r1 <= 1e-12 || fx * fx + fy * fy > r1 * r1)
        {
            DrawTwoCircles(shading, space, alpha, output);
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
        TellIfVisiblyNear(AddStops(brush, shading, alpha));
        Geometry geometry = shading.ExtendEnd
            ? new RectangleGeometry(area)
            : new EllipseGeometry(new Rect(x1 - r1, y1 - r1, 2 * r1, 2 * r1));
        Write(new PdfGeometryStep(geometry, brush.CaptureBase(), null, PdfMatrices.ToDrawing(space)), output);
    }

    // Any other radial shading is the brush between two circles over all a group can let through - where the brush's rule
    // is the shading's. The brush takes the largest s whose circle passes through a point; the shading the largest it
    // allows, so the two part where a point lies on two circles and the larger s is one the shading does not extend to.
    // One circle inside the other puts every point on one circle only; an end that extends allows every larger s. The
    // brush's end circle must have a radius, so one that shrinks to a point - only inside the other - is drawn the other
    // way round, its colors reversed. Where the shading does not extend, its first or last stop is clear: past t's ends
    // nothing is painted. Anything else is worked out point by point into a picture.
    private void DrawTwoCircles(PdfRadialShading shading, PdfMatrix space, double alpha, List<PdfDrawStep> output)
    {
        var (start, startRadius, end, endRadius) =
            (shading.StartCenter, shading.StartRadius, shading.EndCenter, shading.EndRadius);
        var (extendStart, extendEnd) = (shading.ExtendStart, shading.ExtendEnd);
        var (cx, cy, dr) = (end.X - start.X, end.Y - start.Y, endRadius - startRadius);
        var nested = cx * cx + cy * cy < dr * dr;
        var reversed = endRadius <= 1e-9;
        if (!nested && (reversed || !extendEnd))
        {
            RasterizeTwoCircles(shading, space, alpha, output);
            return;
        }

        if (reversed)
        {
            (start, startRadius, end, endRadius) = (end, endRadius, start, startRadius);
            (extendStart, extendEnd) = (extendEnd, extendStart);
        }

        if (endRadius <= 1e-9)
        {
            return;
        }

        var area = Cover(space);
        var brush = new RadialGradientBrush
        {
            Center = Relative(area, end.X, end.Y),
            GradientOrigin = Relative(area, start.X, start.Y),
            RadiusX = endRadius / area.Width,
            RadiusY = endRadius / area.Height,
            GradientOriginRadius = Math.Max(startRadius / endRadius, 1e-9),
            SpreadMethod = GradientSpreadMethod.Pad,
        };
        TellIfVisiblyNear(AddStops(brush, shading, alpha, reversed, extendStart, extendEnd));
        Write(new PdfGeometryStep(new RectangleGeometry(area), brush.CaptureBase(), null, PdfMatrices.ToDrawing(space)),
            output);
    }

    // The shading worked out at each pixel of a picture over what it can paint: the largest s it allows whose circle
    // passes through the pixel's middle, its color from a table along s.
    private void RasterizeTwoCircles(PdfRadialShading shading, PdfMatrix space, double alpha, List<PdfDrawStep> output)
    {
        var (x0, y0, r0) = (shading.StartCenter.X, shading.StartCenter.Y, shading.StartRadius);
        var (x1, y1, r1) = (shading.EndCenter.X, shading.EndCenter.Y, shading.EndRadius);
        var area = Cover(space);
        if (!shading.ExtendStart && !shading.ExtendEnd)
        {
            var circles = Union(new Rect(x0 - r0, y0 - r0, 2 * r0, 2 * r0), new Rect(x1 - r1, y1 - r1, 2 * r1, 2 * r1));
            area = Intersection(area, circles);
        }

        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        var picture = new PdfMatrix(area.Width, 0, 0, area.Height, area.X, area.Y).Then(space);
        if (!_rasterized.TryGetValue((shading, area, alpha), out var image))
        {
            image = RasterizeTwoCircles(shading, area, alpha);
            _rasterized[(shading, area, alpha)] = image;
        }

        Write(new PdfImageStep(image, PdfMatrices.ToDrawing(picture)), output);
    }

    private static ImageSource RasterizeTwoCircles(PdfRadialShading shading, Rect area, double alpha)
    {
        var startX = shading.StartCenter.X;
        var startY = shading.StartCenter.Y;
        var startRadius = shading.StartRadius;
        var scale = RadialRasterSide / Math.Max(area.Width, area.Height);
        var width = Math.Max(1, (int)Math.Ceiling(area.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(area.Height * scale));
        // Red, green and blue along s, three bytes a sample.
        var table = new byte[(FunctionSamples + 1) * 3];
        for (var sample = 0; sample <= FunctionSamples; sample++)
        {
            var color = ColorAt(shading, (double)sample / FunctionSamples);
            table[sample * 3] = Byte(color.R);
            table[sample * 3 + 1] = Byte(color.G);
            table[sample * 3 + 2] = Byte(color.B);
        }

        var travelX = shading.EndCenter.X - startX;
        var travelY = shading.EndCenter.Y - startY;
        var growth = shading.EndRadius - startRadius;
        var leading = travelX * travelX + travelY * travelY - growth * growth;
        var size = travelX * travelX + travelY * travelY + growth * growth;
        var opacity = Byte(alpha);
        var texelWidth = area.Width / width;
        var texelHeight = area.Height / height;

        // The s at a point of the area, or null where the shading paints nothing.
        double? At(double pageX, double pageY)
        {
            var offsetX = pageX - startX;
            var offsetY = pageY - startY;
            return RadialParameter(shading, leading, size, offsetX * travelX + offsetY * travelY + startRadius * growth,
                offsetX * offsetX + offsetY * offsetY - startRadius * startRadius, startRadius, growth);
        }

        void Paint(byte[] into, int texel, double along, byte cover)
        {
            var sample = (int)Math.Round(Math.Clamp(along, 0, 1) * FunctionSamples) * 3;
            into[texel * 4] = table[sample];
            into[texel * 4 + 1] = table[sample + 1];
            into[texel * 4 + 2] = table[sample + 2];
            into[texel * 4 + 3] = cover;
        }

        var rgba = new byte[width * height * 4];
        var painted = new bool[width * height];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (At(area.X + (column + 0.5) * texelWidth, area.Y + (row + 0.5) * texelHeight) is { } along)
                {
                    painted[row * width + column] = true;
                    Paint(rgba, row * width + column, along, opacity);
                }
            }
        }

        // Where the shading's edge crosses a texel - its neighbors differ from it - the texel is covered by how much of
        // it the shading paints, so the edge is smooth instead of a step per texel.
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var texel = row * width + column;
                var own = painted[texel];
                if ((column == 0 || painted[texel - 1] == own) && (column == width - 1 || painted[texel + 1] == own)
                    && (row == 0 || painted[texel - width] == own) && (row == height - 1 || painted[texel + width] == own))
                {
                    continue;
                }

                var hits = 0;
                double? first = null;
                for (var down = 0; down < EdgeSamples; down++)
                {
                    for (var across = 0; across < EdgeSamples; across++)
                    {
                        if (At(area.X + (column + (across + 0.5) / EdgeSamples) * texelWidth,
                                area.Y + (row + (down + 0.5) / EdgeSamples) * texelHeight) is { } along)
                        {
                            hits++;
                            first ??= along;
                        }
                    }
                }

                var cover = (byte)Math.Round(opacity * (double)hits / (EdgeSamples * EdgeSamples));
                if (own)
                {
                    rgba[texel * 4 + 3] = cover;
                }
                else if (first is { } edge)
                {
                    Paint(rgba, texel, edge, cover);
                }
            }
        }

        BleedUnderClear(rgba, width, height);
        return ToImage(width, height, rgba);
    }

    // A texel the shading's edge crosses is sampled this many times across and down.
    private const int EdgeSamples = 4;

    // Gives every clear pixel the color of the nearest painted one along its row, then its column: a smooth sampler
    // blends a painted pixel with its clear neighbors, and their black would rim the shading's edge dark.
    private static void BleedUnderClear(byte[] rgba, int width, int height)
    {
        var colored = new bool[width * height];
        for (var i = 0; i < colored.Length; i++)
        {
            colored[i] = rgba[i * 4 + 3] != 0;
        }

        for (var row = 0; row < height; row++)
        {
            Bleed(rgba, colored, row * width, 1, width);
        }

        for (var column = 0; column < width; column++)
        {
            Bleed(rgba, colored, column, width, height);
        }
    }

    // Along one line of `count` pixels from `first`, `step` apart: each uncolored pixel takes the color last seen going
    // forward, and those before the first colored one the first one's.
    private static void Bleed(byte[] rgba, bool[] colored, int first, int step, int count)
    {
        var seen = -1;
        for (var k = 0; k < count; k++)
        {
            var i = first + k * step;
            if (colored[i])
            {
                if (seen < 0)
                {
                    for (var j = 0; j < k; j++)
                    {
                        CopyColor(rgba, colored, i, first + j * step);
                    }
                }

                seen = i;
            }
            else if (seen >= 0)
            {
                CopyColor(rgba, colored, seen, i);
            }
        }
    }

    private static void CopyColor(byte[] rgba, bool[] colored, int from, int to)
    {
        (rgba[to * 4], rgba[to * 4 + 1], rgba[to * 4 + 2]) = (rgba[from * 4], rgba[from * 4 + 1], rgba[from * 4 + 2]);
        colored[to] = true;
    }

    // The largest s the shading allows - [0, 1], and past an end it extends - at which the circle of radius r0 + s dr,
    // at least 0, passes through the point: the roots of a s^2 - 2 b s + c = 0. Null where there is none. `size` is
    // the circles' own scale (|centers|² + |radii|²), what "a is next to nothing" is measured against.
    private static double? RadialParameter(PdfRadialShading shading, double a, double size, double b, double c,
        double r0, double dr)
    {
        bool Allowed(double s) => r0 + s * dr >= 0 && (s >= 0 || shading.ExtendStart) && (s <= 1 || shading.ExtendEnd);

        if (Math.Abs(a) <= 1e-12 * size)
        {
            if (b == 0)
            {
                return null;
            }

            var only = c / (2 * b);
            return Allowed(only) ? only : null;
        }

        var disc = b * b - a * c;
        if (disc < 0)
        {
            return null;
        }

        // q / a and c / q: neither root is a difference of near-equal numbers.
        var q = b + Math.CopySign(Math.Sqrt(disc), b);
        var (t1, t2) = Math.Abs(q) < 1e-300 ? (0.0, 0.0) : (q / a, c / q);
        var (high, low) = (Math.Max(t1, t2), Math.Min(t1, t2));
        if (Allowed(high))
        {
            return high;
        }

        return Allowed(low) ? low : null;
    }

    private static ColoredMesh Faded(ColoredMesh mesh, double alpha)
    {
        var vertices = new ColoredVertex[mesh.Vertices.Count];
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = mesh.Vertices[i];
            vertices[i] = v with { Color = new Color(v.Color.R, v.Color.G, v.Color.B, Byte(alpha)) };
        }

        return new ColoredMesh(vertices);
    }

    private static Rect Intersection(Rect a, Rect b)
    {
        var (left, top) = (Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        var (right, bottom) = (Math.Min(a.Right, b.Right), Math.Min(a.Bottom, b.Bottom));
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : Rect.Empty;
    }

    // The function sampled over its domain into a picture, once for the shading however often it is painted: a smooth
    // function keeps its look at any zoom. An opacity below 1 is the group's around it.
    private void DrawFunction(PdfFunctionShading shading, PdfMatrix space, double alpha, List<PdfDrawStep> output)
    {
        var (x0, x1, y0, y1) = (shading.Domain[0], shading.Domain[1], shading.Domain[2], shading.Domain[3]);
        if (!_sampled.TryGetValue(shading, out var image))
        {
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
                    rgba[at + 3] = 255;
                }
            }

            image = ToImage(side, side, rgba);
            _sampled[shading] = image;
        }

        var faded = alpha < 1 && OpenGroup(new PdfPushGroupStep { Opacity = alpha }, output);
        var domain = new PdfMatrix(x1 - x0, 0, 0, y1 - y0, x0, y0).Then(shading.Matrix).Then(space);
        Write(new PdfImageStep(image, PdfMatrices.ToDrawing(domain)), output);
        if (faded)
        {
            CloseGroupsDownTo(_openGroups - 1, output);
        }
    }

    // The stops a brush can carry, set where the function strays most from a straight run between them until it strays
    // by no more than StopTolerance; the answer is how far it still strays, in sRGB. Reversed, the brush's offset u is
    // the shading's 1 - u. A side that does not extend spends a stop on a clear end, the brush padding with it past that
    // end.
    private double AddStops(GradientBrush brush, PdfGradientShading shading, double alpha, bool reversed = false,
        bool extendStart = true, bool extendEnd = true)
    {
        const double edge = 1e-4;
        var (from, to) = (extendStart ? 0 : edge, extendEnd ? 1 : 1 - edge);
        var budget = GradientBrush.MaxRenderedStops - (extendStart ? 0 : 1) - (extendEnd ? 0 : 1);
        (double R, double G, double B) At(double u) => ColorAt(shading, reversed ? 1 - u : u);
        // The function at FunctionSamples + 1 points: a stop goes where a straight run between the stops strays most from
        // them, and the stops fit when it strays from none by more than StopTolerance.
        var samples = new (double At, (double R, double G, double B) Color)[FunctionSamples + 1];
        for (var i = 0; i < samples.Length; i++)
        {
            var u = from + (to - from) * i / FunctionSamples;
            samples[i] = (u, At(u));
        }

        var chosen = new List<int> { 0, FunctionSamples };
        double strays;
        while (true)
        {
            var (worst, worstSample) = (0.0, -1);
            for (var s = 0; s + 1 < chosen.Count; s++)
            {
                var (a, b) = (samples[chosen[s]], samples[chosen[s + 1]]);
                for (var i = chosen[s] + 1; i < chosen[s + 1]; i++)
                {
                    var k = (samples[i].At - a.At) / (b.At - a.At);
                    var straight = (a.Color.R + (b.Color.R - a.Color.R) * k, a.Color.G + (b.Color.G - a.Color.G) * k,
                        a.Color.B + (b.Color.B - a.Color.B) * k);
                    var off = Distance(samples[i].Color, straight);
                    if (off > worst)
                    {
                        (worst, worstSample) = (off, i);
                    }
                }
            }

            strays = worst;
            if (worst <= StopTolerance || chosen.Count >= budget)
            {
                break;
            }

            var at = chosen.BinarySearch(worstSample);
            chosen.Insert(~at, worstSample);
        }

        var stops = new List<(double At, (double R, double G, double B) Color)>(chosen.Count);
        foreach (var index in chosen)
        {
            stops.Add(samples[index]);
        }

        if (!extendStart)
        {
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        }

        foreach (var (at, color) in stops)
        {
            brush.GradientStops.Add(new GradientStop(ColorOf(Pack(color, alpha)), at));
        }

        if (!extendEnd)
        {
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        }

        return strays;
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

    // One tile is a step's worth of the cell from its box's corner. The brush's viewport is a fraction of the area it
    // fills - so the tiles scale with the page, as pattern space does - counted from the area's corner; its viewbox is
    // counted from the corner of the cell's picture. The tiles are shifted to stand on the cell's corner in pattern
    // space, wherever the area begins.
    private static Brush TileBrush(PdfTilingPattern tiling, PdfContentDrawing cell, double alpha, Rect area)
    {
        var (width, height) = (Math.Abs(tiling.XStep), Math.Abs(tiling.YStep));
        return new DrawingBrush(cell)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            Viewbox = new Rect(0, 0, width, height),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect((cell.Bounds.X - area.X) / area.Width, (cell.Bounds.Y - area.Y) / area.Height,
                width / area.Width, height / area.Height),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Opacity = alpha,
        }.CaptureBase();
    }

    // The area of the stream being walked - the page, or a cell - as seen in a space placed by `space`: all a group cut
    // to it can let through. Empty for a space that flattens everything.
    private Rect Cover(PdfMatrix space)
    {
        if (space.Inverse() is not { } inverse)
        {
            return Rect.Empty;
        }

        var area = Area;
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var (x, y) in new[]
                 {
                     (area.X, area.Y), (area.Right, area.Y), (area.Right, area.Bottom), (area.X, area.Bottom),
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
