using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// A contour a stroke runs along: its points, and between each point and the next a quadratic curve through the
/// control of the same index - the last point's control closes a closed contour. A contour of straight pieces only has no
/// controls at all.
/// </summary>
internal sealed class StrokeContour
{
    public StrokeContour(Vector2<double>[] points, Vector2<double>[] controls, bool isClosed)
    {
        Points = points;
        Controls = controls;
        IsClosed = isClosed;
    }

    /// <summary>Where each piece starts; an open contour's last one is where it ends.</summary>
    public Vector2<double>[] Points { get; }

    /// <summary>Each piece's control, as many as <see cref="Points"/>; null when every piece is straight.</summary>
    public Vector2<double>[] Controls { get; }

    public bool IsClosed { get; }

    /// <summary>How many pieces it has: one a point closed, one fewer open.</summary>
    public int PieceCount => IsClosed ? Points.Length : Points.Length - 1;

    /// <summary>Piece <paramref name="i"/>, from point i to the next.</summary>
    public QuadraticCurve Piece(int i)
    {
        var start = Points[i];
        var end = Points[(i + 1) % Points.Length];
        return Controls == null ? QuadraticCurve.Line(start, end) : new QuadraticCurve(start, Controls[i], end);
    }

    /// <summary>How far along it each point lies from its first - a straight piece's length its ends' distance, a bending
    /// one's by <see cref="QuadraticCurve.LengthTo"/>, as the stroke shader measures each - and the length of the whole,
    /// the closing piece included.</summary>
    public double[] Arcs(out double length)
    {
        var arcs = new double[Points.Length];
        var along = 0.0;
        for (var i = 0; i < Points.Length; i++)
        {
            arcs[i] = along;
            if (i < PieceCount)
            {
                along += Controls == null ? (Points[(i + 1) % Points.Length] - Points[i]).Length() : Piece(i).LengthTo(1);
            }
        }

        length = along;
        return arcs;
    }

    /// <summary>A contour of straight pieces through <paramref name="points"/>.</summary>
    public static StrokeContour Straight(Vector2<double>[] points, bool isClosed) => new(points, null, isClosed);

    /// <summary>The contours of straight pieces through each of <paramref name="contours"/>.</summary>
    public static List<StrokeContour> Straight(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours)
    {
        var straight = new List<StrokeContour>(contours.Count);
        foreach (var (points, isClosed) in contours)
        {
            straight.Add(Straight(points, isClosed));
        }

        return straight;
    }
}
