using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.RenderUnits;

/// <summary>A corner of a colored mesh as the vertex stage reads it by address: where it stands in the mesh's space and
/// its straight color. Matches <c>ColoredMeshVertex</c> in UIBasicEffect.fx.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ColoredMeshVertexRecord
{
    public Vector2F Position;
    public Vector4F Color;
}
