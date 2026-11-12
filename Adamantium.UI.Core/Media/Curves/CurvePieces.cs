using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Curves;

/// <summary>A fill's curves as a shader walks them: cut into pieces that run one way in x and in y, so a ray along
/// either axis meets a piece at most once.</summary>
internal sealed class CurvePieces
{
    private CurvePieces(QuadraticCurve[] pieces, Rect bounds)
    {
        Pieces = pieces;
        Bounds = bounds;
    }

    /// <summary>The pieces, each running one way in x and one way in y.</summary>
    public QuadraticCurve[] Pieces { get; }

    /// <summary>What the pieces' ends cover - and so the pieces, each running one way.</summary>
    public Rect Bounds { get; }

    /// <summary>Cuts <paramref name="curves"/> into pieces.</summary>
    public static CurvePieces Of(IReadOnlyList<QuadraticCurve> curves)
    {
        var pieces = new List<QuadraticCurve>(curves.Count * 2);
        foreach (var curve in curves)
        {
            AddMonotonic(curve, pieces);
        }

        return new CurvePieces(pieces.ToArray(), BoundsOf(pieces));
    }

    /// <summary>How many times the curves wind round <paramref name="point"/>, counted along a ray toward +x: a piece
    /// crossed going down counts one, going up minus one. The exact count, no edge softened - what a hit test asks.
    /// </summary>
    public int Winding(Vector2<double> point)
    {
        var winding = 0;
        foreach (var piece in Pieces)
        {
            var low = Math.Min(piece.Start.Y, piece.End.Y);
            var high = Math.Max(piece.Start.Y, piece.End.Y);
            if (point.Y < low || point.Y >= high)
            {
                continue;
            }

            var t = Crossing(piece.Start.Y, piece.Control.Y, piece.End.Y, point.Y);
            if (piece.At(t).X > point.X)
            {
                winding += piece.End.Y > piece.Start.Y ? 1 : -1;
            }
        }

        return winding;
    }

    /// <summary>Where along a piece running one way its coordinate - given by its start <paramref name="a"/>, control
    /// <paramref name="b"/> and end <paramref name="c"/> - reaches <paramref name="value"/>.</summary>
    internal static double Crossing(double a, double b, double c, double value)
    {
        var qa = a - 2 * b + c;
        var qb = 2 * (b - a);
        var qc = a - value;
        double t;
        if (Math.Abs(qa) < 1e-12 * (Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + 1))
        {
            t = Math.Abs(qb) < 1e-300 ? 0 : -qc / qb;
        }
        else
        {
            var root = Math.Sqrt(Math.Max(0, qb * qb - 4 * qa * qc));
            var t1 = (-qb + root) / (2 * qa);
            var t2 = (-qb - root) / (2 * qa);
            t = t1 is >= -1e-9 and <= 1 + 1e-9 ? t1 : t2;
        }

        return Math.Clamp(t, 0, 1);
    }

    private static void AddMonotonic(QuadraticCurve curve, List<QuadraticCurve> pieces)
    {
        // Cut where the curve turns in x and where it turns in y, in order along it.
        Span<double> cuts = stackalloc double[2];
        var count = 0;
        foreach (var turn in (ReadOnlySpan<double>)[Turn(curve.Start.X, curve.Control.X, curve.End.X),
                     Turn(curve.Start.Y, curve.Control.Y, curve.End.Y)])
        {
            if (turn is > 1e-9 and < 1 - 1e-9)
            {
                cuts[count++] = turn;
            }
        }

        if (count == 2 && cuts[1] < cuts[0])
        {
            (cuts[0], cuts[1]) = (cuts[1], cuts[0]);
        }

        var rest = curve;
        var done = 0.0;
        for (var i = 0; i < count; i++)
        {
            var (first, second) = rest.Split((cuts[i] - done) / (1 - done));
            pieces.Add(Flattened(first));
            rest = second;
            done = cuts[i];
        }

        pieces.Add(Flattened(rest));
    }

    // Where a quadratic coordinate turns back, or NaN for one that runs one way.
    private static double Turn(double a, double b, double c)
    {
        var bend = a - 2 * b + c;
        return Math.Abs(bend) < 1e-300 ? double.NaN : (a - b) / bend;
    }

    // A cut lands its control on the turn's tangent, a hair outside the ends' range: put it back inside, so the piece
    // runs one way exactly.
    private static QuadraticCurve Flattened(QuadraticCurve piece)
    {
        var control = new Vector2<double>(
            Math.Clamp(piece.Control.X, Math.Min(piece.Start.X, piece.End.X), Math.Max(piece.Start.X, piece.End.X)),
            Math.Clamp(piece.Control.Y, Math.Min(piece.Start.Y, piece.End.Y), Math.Max(piece.Start.Y, piece.End.Y)));
        return piece with { Control = control };
    }

    private static Rect BoundsOf(List<QuadraticCurve> pieces)
    {
        if (pieces.Count == 0)
        {
            return Rect.Empty;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var piece in pieces)
        {
            foreach (var point in (ReadOnlySpan<Vector2<double>>)[piece.Start, piece.End])
            {
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
