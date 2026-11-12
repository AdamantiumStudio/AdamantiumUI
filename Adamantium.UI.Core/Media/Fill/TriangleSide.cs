using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Fill;

/// <summary>A triangle side between two rounded corners, the same whichever way round it is given.</summary>
internal readonly struct TriangleSide : IEquatable<TriangleSide>
{
    public TriangleSide(Vector2<double> a, Vector2<double> b)
    {
        var byX = a.X.CompareTo(b.X);
        var aFirst = byX < 0 || (byX == 0 && a.Y.CompareTo(b.Y) <= 0);
        Low = aFirst ? a : b;
        High = aFirst ? b : a;
    }

    /// <summary>The end with the smaller X, or the smaller Y when both X are equal.</summary>
    public Vector2<double> Low { get; }

    /// <summary>The other end.</summary>
    public Vector2<double> High { get; }

    public bool Equals(TriangleSide other) => Low.Equals(other.Low) && High.Equals(other.High);

    public override bool Equals(object obj) => obj is TriangleSide other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Low, High);
}
