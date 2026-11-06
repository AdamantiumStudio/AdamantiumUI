namespace Adamantium.UI.Core.Media.Animation;

/// <summary>What makes a release a throw, for every kind of thrown value - <see cref="PanInertia"/>'s position and
/// <see cref="FlingValue"/>'s number - so a pan and a pinch read a hand the same way.</summary>
internal static class Flick
{
    /// <summary>How long the hand may rest before lifting and still throw, in milliseconds.</summary>
    public const uint RestMilliseconds = 100;

    /// <summary>How much of the speed measured so far a new move keeps; the rest is the new move's own.</summary>
    public const double KeptOfLast = 0.4;
}
