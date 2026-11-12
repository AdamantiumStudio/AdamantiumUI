using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Rendering;

/// <summary>
/// A gradient as the per-draw uniforms of an effect that includes <c>GradientUniforms.fxh</c>, packed by
/// <see cref="GradientBake"/>: the fill's analytic-AA ring and the stroke are colored by the same gradient this way.
/// </summary>
internal sealed class GradientUniforms
{
    private readonly EffectParameter _isGradient;
    private readonly EffectParameter _params;
    private readonly EffectParameter _geom0;
    private readonly EffectParameter _geom1;
    private readonly EffectParameter _localBounds;
    private readonly EffectParameter[] _stops = new EffectParameter[GradientBake.MaxStops];
    private readonly EffectParameter _offsets0;
    private readonly EffectParameter _offsets1;

    public GradientUniforms(Effect effect)
    {
        var parameters = effect.Parameters;
        _isGradient = parameters["IsGradient"];
        _params = parameters["GParams"];
        _geom0 = parameters["GGeom0"];
        _geom1 = parameters["GGeom1"];
        _localBounds = parameters["GLocalBounds"];
        for (var i = 0; i < _stops.Length; i++) _stops[i] = parameters["GS" + i];
        _offsets0 = parameters["GOff0"];
        _offsets1 = parameters["GOff1"];
    }

    /// <summary>Colors the next draw by <paramref name="gradient"/> over <paramref name="localBounds"/> (minX, minY,
    /// width, height in the shape's local units), its stops' alpha scaled by <paramref name="alpha"/>.</summary>
    public void Set(GradientBrush gradient, float alpha, Vector4F localBounds)
    {
        Span<Vector4F> colors = stackalloc Vector4F[GradientBake.MaxStops];
        Span<float> offsets = stackalloc float[GradientBake.MaxStops];
        var count = GradientBake.PackStops(gradient, alpha, colors, offsets);
        var type = GradientBake.PackGeometry(gradient, out var geom0, out var geom1);

        _isGradient.SetValue(1);
        _params.SetValue(new Vector4F(0, type, count, (float)gradient.SpreadMethod));
        _geom0.SetValue(geom0);
        _geom1.SetValue(geom1);
        _localBounds.SetValue(localBounds);
        for (var i = 0; i < _stops.Length; i++) _stops[i].SetValue(colors[i]);
        _offsets0.SetValue(new Vector4F(offsets[0], offsets[1], offsets[2], offsets[3]));
        _offsets1.SetValue(new Vector4F(offsets[4], offsets[5], offsets[6], offsets[7]));
    }

    /// <summary>The next draw is not a gradient.</summary>
    public void Clear() => _isGradient.SetValue(0);

    /// <summary>Whether any of <paramref name="gradient"/>'s stops shows through at <paramref name="alpha"/>.</summary>
    public static bool IsTranslucent(GradientBrush gradient, float alpha)
    {
        if (alpha < 1f) return true;
        if (gradient is MeshGradientBrush mesh)   // its colors are its four corners, not stops
            return mesh.TopLeft.A < 255 || mesh.TopRight.A < 255 || mesh.BottomLeft.A < 255 || mesh.BottomRight.A < 255;

        foreach (var stop in gradient.GradientStops)
            if (stop.Color.A < 255) return true;
        return false;
    }

    /// <summary>The bounding box of every contour point as (minX, minY, width, height) - the box a shape's gradient
    /// spans.</summary>
    public static Vector4F BoundsOf(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var (points, _) in contours)
            foreach (var p in points)
            {
                if (p.X < minX) minX = (float)p.X;
                if (p.Y < minY) minY = (float)p.Y;
                if (p.X > maxX) maxX = (float)p.X;
                if (p.Y > maxY) maxY = (float)p.Y;
            }
        if (minX > maxX) return new Vector4F(0, 0, 1, 1);
        return new Vector4F(minX, minY, Math.Max(maxX - minX, 1e-4f), Math.Max(maxY - minY, 1e-4f));
    }

    /// <summary>The bounding box of every contour as (minX, minY, width, height): its points, and where a bending piece
    /// turns back between them.</summary>
    internal static Vector4F BoundsOf(IReadOnlyList<StrokeContour> contours)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var contour in contours)
        {
            foreach (var p in contour.Points) Grow(p);
            if (contour.Controls == null) continue;

            for (var i = 0; i < contour.PieceCount; i++)
            {
                var piece = contour.Piece(i);
                Grow(piece.At(Turn(piece.Start.X, piece.Control.X, piece.End.X)));
                Grow(piece.At(Turn(piece.Start.Y, piece.Control.Y, piece.End.Y)));
            }
        }

        if (minX > maxX) return new Vector4F(0, 0, 1, 1);
        return new Vector4F((float)minX, (float)minY, Math.Max((float)(maxX - minX), 1e-4f),
            Math.Max((float)(maxY - minY), 1e-4f));

        void Grow(Vector2<double> p)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        // Where a quadratic coordinate turns back, inside the piece; its start where it runs one way.
        static double Turn(double a, double b, double c)
        {
            var bend = a - 2 * b + c;
            return Math.Abs(bend) < 1e-300 ? 0 : Math.Clamp((a - b) / bend, 0, 1);
        }
    }
}
