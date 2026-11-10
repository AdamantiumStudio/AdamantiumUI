namespace Adamantium.UI.Sandbox.Controls;

/// <summary>Which group of draws a <see cref="DrawingGroupSample"/> shows.</summary>
public enum DrawingGroupSampleKind
{
    /// <summary>The picture cut to a star, by the nonzero rule.</summary>
    StarClip,

    /// <summary>The picture cut to a star by the even-odd rule: its middle is a hole.</summary>
    EvenOddClip,

    /// <summary>The picture cut to a rectangle: a scissor, no layer.</summary>
    RectangleClip,

    /// <summary>The picture seen through letters.</summary>
    TextClip,

    /// <summary>The picture through a soft mask read by its luminosity: a white spot fading to black.</summary>
    LuminosityMask,

    /// <summary>The picture through a soft mask read by its alpha: a gradient fading out.</summary>
    AlphaMask,

    /// <summary>A group inside a group: cut to a star outside, faded and multiplied inside.</summary>
    Nested,

    /// <summary>Three discs laid over the picture by <see cref="DrawingGroupSample.BlendMode"/>.</summary>
    Blend,
}
