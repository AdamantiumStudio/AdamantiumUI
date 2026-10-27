using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

internal readonly record struct LayerMask(
    float Type,
    Vector4F Geom0,
    Vector4F Geom1,
    Vector4F U,
    Vector4F V,
    Vector4F W,
    Vector4F Alpha0,
    Vector4F Alpha1,
    Vector4F Offsets0,
    Vector4F Offsets1,
    Vector4F Params);

/// <summary>A reflection in window device pixels: mirrored about the floor halfway down <see cref="OffsetY"/> under
/// <see cref="Bottom"/>, moved <see cref="OffsetX"/> sideways, showing <see cref="Length"/> rows of which the last
/// <see cref="FadeLength"/> fade away.</summary>
internal readonly record struct LayerMirror(bool Shows, float Bottom, float OffsetX, float OffsetY, float Length,
    float FadeLength, float Opacity);
