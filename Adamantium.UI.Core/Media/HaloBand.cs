using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>One soft band around or inside an outline: offset, solid spread, fade and color; both <see cref="Aura"/> and
/// <see cref="Shadow"/> bake to it. Immutable, captured at record time.</summary>
public readonly struct HaloBand
{
    public HaloBand(Vector2F offset, float spread, float softness, Vector4F color, bool inner)
    {
        Offset = offset;
        Spread = spread;
        Softness = softness;
        Color = color;
        Inner = inner;
    }

    /// <summary>Where the band is thrown, in logical pixels. Zero for an aura, which has no direction.</summary>
    public Vector2F Offset { get; }

    /// <summary>Pixels of FULL strength past the outline before the fade starts.</summary>
    public float Spread { get; }

    /// <summary>Pixels the band fades out over. Zero would be a hard-edged silhouette, so the bake keeps it above zero.</summary>
    public float Softness { get; }

    /// <summary>Straight-alpha RGBA with the author's Opacity already folded into .w.</summary>
    public Vector4F Color { get; }

    /// <summary>Band drawn INSIDE the outline instead of outside it.</summary>
    public bool Inner { get; }

    /// <summary>How far past the outline this band reaches - what the drawn quad has to be grown by to hold it.</summary>
    public float Reach => Spread + Softness + System.Math.Max(System.Math.Abs(Offset.X), System.Math.Abs(Offset.Y));

    /// <summary>A band with no width. A transparent band is not empty: it keeps its record, so switching it on is a
    /// recolor rather than a structural change.</summary>
    public bool IsEmpty => (Spread + Softness) <= 0.0f;
}
