using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Curves;

/// <summary>A quadratic Bezier curve; a straight piece has its control halfway along.</summary>
internal readonly record struct QuadraticCurve(Vector2<double> Start, Vector2<double> Control, Vector2<double> End)
{
    /// <summary>A straight piece from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static QuadraticCurve Line(Vector2<double> start, Vector2<double> end) =>
        new(start, (start + end) * 0.5, end);

    /// <summary>The point at <paramref name="t"/>, from 0 at the start to 1 at the end.</summary>
    public Vector2<double> At(double t)
    {
        var rest = 1 - t;
        return Start * (rest * rest) + Control * (2 * rest * t) + End * (t * t);
    }

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
