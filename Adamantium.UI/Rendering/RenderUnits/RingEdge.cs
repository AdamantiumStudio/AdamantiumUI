using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering.RenderUnits;

// One edge of an analytic-AA ring (40 bytes): its two ends and the directions of the edge before it, itself and the one
// after it, Winding folded into their signs. The vertex stage makes the edge's six corners from it (Ring.fxh).
[StructLayout(LayoutKind.Sequential)]
internal struct RingEdge
{
    public Vector2F A;
    public Vector2F B;
    public Vector2F InDir;
    public Vector2F EdgeDir;
    public Vector2F OutDir;
}
