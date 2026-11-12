using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// Two closed geometries combined: their union, intersection, the first less the second, or either but not both
/// (<see cref="GeometryCombineMode"/>). The outlines are laid over each other by <see cref="GeometryOverlay"/>, which
/// takes them crossing, touching or running along one another; each operand's inside is by its own fill rule, and an
/// open operand adds nothing. Points nearer than twice the step coordinates are rounded to
/// (<see cref="MathHelper.Coordinates.Step"/>) are one: two roundings of one point lie at most a diagonal step apart, so
/// operands meet without seams.
/// </summary>
public class CombinedGeometry : Geometry
{
    private Rect bounds;

    public CombinedGeometry()
    {
        IsClosed = true;
    }

    public override Rect Bounds => bounds;

    public override Geometry Clone()
    {
        throw new System.NotImplementedException();
    }

    public static readonly AdamantiumProperty Geometry1Property =
        AdamantiumProperty.Register(nameof(Geometry1), typeof(Geometry), typeof(CombinedGeometry),
            new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender, Geometry1Changed));

    public static readonly AdamantiumProperty Geometry2Property =
        AdamantiumProperty.Register(nameof(Geometry2), typeof(Geometry), typeof(CombinedGeometry),
            new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender, Geometry2Changed));

    public static readonly AdamantiumProperty GeometryCombineModeProperty =
        AdamantiumProperty.Register(nameof(GeometryCombineMode), typeof(GeometryCombineMode), typeof(CombinedGeometry),
            new PropertyMetadata(GeometryCombineMode.Union, PropertyMetadataOptions.AffectsRender, CombineModeChanged));

    private static void Geometry1Changed(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is CombinedGeometry combined)
        {
            // Was empty: Geometry1's own updates and replacement were silently ignored (asymmetric with Geometry2).
            if (e.OldValue is Geometry oldGeometry) oldGeometry.ComponentUpdated -= combined.GeometryOnComponentUpdated;
            if (e.NewValue is Geometry newGeometry) newGeometry.ComponentUpdated += combined.GeometryOnComponentUpdated;
            combined.InvalidateGeometry();
        }
    }

    private static void Geometry2Changed(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is CombinedGeometry combined)
        {
            if (e.OldValue is Geometry oldGeometry) oldGeometry.ComponentUpdated -= combined.GeometryOnComponentUpdated;
            if (e.NewValue is Geometry newGeometry) newGeometry.ComponentUpdated += combined.GeometryOnComponentUpdated;
            combined.InvalidateGeometry();
        }
    }

    private void GeometryOnComponentUpdated(object sender, ComponentUpdatedEventArgs e)
    {
        InvalidateGeometry();
    }

    private static void CombineModeChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        // Changing the combine mode must re-run the boolean op, not reuse the stale mesh.
        if (a is CombinedGeometry combined) combined.InvalidateGeometry();
    }

    public Geometry Geometry1
    {
        get => GetValue<Geometry>(Geometry1Property);
        set => SetValue(Geometry1Property, value);
    }

    public Geometry Geometry2
    {
        get => GetValue<Geometry>(Geometry2Property);
        set => SetValue(Geometry2Property, value);
    }

    public GeometryCombineMode GeometryCombineMode
    {
        get => GetValue<GeometryCombineMode>(GeometryCombineModeProperty);
        set => SetValue(GeometryCombineModeProperty, value);
    }

    public override void RecalculateBounds()
    {
        // TODO: define is this suitable calculation mechanism or we need to fully process geometry first?
        Rect rect = new Rect();
        if (Geometry1 != null)
        {
            Geometry1.RecalculateBounds();
            rect = Geometry1.Bounds;
        }

        if (Geometry2 != null)
        {
            Geometry2.RecalculateBounds();
            rect = rect.Merge(Geometry2.Bounds);
        }

        if (Transform != null)
        {
            var matrix = Transform.Matrix;
            rect = rect.TransformToAABB(matrix);   // was: result discarded (Rect is a value type)
        }

        bounds = rect;
    }

    protected internal override void ProcessGeometryCore(GeometryType geometryType)
    {
        Geometry1?.ProcessGeometry(GeometryType.Outlined);
        Geometry2?.ProcessGeometry(GeometryType.Outlined);

        Mesh.Clear();

        var rings = GeometryOverlay.Combine(AreaOf(Geometry1), AreaOf(Geometry2), ModeOf(GeometryCombineMode), Weld);
        foreach (var ring in rings)
        {
            if (Strokeable(ring)) Mesh.AddContour(ring, true);
        }

        if (geometryType == GeometryType.Outlined) return;

        Mesh.SetPoints(RingFill.Fill(rings, FillRule.NonZero));
    }

    private const double Weld = 2 * MathHelper.Coordinates.Step;

    private static AreaCombineMode ModeOf(GeometryCombineMode mode) => mode switch
    {
        GeometryCombineMode.Union => AreaCombineMode.Union,
        GeometryCombineMode.Intersect => AreaCombineMode.Intersect,
        GeometryCombineMode.Exclude => AreaCombineMode.Exclude,
        _ => AreaCombineMode.Xor,
    };

    private static RingArea AreaOf(Geometry geometry)
    {
        if (geometry is not { IsClosed: true }) return new RingArea([], FillRule.EvenOdd);
        var (rings, rule) = geometry.Area();
        return new RingArea(rings, rule);
    }

    private static bool Strokeable(Vector2<double>[] ring)
    {
        var rounded = new Vector2<double>[ring.Length];
        for (var i = 0; i < ring.Length; i++) rounded[i] = MathHelper.Coordinates.Round(ring[i]);
        var perimeter = 0.0;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++) perimeter += (rounded[i] - rounded[j]).Length();
        return perimeter > 0 && 2 * Math.Abs(SignedArea(rounded)) / perimeter >= MathHelper.Coordinates.Step;
    }

    private static double SignedArea(Vector2<double>[] ring)
    {
        var area = 0.0;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            area += (ring[j].X - ring[i].X) * (ring[j].Y + ring[i].Y);
        return area / 2;
    }
}
