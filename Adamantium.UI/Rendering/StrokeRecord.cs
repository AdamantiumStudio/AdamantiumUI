using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Rendering;

/// <summary>
/// One stroke of a <see cref="StrokeBatchCollector"/> batch, as <c>StrokeEffect.fx</c> reads it: where its points and
/// tables are, its pen and color, and where it stands - its local matrix on top of its transform slot. Every group is 16
/// bytes, so the layout is the same under any packing rule the shader is compiled with.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct StrokeRecord
{
    public const uint Closed = 1;
    public const uint BatchedContours = 2;
    public const uint HasRuns = 4;
    public const uint WindowThickness = 8;
    public const uint Bends = 16;

    public Matrix4x4F Local;
    public ulong Points;

    /// <summary>Each point's piece's control; 0 when every piece is straight.</summary>
    public ulong Controls;
    public ulong Arcs;
    public ulong Contours;
    public ulong PointContours;
    public ulong Pattern;
    public uint PointCount;
    public uint PatternCount;
    public uint Flags;
    public uint Caps;
    public uint RoundSegments;
    public uint JoinType;
    private uint _pad0;
    private uint _pad1;

    /// <summary>.x thickness, .y least device thickness (0 none), .z dash offset, .w piece salt.</summary>
    public Vector4F Pen;

    /// <summary>.x trim start, .y trim end, .z transform slot, .w opacity slot (-1 none).</summary>
    public Vector4F Window;

    /// <summary>Straight RGBA, the element's own alpha folded in.</summary>
    public Vector4F Color;

    /// <summary>.x rounded clip slot (-1 none), .y the owner it is described from (see StrokeBatchCollector).</summary>
    public Vector4F Extra;

    /// <summary>How many vertices its draw makes: a segment quad and a join fan a point.</summary>
    public readonly uint VertexCount => PointCount * (6u + RoundSegments * 3u);
}
