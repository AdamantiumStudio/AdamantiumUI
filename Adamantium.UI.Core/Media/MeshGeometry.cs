using System.Collections.Specialized;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// An area given as triangles already cut - by a tessellator on another thread, a file, a generator: the
/// <see cref="Positions"/> of their corners and, three to a triangle, the <see cref="TriangleIndices"/> into them. It is
/// drawn as it is and never cut again, so a great many shapes cost the drawing thread nothing to tessellate; its edge is
/// antialiased like any fill's. Whoever cut it usually knows its edge - the rings it was cut from - and gives it as the
/// <see cref="Boundary"/>: the renderer then takes it as it is instead of working it out of the triangles, and a pen
/// draws it. Without one, the edge is found from the triangles and a pen draws nothing. A mesh may be made on any
/// thread; once shown, it is changed on the UI thread only.
/// </summary>
public class MeshGeometry : Geometry
{
    public static readonly AdamantiumProperty PositionsProperty = AdamantiumProperty.Register(nameof(Positions),
        typeof(PointsCollection), typeof(MeshGeometry),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, CollectionChangedCallback));

    public static readonly AdamantiumProperty TriangleIndicesProperty = AdamantiumProperty.Register(
        nameof(TriangleIndices), typeof(IndexCollection), typeof(MeshGeometry),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, CollectionChangedCallback));

    public static readonly AdamantiumProperty BoundaryProperty = AdamantiumProperty.Register(nameof(Boundary),
        typeof(ContourCollection), typeof(MeshGeometry),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, CollectionChangedCallback));

    private Rect _bounds;
    private bool _boundsStale = true;
    private bool _hasBoundary;

    public MeshGeometry()
    {
    }

    /// <summary>A mesh of <paramref name="positions"/> and, three to a triangle, <paramref name="triangleIndices"/>,
    /// with the closed rings of its edge, if known, as <paramref name="boundary"/>.</summary>
    public MeshGeometry(IEnumerable<Vector2<double>> positions, IEnumerable<int> triangleIndices,
        IEnumerable<IEnumerable<Vector2<double>>> boundary = null)
    {
        Positions = new PointsCollection(positions);
        TriangleIndices = new IndexCollection(triangleIndices);
        if (boundary != null)
        {
            Boundary = new ContourCollection(boundary.Select(ring => new PointsCollection(ring)));
        }

        RecalculateBounds();
    }

    /// <summary>The corners of the triangles.</summary>
    public PointsCollection Positions
    {
        get => GetValue<PointsCollection>(PositionsProperty);
        set => SetValue(PositionsProperty, value);
    }

    /// <summary>Three indices into <see cref="Positions"/> to a triangle; a count not a multiple of three, or an index
    /// past the positions, draws nothing.</summary>
    public IndexCollection TriangleIndices
    {
        get => GetValue<IndexCollection>(TriangleIndicesProperty);
        set => SetValue(TriangleIndicesProperty, value);
    }

    /// <summary>The closed rings of the area's edge - outer rings and holes - when whoever cut the triangles knows them;
    /// a ring is changed by putting a new one in its place.</summary>
    public ContourCollection Boundary
    {
        get => GetValue<ContourCollection>(BoundaryProperty);
        set => SetValue(BoundaryProperty, value);
    }

    /// <summary>The box round the positions and the boundary, transformed by <see cref="Geometry.Transform"/>.</summary>
    public override Rect Bounds
    {
        get
        {
            if (_boundsStale)
            {
                RecalculateBounds();
            }

            return _bounds;
        }
    }

    public override Geometry Clone() =>
        new MeshGeometry(Positions ?? [], TriangleIndices ?? [], Boundary) { Transform = Transform };

    /// <summary>True when the last processing put at least one ring of the <see cref="Boundary"/> - three distinct
    /// points or more - into the mesh; else the edge is worked out of the triangles.</summary>
    protected internal override bool ContoursAreFillBoundary => _hasBoundary;

    public override void RecalculateBounds()
    {
        _boundsStale = false;
        var points = new List<Vector2<double>>();
        if (Positions != null)
        {
            points.AddRange(Positions);
        }

        if (Boundary != null)
        {
            foreach (var ring in Boundary)
            {
                if (ring != null)
                {
                    points.AddRange(ring);
                }
            }
        }

        _bounds = points.Count > 0 ? Rect.FromPoints(points) : Rect.Empty;
        if (Transform != null && points.Count > 0)
        {
            _bounds = _bounds.TransformToAABB(Transform.Matrix);
        }
    }

    // One snapshot of each collection: what is checked is what is drawn, even if a collection changes meanwhile.
    protected internal override void ProcessGeometryCore(GeometryType geometryType)
    {
        var positions = Positions?.ToArray();
        var indices = TriangleIndices?.ToArray();
        var rings = Boundary?.Select(ring => ring?.ToArray()).ToArray();
        _hasBoundary = false;
        Mesh.Clear();
        if (!IsDrawable(positions, indices))
        {
            return;
        }

        foreach (var ring in rings ?? [])
        {
            if (ring is not { Length: >= 3 })
            {
                continue;
            }

            Mesh.AddContour(ring, isGeometryClosed: true, generateSegments: false);
            _hasBoundary |= Mesh.Contours[^1].Points is { Length: >= 3 };
        }

        if (geometryType != GeometryType.Outlined)
        {
            Mesh.SetPoints(positions);
            Mesh.SetIndices(indices);
        }
    }

    private static bool IsDrawable(Vector2<double>[] positions, int[] indices)
    {
        if (positions is not { Length: >= 3 } || indices is not { Length: >= 3 } || indices.Length % 3 != 0)
        {
            return false;
        }

        foreach (var index in indices)
        {
            if ((uint)index >= (uint)positions.Length)
            {
                return false;
            }
        }

        return true;
    }

    private static void CollectionChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not MeshGeometry mesh)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged old)
        {
            old.CollectionChanged -= mesh.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged now)
        {
            now.CollectionChanged += mesh.OnCollectionChanged;
        }

        mesh.RaiseComponentUpdated();
    }

    private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseComponentUpdated();
    }

    // Marked, not recomputed: a collection filled point by point would otherwise walk all of it on every point. Comes
    // for a changed collection and a changed transform alike.
    protected override void OnComponentUpdated()
    {
        base.OnComponentUpdated();
        _boundsStale = true;
    }
}
