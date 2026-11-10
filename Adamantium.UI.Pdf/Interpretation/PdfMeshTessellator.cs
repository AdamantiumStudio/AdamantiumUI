using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Shadings;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>A mesh shading as triangles with a color at each corner (ISO 32000-2, 8.7.4.5.5 to 8.7.4.5.8). A triangle is
/// cut finer where its colors come through a function or a space that is not sRGB-like, so that the color between its
/// corners is the shading's, not a straight run in sRGB. A patch is its tensor surface sampled on a grid, as fine as
/// the patch is large on the page - with room for the deepest zoom - its color bilinear in the patch's parameters.
/// Every mesh keeps under <see cref="MaxTriangles"/>; past that it is cut coarser.</summary>
internal static class PdfMeshTessellator
{
    public const int MaxTriangles = 1_000_000;

    // How long a piece of a patch or a cut triangle may be, in the page's points; and how finely one may be cut at most.
    private const double PieceLength = 2.0;
    private const int MaxCuts = 32;

    public static ColoredMesh Tessellate(PdfMeshShading shading, PdfMatrix space, double alpha)
    {
        var scale = PdfMatrices.Scale(space);
        var vertices = new List<ColoredVertex>();
        var smoothInSrgb = shading.Function == null && shading.ColorSpace.Components is 1 or 3;
        var budget = MaxTriangles / Math.Max(1, shading.Triangles.Count + shading.Patches.Count);
        foreach (var triangle in shading.Triangles)
        {
            var cuts = smoothInSrgb ? 1 : Cuts(scale, budget, triangle.A.Position, triangle.B.Position, triangle.C.Position);
            AddTriangle(vertices, shading, triangle, cuts, alpha);
        }

        foreach (var patch in shading.Patches)
        {
            AddPatch(vertices, shading, patch, Cuts(scale, budget * 2, patch.Points), alpha);
        }

        return new ColoredMesh(vertices);
    }

    // As many cuts along a side as keep each piece under PieceLength on the page, within the triangles this one may
    // have.
    private static int Cuts(double scale, int budget, params PdfPoint[] points)
    {
        double left = double.MaxValue, bottom = double.MaxValue, right = double.MinValue, top = double.MinValue;
        foreach (var p in points)
        {
            (left, right) = (Math.Min(left, p.X), Math.Max(right, p.X));
            (bottom, top) = (Math.Min(bottom, p.Y), Math.Max(top, p.Y));
        }

        var size = Math.Max(right - left, top - bottom) * scale;
        var cuts = Math.Clamp((int)Math.Ceiling(size / PieceLength), 1, MaxCuts);
        while (cuts > 1 && cuts * cuts * 2 > budget)
        {
            cuts--;
        }

        return cuts;
    }

    // The triangle cut into cuts² smaller ones, the values at each corner taken between the big one's by barycentric
    // weights, then turned into color.
    private static void AddTriangle(List<ColoredVertex> vertices, PdfShading shading, PdfMeshTriangle triangle, int cuts,
        double alpha)
    {
        ColoredVertex At(int i, int j)
        {
            var (a, b) = ((double)i / cuts, (double)j / cuts);
            var c = 1 - a - b;
            var position = new Vector2<double>(
                triangle.A.Position.X * c + triangle.B.Position.X * a + triangle.C.Position.X * b,
                triangle.A.Position.Y * c + triangle.B.Position.Y * a + triangle.C.Position.Y * b);
            var values = new double[triangle.A.Values.Length];
            for (var k = 0; k < values.Length; k++)
            {
                values[k] = triangle.A.Values[k] * c + Value(triangle.B.Values, k) * a + Value(triangle.C.Values, k) * b;
            }

            return new ColoredVertex(position, ColorOf(shading, values, alpha));
        }

        for (var j = 0; j < cuts; j++)
        {
            for (var i = 0; i + j < cuts; i++)
            {
                vertices.Add(At(i, j));
                vertices.Add(At(i + 1, j));
                vertices.Add(At(i, j + 1));
                if (i + j + 1 < cuts)
                {
                    vertices.Add(At(i + 1, j));
                    vertices.Add(At(i + 1, j + 1));
                    vertices.Add(At(i, j + 1));
                }
            }
        }
    }

    // S(u, v) = sum of Pij Bi(u) Bj(v) (ISO 32000-2, 8.7.4.5.8), with the corner colors c00, c03, c33, c30 at
    // (u, v) = (0, 0), (0, 1), (1, 1), (1, 0).
    private static void AddPatch(List<ColoredVertex> vertices, PdfShading shading, PdfMeshPatch patch, int cuts,
        double alpha)
    {
        var grid = new ColoredVertex[(cuts + 1) * (cuts + 1)];
        Span<double> bu = stackalloc double[4];
        Span<double> bv = stackalloc double[4];
        for (var iu = 0; iu <= cuts; iu++)
        {
            var u = (double)iu / cuts;
            Bernstein(u, bu);
            for (var iv = 0; iv <= cuts; iv++)
            {
                var v = (double)iv / cuts;
                Bernstein(v, bv);
                double x = 0, y = 0;
                for (var i = 0; i < 4; i++)
                {
                    for (var j = 0; j < 4; j++)
                    {
                        var weight = bu[i] * bv[j];
                        x += patch[i, j].X * weight;
                        y += patch[i, j].Y * weight;
                    }
                }

                var values = new double[patch.Corners[0].Length];
                for (var k = 0; k < values.Length; k++)
                {
                    values[k] = Value(patch.Corners[0], k) * (1 - u) * (1 - v) + Value(patch.Corners[1], k) * (1 - u) * v +
                                Value(patch.Corners[2], k) * u * v + Value(patch.Corners[3], k) * u * (1 - v);
                }

                grid[iu * (cuts + 1) + iv] = new ColoredVertex(new Vector2<double>(x, y), ColorOf(shading, values, alpha));
            }
        }

        for (var iu = 0; iu < cuts; iu++)
        {
            for (var iv = 0; iv < cuts; iv++)
            {
                var a = grid[iu * (cuts + 1) + iv];
                var b = grid[(iu + 1) * (cuts + 1) + iv];
                var c = grid[(iu + 1) * (cuts + 1) + iv + 1];
                var d = grid[iu * (cuts + 1) + iv + 1];
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(a);
                vertices.Add(c);
                vertices.Add(d);
            }
        }
    }

    private static void Bernstein(double t, Span<double> b)
    {
        var s = 1 - t;
        b[0] = s * s * s;
        b[1] = 3 * t * s * s;
        b[2] = 3 * t * t * s;
        b[3] = t * t * t;
    }

    private static double Value(double[] values, int k) => k < values.Length ? values[k] : 0;

    private static Color ColorOf(PdfShading shading, double[] values, double alpha)
    {
        Span<double> rgb = stackalloc double[3];
        shading.ToRgb(values, rgb);
        return new Color(Byte(rgb[0]), Byte(rgb[1]), Byte(rgb[2]), Byte(alpha));
    }

    private static byte Byte(double value) =>
        (byte)Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1) * 255);
}
