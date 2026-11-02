using System.Runtime.InteropServices;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.RenderUnits;

// A stroke vertex as its point on the stroke's axis and what the width it was written at moved it by from there: the
// shader scales the move to the width drawn, so a stroke whose width or feather changes is not written again. Cap0 =
// (perp, uA, vA, arcA) and Cap1 = (caps, uB, vB, arcB) - the moves, but caps, which packs both cap codes base-8 - and
// the anchor's (uA, vA, arcA, arcB) and (uB, vB, the width written at), so the shader carves caps analytically.
[StructLayout(LayoutKind.Sequential)]
internal struct StrokeVertex
{
    [VertexInputElement("POSITION")] public Vector2F Offset;
    [VertexInputElement("TEXCOORD0")] public Vector4F Cap0;
    [VertexInputElement("TEXCOORD1")] public Vector4F Cap1;

    // The ribbon piece id, breaking ties in max-coverage overlap resolution; its own attribute, since packing would lose
    // precision in interpolation.
    [VertexInputElement("TEXCOORD2")] public float PieceId;

    [VertexInputElement("TEXCOORD3")] public Vector2F Anchor;
    [VertexInputElement("TEXCOORD4")] public Vector4F AnchorA;
    [VertexInputElement("TEXCOORD5")] public Vector3F AnchorB;
}
