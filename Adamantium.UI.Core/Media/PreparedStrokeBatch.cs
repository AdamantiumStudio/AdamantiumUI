using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// A run of a stroke's contours as the renderer strokes them in one continuous batch - their points as floats one after
/// another, the pieces' controls likewise, where each contour starts and how long it is, the contour of every point, and
/// how far along its contour every point lies - made with the contours, so a stroke processed on a worker (a map's
/// lines) reaches the drawing thread with nothing left to lay out. Shared and never written: whoever changes the points
/// makes its own copy.
/// </summary>
internal sealed class PreparedStrokeBatch
{
    /// <summary>The most points a batch holds: its pieces are numbered two per point, in 16 bits.</summary>
    public const int MostPoints = 32768;

    private PreparedStrokeBatch(int start, int count, float[] points, float[] controls, uint[] contours,
        uint[] pointContours, float[] arcs)
    {
        Start = start;
        Count = count;
        Points = points;
        Controls = controls;
        Contours = contours;
        PointContours = pointContours;
        Arcs = arcs;
    }

    /// <summary>The first of the contours, in the list <see cref="Valid"/> makes.</summary>
    public int Start { get; }

    public int Count { get; }

    /// <summary>x, y of every point.</summary>
    public float[] Points { get; }

    /// <summary>x, y of every point's piece's control; null when every piece of the run is straight.</summary>
    public float[] Controls { get; }

    /// <summary>Three to a contour: its first point, its point count, 1 when it closes.</summary>
    public uint[] Contours { get; }

    public uint[] PointContours { get; }

    public float[] Arcs { get; }

    /// <summary>Whether a stroke can draw <paramref name="contour"/> as it is: a piece at least, and a closed one
    /// enclosing something - three straight pieces, or two of which one may bend.</summary>
    public static bool IsDrawable(StrokeContour contour) =>
        contour.Points is { Length: >= 2 } &&
        (!contour.IsClosed || contour.Points.Length >= 3 || contour.Controls != null);

    /// <summary>Contours of a piece or more, a closed one that encloses nothing taken as open: what a stroke draws. The
    /// list itself when every contour already is.</summary>
    public static IReadOnlyList<StrokeContour> Valid(IReadOnlyList<StrokeContour> contours)
    {
        var asIs = true;
        foreach (var contour in contours)
        {
            if (!IsDrawable(contour))
            {
                asIs = false;
                break;
            }
        }

        if (asIs)
        {
            return contours;
        }

        var valid = new List<StrokeContour>(contours.Count);
        foreach (var contour in contours)
        {
            if (contour.Points is not { Length: >= 2 })
            {
                continue;
            }

            valid.Add(IsDrawable(contour) ? contour : new StrokeContour(contour.Points, contour.Controls, false));
        }

        return valid;
    }

    /// <summary>Contours grouped into runs of at most <see cref="MostPoints"/> points; a contour is never split, and one
    /// bigger than a batch is a batch of its own.</summary>
    public static List<(int Start, int Count)> Batches(IReadOnlyList<StrokeContour> contours)
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
    public static PreparedStrokeBatch[] Of(IReadOnlyList<StrokeContour> contours)
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

    /// <summary>Writes <paramref name="contour"/>'s points as floats into <paramref name="points"/> from point
    /// <paramref name="at"/> on, its pieces' controls into <paramref name="controls"/> when given - a straight piece's
    /// halfway along - and how far along it each point lies into <paramref name="arcs"/>, measured between the floats the
    /// shader measures between. Returns its length.</summary>
    public static double Write(StrokeContour contour, float[] points, float[] controls, float[] arcs, int at)
    {
        var count = contour.Points.Length;
        for (var i = 0; i < count; i++)
        {
            var point = contour.Points[i];
            points[(at + i) * 2] = (float)point.X;
            points[(at + i) * 2 + 1] = (float)point.Y;
            if (controls != null)
            {
                var control = i < contour.PieceCount ? contour.Piece(i).Control : point;
                controls[(at + i) * 2] = (float)control.X;
                controls[(at + i) * 2 + 1] = (float)control.Y;
            }
        }

        var along = 0.0;
        for (var i = 0; i < count; i++)
        {
            arcs[at + i] = (float)along;
            if (i >= contour.PieceCount)
            {
                continue;
            }

            var from = at + i;
            var to = at + (i + 1) % count;
            var start = new Vector2<double>(points[from * 2], points[from * 2 + 1]);
            var end = new Vector2<double>(points[to * 2], points[to * 2 + 1]);
            along += contour.Controls == null || controls == null
                ? (end - start).Length()
                : new QuadraticCurve(start, new Vector2<double>(controls[from * 2], controls[from * 2 + 1]), end).LengthTo(1);
        }

        return along;
    }

    /// <summary>Whether any of the <paramref name="count"/> contours from <paramref name="start"/> bends.</summary>
    public static bool AnyBends(IReadOnlyList<StrokeContour> contours, int start, int count)
    {
        for (var k = start; k < start + count; k++)
        {
            if (contours[k].Controls != null)
            {
                return true;
            }
        }

        return false;
    }

    private static PreparedStrokeBatch Lay(IReadOnlyList<StrokeContour> contours, int start, int count)
    {
        var total = 0;
        for (var k = start; k < start + count; k++)
        {
            total += contours[k].Points.Length;
        }

        var points = new float[total * 2];
        var controls = AnyBends(contours, start, count) ? new float[total * 2] : null;
        var table = new uint[count * 3];
        var owners = new uint[total];
        var arcs = new float[total];
        var at = 0;
        for (var k = 0; k < count; k++)
        {
            var contour = contours[start + k];
            table[k * 3] = (uint)at;
            table[k * 3 + 1] = (uint)contour.Points.Length;
            table[k * 3 + 2] = contour.IsClosed ? 1u : 0u;
            Write(contour, points, controls, arcs, at);
            for (var i = 0; i < contour.Points.Length; i++)
            {
                owners[at + i] = (uint)k;
            }

            at += contour.Points.Length;
        }

        return new PreparedStrokeBatch(start, count, points, controls, table, owners, arcs);
    }
}
