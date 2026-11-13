using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// The contours a geometry's stroke runs along, as the renderer strokes them - each the curves of a figure, the ones
/// with no length dropped, and whether it closes - made where the geometry was processed, so whoever processes it on a
/// worker (a map's tiles) leaves the drawing thread nothing to work out. Immutable; tied to the
/// <see cref="Geometry.Version"/> it was made from.
/// </summary>
internal sealed class PreparedStroke
{
    // Squared: two points nearer than this are one.
    private const double SamePointSq = 1e-6;

    // How near a piece's measured length has to come to that of its two halves: a piece bent past it is halved, so its
    // length along it is measured as closely as its whole.
    private const double LengthTolerance = 1e-5;
    private const int MostHalvings = 8;

    private PreparedStroke(int version, List<StrokeContour> contours, bool layOut)
    {
        Version = version;
        Contours = contours;
        Batches = layOut ? PreparedStrokeBatch.Of(contours) : null;
    }

    public int Version { get; }

    /// <summary>One piece or more each; empty for a geometry with nothing to stroke.</summary>
    public List<StrokeContour> Contours { get; }

    /// <summary>The contours laid out for a continuous stroke, run by run; null when not laid out ahead.</summary>
    public PreparedStrokeBatch[] Batches { get; }

    /// <summary>The stroke of <paramref name="geometry"/>; <paramref name="layOut"/> lays its batches out too - for a
    /// geometry processed on a worker and kept to be drawn.</summary>
    public static PreparedStroke Of(Geometry geometry, int version, bool layOut)
    {
        var curves = geometry.CurvesForDrawing;
        var contours = new List<StrokeContour>(curves.Figures.Count);
        var pieces = new List<QuadraticCurve>();
        foreach (var figure in curves.Figures)
        {
            pieces.Clear();
            for (var i = figure.First; i < figure.First + figure.Count; i++)
            {
                AddPiece(curves.Curves[i], pieces, 0);
            }

            if (pieces.Count == 0)
            {
                continue;
            }

            var closed = figure.IsClosed;
            var start = pieces[0].Start;
            var end = pieces[^1].End;
            if (closed && !Same(start, end))
            {
                pieces.Add(QuadraticCurve.Line(end, start));
            }

            // A closed contour needs a second piece to enclose anything a stroke can tell from an open one.
            if (closed && pieces.Count < 2)
            {
                closed = false;
            }

            contours.Add(ContourOf(pieces, closed));
        }

        return new PreparedStroke(version, contours, layOut);
    }

    private static StrokeContour ContourOf(List<QuadraticCurve> pieces, bool closed)
    {
        var count = closed ? pieces.Count : pieces.Count + 1;
        var points = new Vector2<double>[count];
        var straight = true;
        for (var i = 0; i < pieces.Count; i++)
        {
            points[i] = pieces[i].Start;
            straight &= pieces[i].IsStraight();
        }

        if (!closed)
        {
            points[^1] = pieces[^1].End;
        }

        if (straight)
        {
            return StrokeContour.Straight(points, closed);
        }

        var controls = new Vector2<double>[count];
        for (var i = 0; i < pieces.Count; i++)
        {
            controls[i] = pieces[i].Control;
        }

        if (!closed)
        {
            controls[^1] = points[^1];
        }

        return new StrokeContour(points, controls, closed);
    }

    // A piece with no length is dropped, and the next starts where the last one kept ends; one bent too far to be
    // measured whole is halved.
    private static void AddPiece(QuadraticCurve piece, List<QuadraticCurve> pieces, int halvings)
    {
        if (Same(piece.Start, piece.End) && Same(piece.Start, piece.Control))
        {
            return;
        }

        if (pieces.Count > 0 && piece.Start != pieces[^1].End)
        {
            var start = pieces[^1].End;
            piece = piece.IsStraight() ? QuadraticCurve.Line(start, piece.End) : piece with { Start = start };
        }

        if (halvings < MostHalvings && !piece.IsStraight())
        {
            var halves = piece.Split(0.5);
            var whole = piece.LengthTo(1);
            var halvesLength = halves.First.LengthTo(1) + halves.Second.LengthTo(1);
            if (System.Math.Abs(whole - halvesLength) > LengthTolerance * System.Math.Max(halvesLength, 1e-12))
            {
                AddPiece(halves.First, pieces, halvings + 1);
                AddPiece(halves.Second, pieces, halvings + 1);
                return;
            }
        }

        pieces.Add(piece);
    }

    private static bool Same(Vector2<double> a, Vector2<double> b)
    {
        var d = a - b;
        return d.X * d.X + d.Y * d.Y <= SamePointSq;
    }
}
