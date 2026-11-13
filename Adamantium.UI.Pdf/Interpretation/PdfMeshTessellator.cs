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
/// folds over itself the larger v lies on top. The mesh is cut to its outline - the sum of its pieces' boundaries, the
/// patches' sides as their curves and the triangles' as lines - so its edge is exact and smooth at any zoom; along that
/// outline a border under the triangles reaches past it for it to cut back. Every mesh keeps under
/// <see cref="MaxTriangles"/>: past that it is cut coarser, and what still does not fit is left out.</summary>
internal static class PdfMeshTessellator
{
    public const int MaxTriangles = 1_000_000;

    private const double PieceLength = 2.0;
    private const int MaxCuts = 32;
    private const int TriangleBorderTriangles = 6;
    private const int PatchBorderTrianglesPerCut = 8;
    private const int PatchCornerTriangles = 8;

    private static readonly int[] PatchSides = [0, 4, 8, 12, 13, 14, 15, 11, 7, 3, 2, 1];

    /// <summary>The mesh as the page shows it at the scale <paramref name="space"/> puts it; <paramref name="cut"/> is
    /// whether pieces were left out to keep it under <see cref="MaxTriangles"/>.</summary>
    public static PdfMesh Tessellate(PdfMeshShading shading, PdfMatrix space, out bool cut)
    {
        var scale = PdfMatrices.Scale(space);
        var straight = shading.Function == null &&
                       shading.ColorSpace is PdfDeviceGrayColorSpace or PdfDeviceRgbColorSpace;
        var budget = MaxTriangles / Math.Max(1, shading.Triangles.Count + shading.Patches.Count);
        var triangleCuts = new List<int>();
        var patchCuts = new List<int>();
        var everyPiece = CountNetSides(shading, shading.Triangles.Count, shading.Patches.Count);
        cut = DecideCuts(shading, everyPiece, scale, straight, budget, triangleCuts, patchCuts);
        var net = cut ? CountNetSides(shading, triangleCuts.Count, patchCuts.Count) : everyPiece;

        var body = new List<ColoredVertex>();
        var border = new List<ColoredVertex>();
        var reach = PieceLength / Math.Max(scale, 1e-9);
        Span<bool> outer = stackalloc bool[4];
        for (var index = 0; index < triangleCuts.Count; index++)
        {
            var triangle = shading.Triangles[index];
            if (IsFlat(triangle))
            {
                continue;
            }

            outer[0] = IsOuter(net, PdfMeshSide.Line(triangle.A.Position, triangle.B.Position));
            outer[1] = IsOuter(net, PdfMeshSide.Line(triangle.B.Position, triangle.C.Position));
            outer[2] = IsOuter(net, PdfMeshSide.Line(triangle.C.Position, triangle.A.Position));
            AddTriangle(body, shading, triangle, triangleCuts[index]);
            AddTriangleBorder(border, shading, triangle, outer[..3], reach);
        }

        for (var index = 0; index < patchCuts.Count; index++)
        {
            var patch = shading.Patches[index];
            for (var side = 0; side < 4; side++)
            {
                outer[side] = IsOuter(net, GetPatchSide(patch, side));
            }

            AddPatch(body, border, shading, patch, patchCuts[index], outer);
        }

        var outline = new StreamGeometry { FillRule = FillRule.NonZero };
        var figures = outline.Open();
        if (!AddLoops(figures, CollectOuterPieces(net)))
        {
            AddFigures(figures, shading, triangleCuts.Count, patchCuts.Count);
        }

        outline.RecalculateBounds();
        var borderVertexCount = border.Count;
        border.AddRange(body);
        return new PdfMesh(new ColoredMesh(border, outline), borderVertexCount);
    }

    private static Dictionary<PdfMeshSide, int> CountNetSides(PdfMeshShading shading, int triangles, int patches)
    {
        var net = new Dictionary<PdfMeshSide, int>();
        Span<PdfMeshSide> sides = stackalloc PdfMeshSide[4];
        for (var index = 0; index < triangles; index++)
        {
            GetTriangleSides(shading.Triangles[index], sides);
            AddNetSides(net, sides[..3]);
        }

        for (var index = 0; index < patches; index++)
        {
            GetPatchSides(shading.Patches[index], sides);
            AddNetSides(net, sides);
        }

        return net;
    }

    private static bool DecideCuts(PdfMeshShading shading, Dictionary<PdfMeshSide, int> net, double scale,
        bool straight, int budget, List<int> triangleCuts, List<int> patchCuts)
    {
        var total = 0;
        foreach (var triangle in shading.Triangles)
        {
            var cuts = straight
                ? 1
                : CountCuts(scale, budget, 1, 0, TriangleBorderTriangles, triangle.A.Position, triangle.B.Position,
                    triangle.C.Position);
            var outerSides = CountOuterSides(net, triangle);
            var cost = cuts * cuts + 2 * outerSides;
            if (total + cost > MaxTriangles)
            {
                return true;
            }

            total += cost;
            triangleCuts.Add(cuts);
        }

        foreach (var patch in shading.Patches)
        {
            var cuts = CountCuts(scale, budget, 2, PatchBorderTrianglesPerCut, PatchCornerTriangles, patch.Points);
            var outerSides = CountOuterSides(net, patch);
            var cost = 2 * cuts * cuts + 2 * outerSides * cuts + PatchCornerTriangles;
            if (total + cost > MaxTriangles)
            {
                return true;
            }

            total += cost;
            patchCuts.Add(cuts);
        }

        return false;
    }

    private static int CountOuterSides(Dictionary<PdfMeshSide, int> net, PdfMeshTriangle triangle)
    {
        var outer = 0;
        outer += IsOuter(net, PdfMeshSide.Line(triangle.A.Position, triangle.B.Position)) ? 1 : 0;
        outer += IsOuter(net, PdfMeshSide.Line(triangle.B.Position, triangle.C.Position)) ? 1 : 0;
        outer += IsOuter(net, PdfMeshSide.Line(triangle.C.Position, triangle.A.Position)) ? 1 : 0;
        return outer;
    }

    private static int CountOuterSides(Dictionary<PdfMeshSide, int> net, PdfMeshPatch patch)
    {
        var outer = 0;
        for (var side = 0; side < 4; side++)
        {
            outer += IsOuter(net, GetPatchSide(patch, side)) ? 1 : 0;
        }

        return outer;
    }

    private static int CountCuts(double scale, int budget, int perPiece, int perCut, int fixedCost,
        params PdfPoint[] points)
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
        while (cuts > 1 && cuts * cuts * perPiece + cuts * perCut + fixedCost > budget)
        {
            cuts--;
        }

        return cuts;
    }

    private static double MeasureTurn(PdfPoint cornerA, PdfPoint cornerB, PdfPoint cornerC) =>
        (cornerB.X - cornerA.X) * (cornerC.Y - cornerA.Y) - (cornerB.Y - cornerA.Y) * (cornerC.X - cornerA.X);

    private static bool IsFlat(PdfMeshTriangle triangle) =>
        MeasureTurn(triangle.A.Position, triangle.B.Position, triangle.C.Position) == 0;

    private static void GetTriangleSides(PdfMeshTriangle triangle, Span<PdfMeshSide> sides)
    {
        var cornerA = triangle.A.Position;
        var cornerB = triangle.B.Position;
        var cornerC = triangle.C.Position;
        var turn = MeasureTurn(cornerA, cornerB, cornerC);
        if (turn < 0)
        {
            var swapped = cornerB;
            cornerB = cornerC;
            cornerC = swapped;
        }

        sides[0] = new PdfMeshSide(cornerA, cornerA, cornerB, cornerB);
        sides[1] = new PdfMeshSide(cornerB, cornerB, cornerC, cornerC);
        sides[2] = new PdfMeshSide(cornerC, cornerC, cornerA, cornerA);
    }

    private static void GetPatchSides(PdfMeshPatch patch, Span<PdfMeshSide> sides)
    {
        var count = PatchSides.Length;
        Span<int> order = stackalloc int[count];
        var turn = 0.0;
        for (var index = 0; index < count; index++)
        {
            var here = patch.Points[PatchSides[index]];
            var next = patch.Points[PatchSides[(index + 1) % count]];
            turn += here.X * next.Y - next.X * here.Y;
        }

        for (var index = 0; index < count; index++)
        {
            order[index] = turn >= 0 ? PatchSides[index] : PatchSides[(count - index) % count];
        }

        for (var side = 0; side < 4; side++)
        {
            sides[side] = new PdfMeshSide(patch.Points[order[side * 3]], patch.Points[order[side * 3 + 1]],
                patch.Points[order[side * 3 + 2]], patch.Points[order[(side * 3 + 3) % count]]);
        }
    }

    private static PdfMeshSide GetPatchSide(PdfMeshPatch patch, int side) =>
        PdfMeshSide.Of(patch.Points[PatchSides[side * 3]], patch.Points[PatchSides[side * 3 + 1]],
            patch.Points[PatchSides[side * 3 + 2]], patch.Points[PatchSides[(side * 3 + 3) % PatchSides.Length]]);

    private static void AddNetSides(Dictionary<PdfMeshSide, int> net, ReadOnlySpan<PdfMeshSide> sides)
    {
        foreach (var side in sides)
        {
            if (IsPoint(side))
            {
                continue;
            }

            var key = PdfMeshSide.Of(side.First, side.Second, side.Third, side.Fourth);
            net[key] = net.GetValueOrDefault(key) + (key == side ? 1 : -1);
        }
    }

    private static bool IsPoint(PdfMeshSide side) =>
        side.First == side.Second && side.Second == side.Third && side.Third == side.Fourth;

    private static bool IsOuter(Dictionary<PdfMeshSide, int> net, PdfMeshSide side) =>
        net.GetValueOrDefault(side) != 0;

    private static List<PdfMeshSide> CollectOuterPieces(Dictionary<PdfMeshSide, int> net)
    {
        var pieces = new List<PdfMeshSide>();
        foreach (var entry in net)
        {
            var side = entry.Key;
            var piece = entry.Value > 0 ? side : new PdfMeshSide(side.Fourth, side.Third, side.Second, side.First);
            for (var copy = 0; copy < Math.Abs(entry.Value); copy++)
            {
                pieces.Add(piece);
            }
        }

        return pieces;
    }

    private static bool AddLoops(StreamGeometryContext figures, List<PdfMeshSide> outerPieces)
    {
        var leaving = new Dictionary<PdfPoint, List<int>>();
        for (var index = 0; index < outerPieces.Count; index++)
        {
            var start = outerPieces[index].First;
            if (!leaving.TryGetValue(start, out var from))
            {
                leaving[start] = from = [];
            }

            from.Add(index);
        }

        var used = new bool[outerPieces.Count];
        var loops = new List<List<PdfMeshSide>>();
        for (var first = 0; first < outerPieces.Count; first++)
        {
            if (used[first])
            {
                continue;
            }

            var loop = new List<PdfMeshSide>();
            var current = first;
            while (true)
            {
                used[current] = true;
                loop.Add(outerPieces[current]);
                var end = outerPieces[current].Fourth;
                if (end == outerPieces[first].First)
                {
                    break;
                }

                current = FindUnusedLeaving(leaving, used, end);
                if (current < 0)
                {
                    return false;
                }
            }

            loops.Add(loop);
        }

        foreach (var loop in loops)
        {
            AddFigure(figures, loop);
        }

        return loops.Count > 0;
    }

    private static int FindUnusedLeaving(Dictionary<PdfPoint, List<int>> leaving, bool[] used, PdfPoint at)
    {
        if (!leaving.TryGetValue(at, out var from))
        {
            return -1;
        }

        foreach (var index in from)
        {
            if (!used[index])
            {
                return index;
            }
        }

        return -1;
    }

    private static void AddFigures(StreamGeometryContext figures, PdfMeshShading shading, int triangles, int patches)
    {
        var sides = new PdfMeshSide[4];
        for (var index = 0; index < triangles; index++)
        {
            GetTriangleSides(shading.Triangles[index], sides);
            AddFigure(figures, sides.AsSpan(0, 3));
        }

        for (var index = 0; index < patches; index++)
        {
            GetPatchSides(shading.Patches[index], sides);
            AddFigure(figures, sides);
        }
    }

    private static void AddFigure(StreamGeometryContext figures, ReadOnlySpan<PdfMeshSide> pieces)
    {
        var segments = figures.BeginFigure(Point(pieces[0].First), true, true);
        foreach (var piece in pieces)
        {
            if (piece.First == piece.Second && piece.Third == piece.Fourth)
            {
                segments.LineTo(Point(piece.Fourth));
            }
            else
            {
                segments.CubicBezierTo(Point(piece.Second), Point(piece.Third), Point(piece.Fourth));
            }
        }

        segments.CloseFigure();
    }

    private static void AddFigure(StreamGeometryContext figures, List<PdfMeshSide> pieces) =>
        AddFigure(figures, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pieces));

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

    private static void AddTriangleBorder(List<ColoredVertex> border, PdfShading shading, PdfMeshTriangle triangle,
        ReadOnlySpan<bool> outer, double reach)
    {
        var cornerA = Between(shading, triangle, 1, 0, 0);
        var cornerB = Between(shading, triangle, 0, 1, 0);
        var cornerC = Between(shading, triangle, 0, 0, 1);
        var middle = new Vector2<double>((cornerA.Position.X + cornerB.Position.X + cornerC.Position.X) / 3,
            (cornerA.Position.Y + cornerB.Position.Y + cornerC.Position.Y) / 3);
        if (outer[0])
        {
            AddSideBorder(border, cornerA, cornerB, middle, reach);
        }

        if (outer[1])
        {
            AddSideBorder(border, cornerB, cornerC, middle, reach);
        }

        if (outer[2])
        {
            AddSideBorder(border, cornerC, cornerA, middle, reach);
        }
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

    private static void AddPatch(List<ColoredVertex> body, List<ColoredVertex> border, PdfShading shading,
        PdfMeshPatch patch, int cuts, ReadOnlySpan<bool> outer)
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

        for (var stepV = -1; stepV <= cuts; stepV++)
        {
            for (var stepU = -1; stepU <= cuts; stepU++)
            {
                List<ColoredVertex> into;
                var beforeU = stepU < 0;
                var pastU = stepU >= cuts;
                var beforeV = stepV < 0;
                var pastV = stepV >= cuts;
                if (!beforeU && !pastU && !beforeV && !pastV)
                {
                    into = body;
                }
                else if (IsBorderCell(outer, beforeU, pastU, beforeV, pastV))
                {
                    into = border;
                }
                else
                {
                    continue;
                }

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

    private static bool IsBorderCell(ReadOnlySpan<bool> outer, bool beforeU, bool pastU, bool beforeV, bool pastV)
    {
        var alongU = !beforeU && !pastU;
        var alongV = !beforeV && !pastV;
        if (beforeV && alongU)
        {
            return outer[0];
        }

        if (pastU && alongV)
        {
            return outer[1];
        }

        if (pastV && alongU)
        {
            return outer[2];
        }

        if (beforeU && alongV)
        {
            return outer[3];
        }

        if (beforeV)
        {
            return beforeU ? outer[0] && outer[3] : outer[0] && outer[1];
        }

        return pastU ? outer[1] && outer[2] : outer[2] && outer[3];
    }

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
