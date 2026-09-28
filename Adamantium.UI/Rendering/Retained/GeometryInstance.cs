using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>
/// One element's instance of a mesh shared per <see cref="GeometryKey"/>: its full world matrix (matching the per-unit
/// path exactly) and color, read by <c>SV_InstanceID</c> (see <see cref="InstancedFillCollector"/>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GeometryInstance
{
    /// <summary>Per-instance transform RELATIVE to the transform-table slot in <see cref="Params"/> (element local space
    /// -> slot space). Row-vector convention. The vertex shader applies the slot matrix on top, so the world is never
    /// stored here and moving the slot's node does not touch this record.</summary>
    public Matrix4x4F Local;

    /// <summary>Straight-alpha RGBA (opacity already folded into the alpha by the producer). Four BYTES - the form the
    /// colour arrived in - read by the shader as a <c>uint8_t4</c>.</summary>
    public Color Color;

    /// <summary>.x = transform-table slot; .y = opacity slot, sent but not yet read (the opacity chain is folded into the
    /// color); .zw spare.</summary>
    public Vector4F Params;

    public static GeometryInstance FromLocal(Matrix4x4F local, Vector4F color, int transformSlot, int fadeSlot,
        int clipSlot = -1) => new()
    {
        // Raw row-major, unlike uniforms: storage buffers read row-major, so transposing would misplace the translation.
        Local = local,
        Color = new Color(color),
        // .z is the ROUNDED CLIP slot: a mesh cannot get an ancestor's rounded corners from its own geometry, and a
        // scissor cannot express them, so the shape comes from the table (-1 = no rounded clip).
        Params = new Vector4F(transformSlot, fadeSlot, clipSlot, 0)
    };
}
