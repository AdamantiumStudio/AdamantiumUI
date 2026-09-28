using System.Runtime.InteropServices;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.RenderUnits;

// A stroke vertex: position plus one float4 per end of its piece, Cap0 = (perp, uA, vA, arcA) and Cap1 = (caps, uB, vB,
// arcB), so the shader carves caps analytically; caps packs both cap codes base-8.
[StructLayout(LayoutKind.Sequential)]
internal struct StrokeVertex
{
    [VertexInputElement("POSITION")] public Vector2F Position;
    [VertexInputElement("TEXCOORD0")] public Vector4F Cap0;
    [VertexInputElement("TEXCOORD1")] public Vector4F Cap1;

    // The ribbon piece id, breaking ties in max-coverage overlap resolution; its own attribute, since packing would lose
    // precision in interpolation.
    [VertexInputElement("TEXCOORD2")] public float PieceId;
}
