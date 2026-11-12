using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Curves;
using Adamantium.UI.Core.Media.Fill;

namespace Adamantium.UI.Rendering.Retained;

// The fill the instanced path draws, as the geometry prepared it - its curves, and its triangles (PreparedFill) when it
// has any, never the live Mesh - plus the keys identical fills merge under.
internal sealed class FrozenMesh
{
    private readonly PreparedFill _fill;
    private readonly PathCurves _outline;
    private List<(Vector2<double>[] Points, bool IsClosed)> _loops;

    private FrozenMesh(PreparedFill fill, CurveFill curves, PathCurves outline, Rect bounds)
    {
        _fill = fill;
        _outline = outline;
        Curves = curves;
        Bounds = bounds;
        CurveKey = curves == null ? default : GeometryKey.CurveFill(curves.Fingerprint);
        Key = fill != null ? GeometryKey.ArbitraryMesh(fill.Fingerprint) : CurveKey;
    }

    /// <summary>The fill as curves for the curve fill shader - exact at any zoom - or null when the geometry encloses
    /// nothing or curves are switched off.</summary>
    public CurveFill Curves { get; }

    /// <summary>What identical curve fills merge under.</summary>
    public GeometryKey CurveKey { get; }

    /// <summary>The triangles; empty for a fill made from its curves alone.</summary>
    public UIVertex[] Vertices => _fill?.Vertices ?? [];

    public int[] Indices => _fill?.Indices ?? [];

    public PrimitiveType Topology => _fill?.Topology ?? PrimitiveType.TriangleList;

    /// <summary>The geometry's local bounds: the gradient shader maps a fragment to a 0..1 uv across them.</summary>
    public Rect Bounds { get; }

    /// <summary>What identical triangle fills merge under; for a fill of curves alone, <see cref="CurveKey"/>.</summary>
    public GeometryKey Key { get; }

    /// <summary>Whether there is anything to draw: triangles or curves.</summary>
    public bool HasPoints => Vertices.Length > 0 || Curves != null;

    /// <summary>The analytic-AA ring for the triangles: the same edges every instance of it needs, so the collector
    /// uploads ONE of them per key and draws it instanced. Empty when there are no triangles or no closed boundary.
    /// </summary>
    public RingEdge[] Ring => _fill?.Ring ?? [];

    /// <summary>The fill's closed boundary loops in LOCAL space - the triangles' edge, or else the filled figures of its
    /// curves - for the distance field a halo bakes; made on first use.</summary>
    public List<(Vector2<double>[] Points, bool IsClosed)> Loops => _loops ??= _fill?.Loops ?? _outline?.FillLoops();

    /// <summary>The fill of a processed geometry as the geometry keeps it ready. Null if it has nothing to draw.</summary>
    public static FrozenMesh From(Core.Media.Geometry geometry)
    {
        var fill = geometry.FillForDrawing;
        var outline = InstancedFillCollector.CurvesEnabled ? geometry.CurvesForDrawing : null;
        var curves = outline != null ? CurveFill.From(outline) : null;
        if (fill == null && curves == null)
        {
            return null;
        }

        return new FrozenMesh(fill, curves, outline, fill?.Bounds ?? geometry.Bounds);
    }
}
