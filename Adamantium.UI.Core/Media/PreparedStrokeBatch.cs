using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// A run of a stroke's contours as the renderer strokes them in one continuous batch - their points as floats one after
/// another, where each contour starts and how long it is, the contour of every point, and how far along its contour
/// every point lies - made with the contours, so a stroke processed on a worker (a map's lines) reaches the drawing
/// thread with nothing left to lay out. Shared and never written: whoever changes the points makes its own copy.
/// </summary>
internal sealed class PreparedStrokeBatch
{
    /// <summary>The most points a batch holds: its pieces are numbered two per point, in 16 bits.</summary>
    public const int MostPoints = 32768;

    private PreparedStrokeBatch(int start, int count, float[] points, uint[] contours, uint[] pointContours,
        float[] arcs)
    {
        Start = start;
        Count = count;
        Points = points;
        Contours = contours;
        PointContours = pointContours;
        Arcs = arcs;
    }

    /// <summary>The first of the contours, in the list <see cref="Valid"/> makes.</summary>
    public int Start { get; }

    public int Count { get; }

    /// <summary>x, y of every point.</summary>
    public float[] Points { get; }

    /// <summary>Three to a contour: its first point, its point count, 1 when it closes.</summary>
    public uint[] Contours { get; }

    public uint[] PointContours { get; }

    public float[] Arcs { get; }

    /// <summary>Contours of two points or more, a closed one of fewer than three taken as open: what a stroke draws.
    /// The list itself when every contour already is.</summary>
    public static IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> Valid(
        IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours)
    {
        var asIs = true;
        foreach (var (points, isClosed) in contours)
        {
            if (points is not { Length: >= 2 } || isClosed && points.Length < 3)
            {
                asIs = false;
                break;
            }
        }

        if (asIs)
        {
            return contours;
        }

        var valid = new List<(Vector2<double>[] Points, bool IsClosed)>(contours.Count);
        foreach (var (points, isClosed) in contours)
        {
            if (points is { Length: >= 2 })
            {
                valid.Add((points, isClosed && points.Length >= 3));
            }
        }

        return valid;
    }

    /// <summary>Contours grouped into runs of at most <see cref="MostPoints"/> points; a contour is never split, and one
    /// bigger than a batch is a batch of its own.</summary>
    public static List<(int Start, int Count)> Batches(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours)
    {
        var batches = new List<(int Start, int Count)>();
        var start = 0;
        var points = 0;
        for (var i = 0; i < contours.Count; i++)
        {
            if (i > start && points + contours[i].Points.Length > MostPoints)
            {
                batches.Add((start, i - start));
                start = i;
                points = 0;
            }

            points += contours[i].Points.Length;
        }

        if (contours.Count > start)
        {
            batches.Add((start, contours.Count - start));
        }

        return batches;
    }

    /// <summary>The continuous batches of <paramref name="contours"/> laid out, one for each run of
    /// <see cref="Batches"/> - null for a run of one contour, which is stroked on its own; empty for fewer than two
    /// contours.</summary>
    public static PreparedStrokeBatch[] Of(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours)
    {
        var valid = Valid(contours);
        if (valid.Count < 2)
        {
            return [];
        }

        var runs = Batches(valid);
        var batches = new PreparedStrokeBatch[runs.Count];
        for (var b = 0; b < runs.Count; b++)
        {
            var (start, count) = runs[b];
            if (count > 1)
            {
                batches[b] = Lay(valid, start, count);
            }
        }

        return batches;
    }

    private static PreparedStrokeBatch Lay(IReadOnlyList<(Vector2<double>[] Points, bool IsClosed)> contours, int start,
        int count)
    {
        var total = 0;
        for (var k = start; k < start + count; k++)
        {
            total += contours[k].Points.Length;
        }

        var points = new float[total * 2];
        var table = new uint[count * 3];
        var owners = new uint[total];
        var arcs = new float[total];
        var at = 0;
        for (var k = 0; k < count; k++)
        {
            var (contour, isClosed) = contours[start + k];
            table[k * 3] = (uint)at;
            table[k * 3 + 1] = (uint)contour.Length;
            table[k * 3 + 2] = isClosed ? 1u : 0u;
            var first = at;
            var along = 0.0;
            foreach (var point in contour)
            {
                points[at * 2] = (float)point.X;
                points[at * 2 + 1] = (float)point.Y;
                owners[at] = (uint)k;
                if (at > first)
                {
                    var dx = points[at * 2] - points[at * 2 - 2];
                    var dy = points[at * 2 + 1] - points[at * 2 - 1];
                    along += System.Math.Sqrt((double)dx * dx + (double)dy * dy);
                }

                arcs[at] = (float)along;
                at++;
            }
        }

        return new PreparedStrokeBatch(start, count, points, table, owners, arcs);
    }
}
