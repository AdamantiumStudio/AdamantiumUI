using Adamantium.Mathematics;

namespace Adamantium.UI.Core;

/// <summary>A desktop point in physical pixels. A separate type so it cannot mix with DIPs; convert with
/// <see cref="ToLogical"/>/<see cref="FromLogical"/> against a specific window's scale.</summary>
public readonly struct PixelPoint : IEquatable<PixelPoint>
{
    public PixelPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }

    public static PixelPoint Zero => new(0, 0);

    /// <summary>This desktop point in the logical units of a surface whose scale is <paramref name="dpiScale"/>. Per
    /// AXIS, not one number: the scale is a <see cref="Vector2"/> because a display can be anisotropic.</summary>
    public Vector2 ToLogical(Vector2 dpiScale) => new((float)(X / dpiScale.X), (float)(Y / dpiScale.Y));

    /// <summary>A logical offset (a size, a distance - not a point) as physical pixels at <paramref name="dpiScale"/>.</summary>
    public static PixelPoint FromLogical(Vector2 logical, Vector2 dpiScale) =>
        new(logical.X * dpiScale.X, logical.Y * dpiScale.Y);

    /// <summary>The vector from <paramref name="other"/> to this point, still in physical pixels. Subtracting two
    /// desktop points is the one operation that is meaningful without a scale.</summary>
    public PixelPoint Minus(PixelPoint other) => new(X - other.X, Y - other.Y);

    public static PixelPoint operator +(PixelPoint left, PixelPoint right) => new(left.X + right.X, left.Y + right.Y);

    public static PixelPoint operator -(PixelPoint left, PixelPoint right) => left.Minus(right);

    public static bool operator ==(PixelPoint left, PixelPoint right) => left.Equals(right);

    public static bool operator !=(PixelPoint left, PixelPoint right) => !left.Equals(right);

    public bool Equals(PixelPoint other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object obj) => obj is PixelPoint other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public override string ToString() => $"{X};{Y} px";
}
