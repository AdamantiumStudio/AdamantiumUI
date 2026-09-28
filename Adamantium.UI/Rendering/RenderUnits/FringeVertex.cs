using System.Runtime.InteropServices;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.RenderUnits;

// A fringe vertex (FringeVert, 24 bytes): contour position plus two edge directions; zero directions lie on the contour,
// others are pushed out 1 device px along the screen-space miter.
[StructLayout(LayoutKind.Sequential)]
internal struct FringeVertex
{
    [VertexInputElement("POSITION")] public Vector2F Position;
    [VertexInputElement("TEXCOORD0")] public Vector2F Dir0;
    [VertexInputElement("TEXCOORD1")] public Vector2F Dir1;
}
