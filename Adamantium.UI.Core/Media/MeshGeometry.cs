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
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, CollectionChangedCallback)
        {
            WriteCheckCallback = StaysFrozen(mesh => mesh.Positions)
        });

    public static readonly AdamantiumProperty TriangleIndicesProperty = AdamantiumProperty.Register(
        nameof(TriangleIndices), typeof(IndexCollection), typeof(MeshGeometry),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, CollectionChangedCallback)
        {
            WriteCheckCallback = StaysFrozen(mesh => mesh.TriangleIndices)
        });

    public static readonly AdamantiumProperty BoundaryProperty = AdamantiumProperty.Register(nameof(Boundary),
        typeof(ContourCollection), typeof(MeshGeometry),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, CollectionChangedCallback)
        {
            WriteCheckCallback = StaysFrozen(mesh => mesh.Boundary)
        });

    public static readonly AdamantiumProperty CutBoxProperty = AdamantiumProperty.Register(nameof(CutBox),
        typeof(Rect), typeof(MeshGeometry),
        new PropertyMetadata(Rect.Empty, PropertyMetadataOptions.AffectsRender, CutBoxChangedCallback)
        {
            WriteCheckCallback = StaysFrozen(mesh => mesh.CutBox)
        });

    private const string FrozenStaysAsItIs = "A frozen mesh does not change: it has let go of what it was made of.";

    private Rect _bounds;
    private bool _boundsStale = true;
    private bool _hasBoundary;
    private bool _freezing;

    public MeshGeometry()
    {
    }

    // Refused before it is written, so a frozen mesh keeps nothing of it; only the value it has, written again - a
    // binding refreshed - is let through. A value cleared is refused too: what lay under it would come through.
    private static WriteCheckCallback StaysFrozen(Func<MeshGeometry, object> now) => (a, value) =>
    {
        if (a is MeshGeometry { IsFrozen: true } mesh &&
            (value == AdamantiumProperty.UnsetValue || !Equals(value, now(mesh))))
        {
            throw new InvalidOperationException(FrozenStaysAsItIs);
        }
    };

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

    /// <summary>The box the area was cut to when it is a piece of a larger one - a map tile's square: the
    /// <see cref="Boundary"/> runs along its sides where the area was cut, and that edge is not antialiased, so pieces
    /// cut side by side meet without a seam. Empty when the whole boundary is the area's own edge.</summary>
    public Rect CutBox
    {
        get => GetValue<Rect>(CutBoxProperty);
        set => SetValue(CutBoxProperty, value);
    }

    protected internal override Rect? FillCutBox => CutBox.IsEmpty ? null : CutBox;

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

    /// <summary>Whether <see cref="Freeze"/> has let go of what the area was given as.</summary>
    public bool IsFrozen { get; private set; }

    protected override bool RefusesChange => IsFrozen;

    /// <summary>Lets go of what the area was given as - <see cref="Positions"/>, <see cref="TriangleIndices"/>,
    /// <see cref="Boundary"/> and, when it has a boundary, the points and indices of its <see cref="Geometry.Mesh"/> -
    /// keeping only what is drawn, made ready first if it was not: an area that will not change - a map tile's - then
    /// weighs about half. Its
    /// edge stays, for its antialiasing and a pen. Changing it afterwards, or making a copy, throws; it is drawn on its
    /// own, not inside a <see cref="GeometryGroup"/> or a <see cref="CombinedGeometry"/>.</summary>
    public void Freeze()
    {
        if (IsFrozen)
        {
            return;
        }

        ProcessGeometry(GeometryType.Both);
        _freezing = true;
        try
        {
            Positions = null;
            TriangleIndices = null;
            Boundary = null;
        }
        finally
        {
            _freezing = false;
        }

        IsFrozen = true;

        // Without a boundary the fill is not kept ready but made from the mesh when drawn: then the mesh stays.
        if (ContoursAreFillBoundary)
        {
            Mesh.SetPoints(Array.Empty<Vector3<double>>(), updateBoundingBox: false);
            Mesh.SetIndices(Array.Empty<int>());
        }
    }

    public override Geometry Clone() => IsFrozen
        ? throw new InvalidOperationException("A frozen mesh has let go of what it was made of; it is not copied.")
        : new MeshGeometry(Positions ?? [], TriangleIndices ?? [], Boundary) { Transform = Transform, CutBox = CutBox };

    /// <summary>True when the last processing put at least one ring of the <see cref="Boundary"/> - three distinct
    /// points or more - into the mesh; else the edge is worked out of the triangles.</summary>
    protected internal override bool ContoursAreFillBoundary => _hasBoundary;

    public override void RecalculateBounds()
    {
        _boundsStale = false;
        if (IsFrozen)
        {
            return;
        }

        var bounds = Rect.Empty;
        var any = false;
        if (Positions != null)
        {
            Rect.Include(ref bounds, ref any, Positions);
        }

        if (Boundary != null)
        {
            foreach (var ring in Boundary)
            {
                if (ring != null)
                {
                    Rect.Include(ref bounds, ref any, ring);
                }
            }
        }

        _bounds = any && Transform != null ? bounds.TransformToAABB(Transform.Matrix) : bounds;
    }

    // One snapshot of each collection: what is checked is what is drawn, even if a collection changes meanwhile.
    protected internal override void ProcessGeometryCore(GeometryType geometryType)
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException(FrozenStaysAsItIs);
        }

        var positions = Corners(Positions);
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

    private static Vector3<double>[] Corners(PointsCollection positions)
    {
        if (positions == null)
        {
            return null;
        }

        lock (positions.SyncRoot)
        {
            var points = positions.AsSpan();
            var corners = new Vector3<double>[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                corners[i] = (Vector3<double>)points[i];
            }

            return corners;
        }
    }

    private static bool IsDrawable(Vector3<double>[] positions, int[] indices)
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

    private static void CutBoxChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is MeshGeometry mesh)
        {
            mesh.RaiseComponentUpdated();
        }
    }

    private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseComponentUpdated();
    }

    // Marked, not recomputed: a collection filled point by point would otherwise walk all of it on every point. Comes
    // for a changed collection and a changed transform alike. Letting go while freezing changes nothing that is drawn;
    // a change once frozen - a value of its transform's - is refused where it is made.
    protected override void OnComponentUpdated()
    {
        if (_freezing)
        {
            return;
        }

        if (IsFrozen)
        {
            throw new InvalidOperationException(FrozenStaysAsItIs);
        }

        base.OnComponentUpdated();
        _boundsStale = true;
    }

    /// <summary>Empty when nothing is drawn; a frozen mesh is drawn from the fill it kept ready.</summary>
    public override bool IsEmpty() => IsFrozen && ContoursAreFillBoundary ? FillForDrawing == null : base.IsEmpty();
}
