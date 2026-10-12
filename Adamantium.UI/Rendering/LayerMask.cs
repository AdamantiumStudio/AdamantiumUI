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
