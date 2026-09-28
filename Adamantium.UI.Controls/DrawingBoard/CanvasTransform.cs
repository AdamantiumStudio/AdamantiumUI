using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>An item's rotation and skew as one transform about the middle of its box, so outline, hit test and frame agree.</summary>
public readonly struct CanvasTransform : IEquatable<CanvasTransform>
{
    public CanvasTransform(double angle, double skewX = 0, double skewY = 0)
    {
        Angle = angle;
        SkewX = skewX;
        SkewY = skewY;
    }

    /// <summary>Nothing turned and nothing leaned.</summary>
    public static CanvasTransform None => default;

    /// <summary>Degrees clockwise on the screen, which is the direction the plane's Y runs.</summary>
    public double Angle { get; init; }

    /// <summary>Degrees the horizontal leans by.</summary>
    public double SkewX { get; init; }

    /// <summary>Degrees the vertical leans by.</summary>
    public double SkewY { get; init; }

    /// <summary>Whether this says anything at all. Everything that costs something - a second pass over the points, a
    /// per-unit draw instead of a batched one - is skipped when it does not.</summary>
    public bool IsSomething => Angle != 0 || SkewX != 0 || SkewY != 0;

    /// <summary>Where a point of the item ends up, turned about <paramref name="about"/>.</summary>
    public Vector2 Apply(Vector2 point, Vector2 about)
    {
        if (!IsSomething) return point;

        var x = point.X - about.X;
        var y = point.Y - about.Y;

        // Skew, then rotate, as drawing programs state it; both leans read the original x and y, as a true shear does.
        if (SkewX != 0 || SkewY != 0)
        {
            var leaned = x + y * Math.Tan(SkewX * Math.PI / 180);
            y += x * Math.Tan(SkewY * Math.PI / 180);
            x = leaned;
        }

        if (Angle != 0)
        {
            var radians = Angle * Math.PI / 180;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            (x, y) = (x * cos - y * sin, x * sin + y * cos);
        }

        return new Vector2(about.X + x, about.Y + y);
    }

    /// <summary>Where a point on the SCREEN came from - the inverse. What hit-testing needs: a turned shape is asked
    /// about a point in the world, and the only way it can answer is to un-turn the point and ask its plain self.
    /// </summary>
    public Vector2 Undo(Vector2 point, Vector2 about)
    {
        if (!IsSomething) return point;

        var x = point.X - about.X;
        var y = point.Y - about.Y;

        if (Angle != 0)
        {
            var radians = -Angle * Math.PI / 180;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);

            (x, y) = (x * cos - y * sin, x * sin + y * cos);
        }

        // The shear UNDONE as a pair, because it was done as a pair: taking one lean out and then the other from the
        // half-undone number is not the inverse of shearing both at once.
        if (SkewX != 0 || SkewY != 0)
        {
            var leanX = Math.Tan(SkewX * Math.PI / 180);
            var leanY = Math.Tan(SkewY * Math.PI / 180);
            var room = 1 - leanX * leanY;

            // Flat: the two leans together have squashed the plane onto a line, and nothing can be taken back out of
            // it. The point is left where it is rather than divided by nothing.
            if (Math.Abs(room) > 1e-9)
            {
                var straight = (x - y * leanX) / room;
                y = (y - x * leanY) / room;
                x = straight;
            }
        }

        return new Vector2(about.X + x, about.Y + y);
    }

    /// <summary>The transform as a row-vector matrix (v * M) about a point, the convention <c>TransformValues</c> uses, so
    /// pixels, frame and hit test share one arithmetic.</summary>
    public Matrix4x4F Matrix(Vector2 about)
    {
        var lean = Matrix4x4F.Identity;

        if (SkewX != 0 || SkewY != 0)
        {
            lean.M21 = (float)Math.Tan(SkewX * Math.PI / 180);
            lean.M12 = (float)Math.Tan(SkewY * Math.PI / 180);
        }

        return Matrix4x4F.Translation((float)-about.X, (float)-about.Y, 0)
               * lean
               * Matrix4x4F.RotationZ((float)(Angle * Math.PI / 180))
               * Matrix4x4F.Translation((float)about.X, (float)about.Y, 0);
    }

    public bool Equals(CanvasTransform other) =>
        Angle.Equals(other.Angle) && SkewX.Equals(other.SkewX) && SkewY.Equals(other.SkewY);

    public override bool Equals(object obj) => obj is CanvasTransform other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Angle, SkewX, SkewY);
}
