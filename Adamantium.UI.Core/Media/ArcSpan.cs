using System;

namespace Adamantium.UI.Core.Media;

/// <summary>The part of an ellipse an arc covers, in the ellipse's parametric degrees - the angle on the circle it is a
/// squashed one of, 0 at three o'clock and growing clockwise on the screen, as an HTML canvas measures it. So a sector's
/// share of the area is its share of the turn however wide the ellipse, and sectors of a chart laid one after another
/// meet on the same ray. Any start is taken round the turn; the sweep's sign is its way round, negative counterclockwise.
/// A sweep past a turn goes round and on: what it covers is what is left over whole turns, 485 the arc of 125 - and a
/// whole number of turns, 720 as 360, is the whole ellipse.</summary>
public readonly struct ArcSpan : IEquatable<ArcSpan>
{
    private const double Turn = 360;

    public ArcSpan(double start, double sweep)
    {
        Start = double.IsFinite(start) ? Round(start) : 0;
        Sweep = Reach(sweep);
    }

    // What is left of a sweep over whole turns, its sign kept; whole turns, or a sweep with no end, are a turn. Left over
    // by no more than rounding - a ring summed from its shares, 360.00000000000006 - is whole turns too, not a sliver.
    private static double Reach(double sweep)
    {
        if (double.IsNaN(sweep)) return 0;
        if (Math.Abs(sweep) <= Turn) return sweep;
        if (double.IsInfinity(sweep)) return Math.CopySign(Turn, sweep);
        var left = sweep % Turn;
        return Math.Abs(left) <= RoundingLeft ? Math.CopySign(Turn, sweep) : left;
    }

    // Degrees past whole turns that are rounding, not an arc anybody meant.
    private const double RoundingLeft = 1e-9;

    /// <summary>Where the arc starts, in [0, 360).</summary>
    public double Start { get; }

    /// <summary>How far it goes from <see cref="Start"/>, in [-360, 360]: negative counterclockwise.</summary>
    public double Sweep { get; }

    /// <summary>Whether it goes all the way round: the whole ellipse, its start and way round only where a stroke's dashes
    /// and trimmed ends begin and go.</summary>
    public bool IsWhole => Math.Abs(Sweep) >= Turn;

    /// <summary>Whether it covers nothing.</summary>
    public bool IsEmpty => Sweep == 0;

    /// <summary>The same part of the ellipse gone round clockwise: what a fill, a cut or a hit covers, none of which cares
    /// which way it was swept. Only a stroke's dashes and ends follow the way round.</summary>
    public ArcSpan Clockwise => Sweep >= 0 ? this : new ArcSpan(Start + Sweep, -Sweep);

    /// <summary>Whether the parametric angle <paramref name="degrees"/> lies on the arc, its ends included.</summary>
    public bool Holds(double degrees)
    {
        if (IsWhole)
        {
            return true;
        }

        var clockwise = Clockwise;
        return Round(degrees - clockwise.Start) <= clockwise.Sweep;
    }

    // Taken round the turn into [0, 360).
    private static double Round(double degrees)
    {
        var round = degrees - Turn * Math.Floor(degrees / Turn);
        return round >= Turn ? 0 : round;
    }

    public bool Equals(ArcSpan other) => Start.Equals(other.Start) && Sweep.Equals(other.Sweep);

    public override bool Equals(object obj) => obj is ArcSpan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Start, Sweep);

    public override string ToString() => $"from {Start} by {Sweep}";
}
