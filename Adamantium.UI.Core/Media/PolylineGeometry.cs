using System.Collections.Specialized;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Collections;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// Lines given as points - open <see cref="Lines"/> and closed <see cref="Rings"/> - for a pen to stroke: no area, nothing
/// cut into triangles, and a contour costs its points and little more, so thousands of them - a map's roads - weigh
/// what their points do. Many contours of one pen are stroked as one batch. A geometry may be made on any thread; once
/// shown, it is changed on the UI thread only.
/// </summary>
public class PolylineGeometry : Geometry
{
    public static readonly AdamantiumProperty LinesProperty = AdamantiumProperty.Register(nameof(Lines),
        typeof(ContourCollection), typeof(PolylineGeometry),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, CollectionChangedCallback));

    public static readonly AdamantiumProperty RingsProperty = AdamantiumProperty.Register(nameof(Rings),
        typeof(ContourCollection), typeof(PolylineGeometry),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsMeasure, CollectionChangedCallback));

    private Rect _bounds;
    private bool _boundsStale = true;

    static PolylineGeometry()
    {
        IsClosedProperty.OverrideMetadata(typeof(PolylineGeometry), new PropertyMetadata(false));
    }

    public PolylineGeometry()
    {
    }

    /// <summary>Open <paramref name="lines"/> and closed <paramref name="rings"/>, each of its points.</summary>
    public PolylineGeometry(IEnumerable<IEnumerable<Vector2<double>>> lines,
        IEnumerable<IEnumerable<Vector2<double>>> rings = null)
    {
        if (lines != null)
        {
            Lines = new ContourCollection(lines.Select(line => new PointsCollection(line)));
        }

        if (rings != null)
        {
            Rings = new ContourCollection(rings.Select(ring => new PointsCollection(ring)));
        }
    }

    /// <summary>Open lines, each of two points or more; a line is changed by putting a new one in its place.</summary>
    public ContourCollection Lines
    {
        get => GetValue<ContourCollection>(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    /// <summary>Closed rings, each of three points or more - the last joined back to the first.</summary>
    public ContourCollection Rings
    {
        get => GetValue<ContourCollection>(RingsProperty);
        set => SetValue(RingsProperty, value);
    }

    /// <summary>The box round every point that is stroked, transformed by <see cref="Geometry.Transform"/>.</summary>
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

    public override Geometry Clone() => new PolylineGeometry(Lines, Rings) { Transform = Transform };

    public override void RecalculateBounds()
    {
        _boundsStale = false;
        var bounds = Rect.Empty;
        var any = false;
        Include(Lines, 2, ref bounds, ref any);
        Include(Rings, 3, ref bounds, ref any);
        _bounds = any && Transform != null ? bounds.TransformToAABB(Transform.Matrix) : bounds;
    }

    private static void Include(ContourCollection contours, int least, ref Rect bounds, ref bool any)
    {
        if (contours == null)
        {
            return;
        }

        foreach (var contour in contours)
        {
            if (contour == null)
            {
                continue;
            }

            lock (contour.SyncRoot)
            {
                if (contour.Count >= least)
                {
                    Rect.Include(ref bounds, ref any, contour);
                }
            }
        }
    }

    protected internal override bool ContoursAreStrokePaths => true;

    // One snapshot of each collection: what is checked is what is stroked, even if a collection changes meanwhile.
    protected internal override void ProcessGeometryCore(GeometryType geometryType)
    {
        Mesh.Clear();
        foreach (var line in Stroked(Lines, 2))
        {
            Mesh.AddContour(line, isGeometryClosed: false, generateSegments: false);
        }

        foreach (var ring in Stroked(Rings, 3))
        {
            Mesh.AddContour(ring, isGeometryClosed: true, generateSegments: false);
        }
    }

    protected override void OnComponentUpdated()
    {
        base.OnComponentUpdated();
        _boundsStale = true;
    }

    private static IEnumerable<Vector2<double>[]> Stroked(ContourCollection contours, int least) =>
        contours?.Where(contour => contour != null).Select(contour => contour.ToArray())
            .Where(points => points.Length >= least) ?? [];

    private static void CollectionChangedCallback(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is not PolylineGeometry geometry)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged old)
        {
            old.CollectionChanged -= geometry.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged now)
        {
            now.CollectionChanged += geometry.OnCollectionChanged;
        }

        geometry.RaiseComponentUpdated();
    }

    private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseComponentUpdated();
    }
}
