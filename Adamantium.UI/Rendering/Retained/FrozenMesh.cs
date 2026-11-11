using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Fill;

namespace Adamantium.UI.Rendering.Retained;

// The fill the instanced path draws, as the geometry prepared it (PreparedFill) - never the live Mesh - plus the key
// identical fills merge under.
internal sealed class FrozenMesh
{
    private readonly PreparedFill _fill;

    private FrozenMesh(PreparedFill fill, CurveFill curves)
    {
        _fill = fill;
        Curves = curves;
        Key = GeometryKey.ArbitraryMesh(fill.Fingerprint);
        CurveKey = curves == null ? default : GeometryKey.CurveFill(curves.Fingerprint);
    }

    /// <summary>The fill as curves for the curve fill shader - exact at any zoom - or null when the geometry encloses
    /// nothing or curves are switched off.</summary>
    public CurveFill Curves { get; }

    /// <summary>What identical curve fills merge under.</summary>
    public GeometryKey CurveKey { get; }

    public UIVertex[] Vertices => _fill.Vertices;
    public int[] Indices => _fill.Indices;
    public PrimitiveType Topology => _fill.Topology;
    public Rect Bounds => _fill.Bounds;          // the GEOMETRY's local bounds (the gradient shader maps a fragment to a 0..1 uv)
    public GeometryKey Key { get; }
    public bool HasPoints => Vertices.Length > 0;

    /// <summary>The analytic-AA ring for this mesh: the same edges every instance of it needs, so the collector uploads
    /// ONE of them per key and draws it instanced instead of one fringe draw per element. Empty when the mesh has no
    /// closed boundary to feather.</summary>
    public RingEdge[] Ring => _fill.Ring;

    /// <summary>The mesh's closed boundary loops in LOCAL space - the same ones the ring is built from, kept because a
    /// halo needs a distance field baked from them.</summary>
    public List<(Vector2<double>[] Points, bool IsClosed)> Loops => _fill.Loops;

    /// <summary>The fill of a processed geometry as the geometry keeps it ready. Null if it has no drawable vertices.</summary>
    public static FrozenMesh From(Core.Media.Geometry geometry) =>
        geometry.FillForDrawing is { } fill
            ? new FrozenMesh(fill, InstancedFillCollector.CurvesEnabled ? CurveFill.From(geometry.CurvesForDrawing) : null)
            : null;
}
