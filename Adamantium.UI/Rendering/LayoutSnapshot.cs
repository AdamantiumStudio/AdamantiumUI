using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Rendering;

// A component's frozen per-frame layout state, the only layout the draw path reads, so it can run on the render thread
// while layout mutates the tree. Recorded on the update thread and sent as a delta (RenderPacket.SnapDelta).
internal readonly struct LayoutSnapshot(
    Matrix4x4F localTransform,
    Size renderSize,
    bool clipToBounds,
    bool isMotionNode,
    IUIComponent renderParent,
    float opacity = 1f,
    float selfOpacity = 1f,
    Vector4F clipRadii = default) : System.IEquatable<LayoutSnapshot>
{
    // A FIELD, not a get-only property: 64 bytes that Equals wants to compare by reference, and a property cannot be
    // passed as `in` (it is not addressable, so the compiler copies it first - the very copy this avoids).
    public readonly Matrix4x4F LocalTransform = localTransform;
    public Size RenderSize { get; } = renderSize;
    public bool ClipToBounds { get; } = clipToBounds;
    public bool IsMotionNode { get; } = isMotionNode;

    /// <summary>How this clip's own corners are rounded, in the CornerRadius order (TL, TR, BR, BL). Zero = a square
    /// clip, which is what a scissor already does. Frozen here with the rest, so the draw never reads it live.</summary>
    public Vector4F ClipRadii { get; } = clipRadii;

    /// <summary>The element's OWN opacity - the part that composites DOWN onto descendants. The bake multiplies this up the
    /// <see cref="RenderParent"/> chain (see RenderCache.EffectiveOpacity); frozen here so the draw never reads the live
    /// property.</summary>
    public float Opacity { get; } = opacity;

    /// <summary>The element's SelfOpacity - fades only its own draws, NOT composited onto descendants.</summary>
    public float SelfOpacity { get; } = selfOpacity;

    /// <summary>The component this one is composed ON TOP OF - <see cref="IUIComponent.RenderParent"/>, i.e. the visual
    /// parent for everything but an adorner (which draws in its adorned element's space, not in the visual tree).</summary>
    public IUIComponent RenderParent { get; } = renderParent;

    // Exact field-by-field: the default would use reflection, and the matrix's tolerant == would drop small moves.
    public bool Equals(LayoutSnapshot other)
    {
        return ExactlySame(in LocalTransform, in other.LocalTransform)
               && RenderSize.Width == other.RenderSize.Width
               && RenderSize.Height == other.RenderSize.Height
               && ClipToBounds == other.ClipToBounds
               && ClipRadii == other.ClipRadii
               && IsMotionNode == other.IsMotionNode
               && Opacity == other.Opacity
               && SelfOpacity == other.SelfOpacity
               && ReferenceEquals(RenderParent, other.RenderParent);
    }

    // BY REFERENCE: a Matrix4x4F is 64 bytes, and this is asked once per re-frozen component per frame - passing two of
    // them by value copied 128 bytes to compare sixteen floats that usually differ in the first one.
    private static bool ExactlySame(in Matrix4x4F a, in Matrix4x4F b)
    {
        return a.M11 == b.M11 && a.M12 == b.M12 && a.M13 == b.M13 && a.M14 == b.M14
               && a.M21 == b.M21 && a.M22 == b.M22 && a.M23 == b.M23 && a.M24 == b.M24
               && a.M31 == b.M31 && a.M32 == b.M32 && a.M33 == b.M33 && a.M34 == b.M34
               && a.M41 == b.M41 && a.M42 == b.M42 && a.M43 == b.M43 && a.M44 == b.M44;
    }

    public override bool Equals(object obj) => obj is LayoutSnapshot other && Equals(other);

    public override int GetHashCode() => System.HashCode.Combine(RenderSize, ClipToBounds, ClipRadii, IsMotionNode, Opacity, SelfOpacity, RenderParent);
}
