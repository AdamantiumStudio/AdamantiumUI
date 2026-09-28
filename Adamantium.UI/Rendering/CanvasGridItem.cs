using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

/// <summary>One canvas grid instance (GridEffect.fx, pass Grid); colors stay float4 since there is one per draw.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CanvasGridItem
{
    /// <summary>Node-local bounds: x, y, w, h.</summary>
    public Vector4F Bounds;

    /// <summary>.x transform-table slot; .y marks (1 dots, 2 lines); .z opacity slot, or -1; .w mark size in logical px.</summary>
    public Vector4F Params;

    /// <summary>.xy the lattice phase within one cell (logical px), never the pan distance, which float32 cannot hold;
    /// .z screen px per world unit; .w spare.</summary>
    public Vector4F Camera;

    /// <summary>.x the step to draw (world units, ALREADY coarsened); .y the coarsening the accent level is above it;
    /// .z 1 / the pitch a mark must keep on screen, as a RECIPROCAL - the shader must not divide; .w spare.</summary>
    public Vector4F Step;

    /// <summary>.x the ancestor's rounded-clip slot, or -1; .yz where the world's ORIGIN sits, penned in to the
    /// element's reach - the AXES' own place, which a phase cannot say because every cell looks like every other;
    /// .w spare.</summary>
    public Vector4F Clip;

    /// <summary>The GROUND, straight RGBA. Carried by the grid because the grid is flushed first of its clip group -
    /// that is what makes it a ground - and a background drawn separately would then land on top of it.</summary>
    public Vector4F Background;

    /// <summary>Straight RGBA, opacity folded into the alpha.</summary>
    public Vector4F GridColor;

    /// <summary>Straight RGBA; alpha 0 leaves the world's axes undrawn.</summary>
    public Vector4F AxisColor;
}
