using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Curves;

/// <summary>A quadratic Bezier curve; a straight piece has its control halfway along.</summary>
internal readonly record struct QuadraticCurve(Vector2<double> Start, Vector2<double> Control, Vector2<double> End)
{
    /// <summary>A straight piece from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static QuadraticCurve Line(Vector2<double> start, Vector2<double> end) =>
        new(start, (start + end) * 0.5, end);

    /// <summary>Whether every coordinate is a finite number.</summary>
    public bool IsFinite =>
        double.IsFinite(Start.X) && double.IsFinite(Start.Y) && double.IsFinite(Control.X) &&
        double.IsFinite(Control.Y) && double.IsFinite(End.X) && double.IsFinite(End.Y);

    /// <summary>The point at <paramref name="t"/>, from 0 at the start to 1 at the end.</summary>
    public Vector2<double> At(double t)
    {
        var rest = 1 - t;
        return Start * (rest * rest) + Control * (2 * rest * t) + End * (t * t);
    }

    /// <summary>Whether the control lies on the chord between the ends, within <paramref name="tolerance"/> of the chord's
    /// length: the curve is a straight piece. One that runs past an end and back, or out and back to where it began,
    /// is not.</summary>
    public bool IsStraight(double tolerance = 1e-9)
    {
        var chord = End - Start;
        var off = Control - Start;
        var lengthSquared = chord.X * chord.X + chord.Y * chord.Y;
        if (lengthSquared <= 1e-300)
        {
            return off.X * off.X + off.Y * off.Y <= 1e-300;
        }

        var cross = chord.X * off.Y - chord.Y * off.X;
        var along = chord.X * off.X + chord.Y * off.Y;
        return Math.Abs(cross) <= tolerance * lengthSquared && along >= -tolerance * lengthSquared &&
               along <= (1 + tolerance) * lengthSquared;
    }

    /// <summary>How long the curve is from its start to <paramref name="t"/>: five-point Gauss-Legendre over its speed,
    /// the rule the stroke shader measures by, so the two agree.</summary>
    public double LengthTo(double t)
    {
        var a = Start - Control * 2 + End;
        var b = Control - Start;
        var length = 0.0;
        for (var i = 0; i < 5; i++)
        {
            var u = t * 0.5 * (1 + GaussNodes[i]);
            var speed = (a * u + b) * 2;
            length += GaussWeights[i] * Math.Sqrt(speed.X * speed.X + speed.Y * speed.Y);
        }

        return length * t * 0.5;
    }

    private static readonly double[] GaussNodes =
        [-0.9061798459386640, -0.5384693101056831, 0, 0.5384693101056831, 0.9061798459386640];

    private static readonly double[] GaussWeights =
        [0.2369268850561891, 0.4786286704993665, 0.5688888888888889, 0.4786286704993665, 0.2369268850561891];

    /// <summary>The two halves of the curve either side of <paramref name="t"/>.</summary>
    public (QuadraticCurve First, QuadraticCurve Second) Split(double t)
    {
        var a = Vector2<double>.Lerp(Start, Control, t);
        var b = Vector2<double>.Lerp(Control, End, t);
        var middle = Vector2<double>.Lerp(a, b, t);
        return (new QuadraticCurve(Start, a, middle), new QuadraticCurve(middle, b, End));
    }

    /// <summary>The curve with every point put through <paramref name="matrix"/>: an affine map moves a Bezier curve
    /// exactly by moving its points.</summary>
    public QuadraticCurve Transformed(Matrix4x4<double> matrix) =>
        new(Vector2<double>.TransformCoordinate(Start, matrix), Vector2<double>.TransformCoordinate(Control, matrix),
            Vector2<double>.TransformCoordinate(End, matrix));
}
