using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Shadings;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>A mesh shading as triangles with an opaque color at each corner (ISO 32000-2, 8.7.4.5.5 to 8.7.4.5.8). A
/// triangle is cut finer unless its colors are DeviceGray or DeviceRGB values taken straight, so that the color between
/// its corners is the shading's, not a straight run in sRGB. A patch is its tensor surface sampled on a grid, as fine as
/// the patch is large on the page, its color bilinear in the patch's parameters, and drawn row by row of v, so where it
/// folds over itself the larger v lies on top. The mesh is cut to its outline - the patches' sides as their curves, the
/// triangles' as lines - so its edge is exact and smooth at any zoom; under the triangles lies a border reaching past
/// that outline, which the outline cuts back. Every mesh keeps under <see cref="MaxTriangles"/>: past that it is cut
/// coarser, and what still does not fit is left out.</summary>
internal static class PdfMeshTessellator
{
    public const int MaxTriangles = 1_000_000;

    // How long a piece of a patch or a cut triangle may be, in the page's points; and how finely one may be cut at most.
    private const double PieceLength = 2.0;
    private const int MaxCuts = 32;

    /// <summary>The mesh as the page shows it at the scale <paramref name="space"/> puts it; <paramref name="cut"/> is
    /// whether pieces were left out to keep it under <see cref="MaxTriangles"/>.</summary>
    public static ColoredMesh Tessellate(PdfMeshShading shading, PdfMatrix space, out bool cut)
    {
        var scale = PdfMatrices.Scale(space);
        var body = new List<ColoredVertex>();
        var border = new List<ColoredVertex>();
        var outline = new StreamGeometry { FillRule = FillRule.NonZero };
        var figures = outline.Open();
        var straight = shading.Function == null &&
                       shading.ColorSpace is PdfDeviceGrayColorSpace or PdfDeviceRgbColorSpace;
        var budget = MaxTriangles / Math.Max(1, shading.Triangles.Count + shading.Patches.Count);
        // How far the border reaches past a triangle's side, in the shading's units: a piece's length on the page.
        var reach = PieceLength / Math.Max(scale, 1e-9);
        cut = false;
        foreach (var triangle in shading.Triangles)
        {
            var cuts = straight ? 1 : Cuts(scale, budget, 1, triangle.A.Position, triangle.B.Position, triangle.C.Position);
            if ((body.Count + border.Count) / 3 + cuts * cuts + 6 > MaxTriangles)
            {
                cut = true;
                break;
            }

            AddTriangle(body, shading, triangle, cuts);
            AddTriangleBorder(border, shading, triangle, reach);
            AddTriangleOutline(figures, triangle);
        }

        foreach (var patch in shading.Patches)
        {
            var cuts = Cuts(scale, budget, 2, patch.Points);
            if ((body.Count + border.Count) / 3 + 2 * (cuts + 2) * (cuts + 2) > MaxTriangles)
            {
                cut = true;
                break;
            }

            AddPatch(body, border, shading, patch, cuts);
            AddPatchOutline(figures, patch);
        }

        outline.RecalculateBounds();
        border.AddRange(body);   // the border first: every triangle of the mesh lies over it
        return new ColoredMesh(border, outline);
    }

    // As many cuts along a side as keep each piece under PieceLength on the page, within the triangles this one may
    // have: a piece of a patch is two of them.
    private static int Cuts(double scale, int budget, int perPiece, params PdfPoint[] points)
    {
        double left = double.MaxValue, bottom = double.MaxValue, right = double.MinValue, top = double.MinValue;
        foreach (var point in points)
        {
            left = Math.Min(left, point.X);
            right = Math.Max(right, point.X);
            bottom = Math.Min(bottom, point.Y);
            top = Math.Max(top, point.Y);
        }

        var size = Math.Max(right - left, top - bottom) * scale;
        var cuts = Math.Clamp((int)Math.Ceiling(size / PieceLength), 1, MaxCuts);
        while (cuts > 1 && cuts * cuts * perPiece > budget)
        {
            cuts--;
        }

        return cuts;
    }

    // The triangle cut into cuts² smaller ones, the values at each corner taken between the big one's by barycentric
    // weights, then turned into color.
    private static void AddTriangle(List<ColoredVertex> vertices, PdfShading shading, PdfMeshTriangle triangle, int cuts)
    {
        ColoredVertex At(int towardB, int towardC)
        {
            var weightB = (double)towardB / cuts;
            var weightC = (double)towardC / cuts;
            return Between(shading, triangle, 1 - weightB - weightC, weightB, weightC);
        }

        for (var towardC = 0; towardC < cuts; towardC++)
        {
            for (var towardB = 0; towardB + towardC < cuts; towardB++)
            {
                vertices.Add(At(towardB, towardC));
                vertices.Add(At(towardB + 1, towardC));
                vertices.Add(At(towardB, towardC + 1));
                if (towardB + towardC + 1 < cuts)
                {
                    vertices.Add(At(towardB + 1, towardC));
                    vertices.Add(At(towardB + 1, towardC + 1));
                    vertices.Add(At(towardB, towardC + 1));
                }
            }
        }
    }

    // The point of the triangle at barycentric weights, its values taken between the corners'.
    private static ColoredVertex Between(PdfShading shading, PdfMeshTriangle triangle, double weightA, double weightB,
        double weightC)
    {
        var position = new Vector2<double>(
            triangle.A.Position.X * weightA + triangle.B.Position.X * weightB + triangle.C.Position.X * weightC,
            triangle.A.Position.Y * weightA + triangle.B.Position.Y * weightB + triangle.C.Position.Y * weightC);
        var values = new double[triangle.A.Values.Length];
        for (var component = 0; component < values.Length; component++)
        {
            values[component] = triangle.A.Values[component] * weightA + Value(triangle.B.Values, component) * weightB +
                                Value(triangle.C.Values, component) * weightC;
        }

        return new ColoredVertex(position, ColorOf(shading, values));
    }

    // A strip along each side, reaching `reach` out from the triangle, in the colors of the side's ends.
    private static void AddTriangleBorder(List<ColoredVertex> border, PdfShading shading, PdfMeshTriangle triangle,
        double reach)
    {
        var cornerA = Between(shading, triangle, 1, 0, 0);
        var cornerB = Between(shading, triangle, 0, 1, 0);
        var cornerC = Between(shading, triangle, 0, 0, 1);
        var middle = new Vector2<double>((cornerA.Position.X + cornerB.Position.X + cornerC.Position.X) / 3,
            (cornerA.Position.Y + cornerB.Position.Y + cornerC.Position.Y) / 3);
        AddSideBorder(border, cornerA, cornerB, middle, reach);
        AddSideBorder(border, cornerB, cornerC, middle, reach);
        AddSideBorder(border, cornerC, cornerA, middle, reach);
    }

    private static void AddSideBorder(List<ColoredVertex> border, ColoredVertex start, ColoredVertex end,
        Vector2<double> middle, double reach)
    {
        var alongX = end.Position.X - start.Position.X;
        var alongY = end.Position.Y - start.Position.Y;
        var length = Math.Sqrt(alongX * alongX + alongY * alongY);
        if (length < 1e-12)
        {
            return;
        }

        // The side's normal, turned away from the triangle's middle.
        var normalX = -alongY / length;
        var normalY = alongX / length;
        if ((start.Position.X - middle.X) * normalX + (start.Position.Y - middle.Y) * normalY < 0)
        {
            normalX = -normalX;
            normalY = -normalY;
        }

        var startOut = new ColoredVertex(
            new Vector2<double>(start.Position.X + normalX * reach, start.Position.Y + normalY * reach), start.Color);
        var endOut = new ColoredVertex(
            new Vector2<double>(end.Position.X + normalX * reach, end.Position.Y + normalY * reach), end.Color);
        border.Add(start);
        border.Add(end);
        border.Add(endOut);
        border.Add(start);
        border.Add(endOut);
        border.Add(startOut);
    }

    private static void AddTriangleOutline(StreamGeometryContext figures, PdfMeshTriangle triangle)
    {
        var cornerA = Point(triangle.A.Position);
        var cornerB = Point(triangle.B.Position);
        var cornerC = Point(triangle.C.Position);
        // Every figure turns the same way, so where two overlap they add up instead of cancelling.
        var turn = (cornerB.X - cornerA.X) * (cornerC.Y - cornerA.Y) - (cornerB.Y - cornerA.Y) * (cornerC.X - cornerA.X);
        if (turn < 0)
        {
            var swapped = cornerB;
            cornerB = cornerC;
            cornerC = swapped;
        }

        figures.BeginFigure(cornerA, true, true).LineTo(cornerB).LineTo(cornerC).CloseFigure();
    }

    // S(u, v) = sum of Pij Bi(u) Bj(v) (ISO 32000-2, 8.7.4.5.8), with the corner colors c00, c03, c33, c30 at
    // (u, v) = (0, 0), (0, 1), (1, 1), (1, 0). The grid runs a cell past each side: those cells, the surface carried on
    // and the color held at the side's, are the border.
    private static void AddPatch(List<ColoredVertex> body, List<ColoredVertex> border, PdfShading shading,
        PdfMeshPatch patch, int cuts)
    {
        var side = cuts + 3;
        var grid = new ColoredVertex[side * side];
        Span<double> weightsU = stackalloc double[4];
        Span<double> weightsV = stackalloc double[4];
        for (var stepU = -1; stepU <= cuts + 1; stepU++)
        {
            var alongU = (double)stepU / cuts;
            Bernstein(alongU, weightsU);
            for (var stepV = -1; stepV <= cuts + 1; stepV++)
            {
                var alongV = (double)stepV / cuts;
                Bernstein(alongV, weightsV);
                double placeX = 0, placeY = 0;
                for (var row = 0; row < 4; row++)
                {
                    for (var column = 0; column < 4; column++)
                    {
                        var weight = weightsU[row] * weightsV[column];
                        placeX += patch[row, column].X * weight;
                        placeY += patch[row, column].Y * weight;
                    }
                }

                var colorU = Math.Clamp(alongU, 0, 1);
                var colorV = Math.Clamp(alongV, 0, 1);
                var values = new double[patch.Corners[0].Length];
                for (var component = 0; component < values.Length; component++)
                {
                    values[component] = Value(patch.Corners[0], component) * (1 - colorU) * (1 - colorV) +
                                        Value(patch.Corners[1], component) * (1 - colorU) * colorV +
                                        Value(patch.Corners[2], component) * colorU * colorV +
                                        Value(patch.Corners[3], component) * colorU * (1 - colorV);
                }

                grid[(stepU + 1) * side + stepV + 1] =
                    new ColoredVertex(new Vector2<double>(placeX, placeY), ColorOf(shading, values));
            }
        }

        // Rows of v outermost and u within: the triangles drawn later lie on top, as the larger v and then u must.
        for (var stepV = -1; stepV <= cuts; stepV++)
        {
            for (var stepU = -1; stepU <= cuts; stepU++)
            {
                var into = stepU < 0 || stepU >= cuts || stepV < 0 || stepV >= cuts ? border : body;
                var first = grid[(stepU + 1) * side + stepV + 1];
                var nextU = grid[(stepU + 2) * side + stepV + 1];
                var both = grid[(stepU + 2) * side + stepV + 2];
                var nextV = grid[(stepU + 1) * side + stepV + 2];
                into.Add(first);
                into.Add(nextU);
                into.Add(both);
                into.Add(first);
                into.Add(both);
                into.Add(nextV);
            }
        }
    }

    // The patch's four sides as the cubic curves they are: v = 0, u = 1, v = 1 and u = 0, turned the way every figure
    // of the outline turns.
    private static void AddPatchOutline(StreamGeometryContext figures, PdfMeshPatch patch)
    {
        Span<int> order = stackalloc int[12];
        var around = PatchSides;
        var turn = 0.0;
        for (var index = 0; index < around.Length; index++)
        {
            var here = patch.Points[around[index]];
            var next = patch.Points[around[(index + 1) % around.Length]];
            turn += here.X * next.Y - next.X * here.Y;
        }

        for (var index = 0; index < around.Length; index++)
        {
            order[index] = turn >= 0 ? around[index] : around[(around.Length - index) % around.Length];
        }

        var segments = figures.BeginFigure(Point(patch.Points[order[0]]), true, true);
        for (var sideStart = 0; sideStart < 12; sideStart += 3)
        {
            segments.CubicBezierTo(Point(patch.Points[order[sideStart + 1]]), Point(patch.Points[order[sideStart + 2]]),
                Point(patch.Points[order[(sideStart + 3) % 12]]));
        }

        segments.CloseFigure();
    }

    // A patch's control points round its edge, three a side: P00 P10 P20 P30 P31 P32 P33 P23 P13 P03 P02 P01.
    private static readonly int[] PatchSides = [0, 4, 8, 12, 13, 14, 15, 11, 7, 3, 2, 1];

    private static Vector2<double> Point(PdfPoint point) => new(point.X, point.Y);

    private static void Bernstein(double along, Span<double> weights)
    {
        var rest = 1 - along;
        weights[0] = rest * rest * rest;
        weights[1] = 3 * along * rest * rest;
        weights[2] = 3 * along * along * rest;
        weights[3] = along * along * along;
    }

    private static double Value(double[] values, int component) => component < values.Length ? values[component] : 0;

    private static Color ColorOf(PdfShading shading, double[] values)
    {
        Span<double> rgb = stackalloc double[3];
        shading.ToRgb(values, rgb);
        return new Color(Byte(rgb[0]), Byte(rgb[1]), Byte(rgb[2]));
    }

    private static byte Byte(double value) =>
        (byte)Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1) * 255);
}
