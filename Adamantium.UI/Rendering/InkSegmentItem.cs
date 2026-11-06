using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

/// <summary>
/// One record of ink (see InkEffect.fx): one per point of a stroke, each drawing its own segment's box. A fragment keeps
/// a pixel only when its segment is the nearest of the whole polyline, so overlaps blend once.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InkSegmentItem
{
    /// <summary>.xy the point, .zw the next one - node-local.</summary>
    public Vector4F Segment;

    /// <summary>.x the wider half width of the segment's two ends, in node-local units - what its quad has to reach; .y
    /// transform slot; .z opacity slot, or -1; .w which segment this is, counted from one, or 0 for a record that draws
    /// nothing.</summary>
    public Vector4F Params;

    /// <summary>.x the ancestor's rounded-clip slot, or -1; .y how many points; .z how far back the first of them is -
    /// RELATIVE, because the draw bases the buffer at the run it flushes; .w the half width at this point, in node-local
    /// units.</summary>
    public Vector4F Clip;

    /// <summary>Straight RGBA, opacity folded into the alpha.</summary>
    public Vector4F Color;

    /// <summary>.x the least half width, in device pixels, whatever the node's scale; 0 for none.</summary>
    public Vector4F Floor;
}
