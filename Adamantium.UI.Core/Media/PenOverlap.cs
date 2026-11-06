namespace Adamantium.UI.Core.Media;

/// <summary>How a stroke paints where it crosses itself.</summary>
public enum PenOverlap
{
    /// <summary>Chosen by the brush: a stroke that shows through takes <see cref="Union"/>, an opaque one
    /// <see cref="Blend"/>. A picture is never known to be opaque, so it takes <see cref="Union"/>.</summary>
    Auto,

    /// <summary>One pass: where the stroke crosses itself, a translucent brush is laid on twice and shows darker.</summary>
    Blend,

    /// <summary>Two passes: every pixel of the stroke is painted once, however often it crosses itself.</summary>
    Union,
}
