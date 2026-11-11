using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.RoutedEvents;
using Polygon = Adamantium.Mathematics.Triangulation.Polygon;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// Two closed geometries combined: their union, intersection, the first less the second, or either but not both
/// (<see cref="GeometryCombineMode"/>). Outlines that do not cross are combined by how they nest; outlines that cross,
/// touch or run along one another are laid over each other (<see cref="GeometryOverlay"/>), the result's outline the
/// edges with the area on one side only. Each operand's inside is by the even-odd rule; an open operand adds nothing.
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

        var first = AreaOf(Geometry1);
        var second = AreaOf(Geometry2);

        // Fast path for non-crossing input (nested or disjoint): containment and mode decide the result, skipping the
        // overlay of every edge against every other.
        if (TryFastCombine(first, second, geometryType)) return;

        var rings = GeometryOverlay.Combine(first, second, ModeFill);
        foreach (var ring in rings)
        {
            if (Strokeable(ring)) Mesh.AddContour(ring, true);
        }

        if (geometryType == GeometryType.Outlined) return;

        Mesh.SetPoints(Fill(rings));
    }

    private static bool Strokeable(Vector2<double>[] ring)
    {
        // A contour keeps five decimals: a sliver narrower than that on average would collapse into its own sides.
        var rounded = new Vector2<double>[ring.Length];
        for (var i = 0; i < ring.Length; i++) rounded[i] = Vector2<double>.Round(ring[i], MeshContour.CoordinatePrecision);
        var perimeter = 0.0;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++) perimeter += (rounded[i] - rounded[j]).Length();
        var precision = Math.Pow(10, -MeshContour.CoordinatePrecision);
        return perimeter > 0 && 2 * Math.Abs(SignedArea(rounded)) / perimeter >= precision;
    }

    private const double FillSize = 1000;

    private static List<Vector3<double>> Fill(List<Vector2<double>[]> rings)
    {
        // The rings are simple and cross nowhere, filled side on their left: each outer ring with the holes straight
        // inside it is a face, its ears clipped exactly as the rings lie. A face the clipping does not cut as a polygon is
        // cut into its regions instead, at a size the region cutter's fixed tolerances suit.
        var triangles = new List<Vector3<double>>();
        var outers = new List<(Vector2<double>[] Ring, double Area, List<Vector2<double>[]> Holes)>();
        var holes = new List<Vector2<double>[]>();
        foreach (var ring in rings)
        {
            var area = SignedArea(ring);
            if (area > 0) outers.Add((ring, area, []));
            else if (area < 0) holes.Add(ring);
        }

        foreach (var hole in holes)
        {
            (Vector2<double>[] Ring, double Area, List<Vector2<double>[]> Holes) owner = default;
            foreach (var outer in outers)
            {
                if ((owner.Ring == null || outer.Area < owner.Area) && Within(hole, outer.Ring)) owner = outer;
            }

            owner.Holes?.Add(hole);
        }

        foreach (var (outer, _, faceHoles) in outers)
        {
            if (!ClipEars(outer, faceHoles, triangles))
            {
                var face = new List<Vector2<double>[]>(faceHoles.Count + 1) { outer };
                face.AddRange(faceHoles);
                triangles.AddRange(CutIntoRegions(face));
            }
        }

        return triangles;
    }

    private static bool ClipEars(Vector2<double>[] outer, List<Vector2<double>[]> holes, List<Vector3<double>> triangles)
    {
        // Ear clipping takes rings touching at a point as they are; the triangles turn as the renderer's others do.
        var points = new List<Vector2<double>>(outer);
        var starts = new List<int>(holes.Count);
        foreach (var hole in holes)
        {
            starts.Add(points.Count);
            points.AddRange(hole);
        }

        var indices = new List<int>();
        if (!EarClipping.Triangulate(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(points),
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(starts), indices))
        {
            return false;
        }

        // The triangles cover what the face does or they are not its fill: a ring next to no width can come out of the
        // clipping as wide triangles.
        var faceArea = Math.Abs(SignedArea(outer));
        foreach (var hole in holes) faceArea -= Math.Abs(SignedArea(hole));
        var cut = 0.0;
        for (var t = 0; t + 2 < indices.Count; t += 3)
        {
            var (a, b, c) = (points[indices[t]], points[indices[t + 1]], points[indices[t + 2]]);
            cut += Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
        }

        if (Math.Abs(cut - faceArea) > Math.Max(Math.Abs(faceArea), double.Epsilon) * AreaTolerance)
        {
            return false;
        }

        for (var t = 0; t + 2 < indices.Count; t += 3)
        {
            var (a, b, c) = (points[indices[t]], points[indices[t + 1]], points[indices[t + 2]]);
            if (!MathHelper.Polyline.IsClockwise(a, b, c)) (b, c) = (c, b);
            triangles.Add((Vector3<double>)a);
            triangles.Add((Vector3<double>)b);
            triangles.Add((Vector3<double>)c);
        }

        return true;
    }

    private const double AreaTolerance = 1e-6;

    private static double SignedArea(Vector2<double>[] ring)
    {
        var area = 0.0;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            area += (ring[j].X - ring[i].X) * (ring[j].Y + ring[i].Y);
        return area / 2;
    }

    private static bool Within(Vector2<double>[] hole, Vector2<double>[] outer)
    {
        // The rings cross nowhere: any corner of the hole the outer ring does not share tells.
        var corners = new HashSet<Vector2<double>>(outer);
        foreach (var p in hole)
        {
            if (!corners.Contains(p)) return PointInPolygon(p, outer);
        }

        return false;
    }

    private static List<Vector3<double>> CutIntoRegions(List<Vector2<double>[]> rings)
    {
        if (rings.Count == 0) return [];
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var ring in rings)
        {
            foreach (var p in ring)
            {
                (minX, minY, maxX, maxY) = (Math.Min(minX, p.X), Math.Min(minY, p.Y), Math.Max(maxX, p.X), Math.Max(maxY, p.Y));
            }
        }

        var extent = Math.Max(maxX - minX, maxY - minY);
        if (extent <= 0) return [];
        var scale = FillSize / extent;
        var placed = new List<Vector2<double>[]>(rings.Count);
        foreach (var ring in rings)
        {
            var copy = new Vector2<double>[ring.Length];
            for (var i = 0; i < ring.Length; i++) copy[i] = new Vector2<double>((ring[i].X - minX) * scale, (ring[i].Y - minY) * scale);
            placed.Add(copy);
        }

        var triangles = PlanarFill.Fill(placed, FillRule.EvenOdd);
        for (var i = 0; i < triangles.Count; i++)
        {
            var t = triangles[i];
            triangles[i] = new Vector3<double>(t.X / scale + minX, t.Y / scale + minY, t.Z);
        }

        return triangles;
    }

    private static (IReadOnlyList<Vector2<double>[]> Rings, FillRule Rule) AreaOf(Geometry geometry)
    {
        if (geometry is not { IsClosed: true }) return ([], FillRule.EvenOdd);
        var (rings, rule) = geometry.Area();
        return (rings, rule);
    }

    // Combine two operands whose outlines do NOT cross (clean nesting / disjoint), each even-odd or of one ring.
    // Returns false (caller uses the overlay) for any crossing or touch between rings, or a non-zero area of several.
    private bool TryFastCombine((IReadOnlyList<Vector2<double>[]> Rings, FillRule Rule) first,
        (IReadOnlyList<Vector2<double>[]> Rings, FillRule Rule) second, GeometryType geometryType)
    {
        var rings = new List<(Vector2<double>[] pts, int src)>();
        if (!CollectRings(first, 1, rings)) return false;
        if (!CollectRings(second, 2, rings)) return false;
        if (rings.Count == 0) return false;

        if (AnyRingsCross(rings)) return false;   // crossing/touching -> full pipeline

        // Keep a ring iff mode-membership differs just inside it vs just outside it (it bounds the result). Containment
        // parity per geometry comes from how many of the OTHER rings contain a vertex of this ring (non-crossing makes
        // that unambiguous); "just inside" additionally counts the ring itself.
        var kept = new List<Vector2<double>[]>();
        for (var i = 0; i < rings.Count; i++)
        {
            int out1 = 0, out2 = 0;
            for (var j = 0; j < rings.Count; j++)
            {
                if (j == i || !PointInPolygon(rings[i].pts[0], rings[j].pts)) continue;
                if (rings[j].src == 1) out1++; else out2++;
            }
            var in1 = out1 + (rings[i].src == 1 ? 1 : 0);
            var in2 = out2 + (rings[i].src == 2 ? 1 : 0);

            if (ModeFill((in1 & 1) == 1, (in2 & 1) == 1) != ModeFill((out1 & 1) == 1, (out2 & 1) == 1))
                kept.Add(rings[i].pts);
        }

        foreach (var ring in kept)
        {
            if (Strokeable(ring)) Mesh.AddContour(ring, true);
        }

        if (geometryType == GeometryType.Outlined) return true;

        if (kept.Count == 0)
        {
            Mesh.SetPoints(new List<Vector3<double>>());
            return true;
        }

        // The boundary rings reproduce the result region under even-odd; the nesting fast path earcuts it with holes.
        var polygon = new Polygon(FillRule.EvenOdd);
        foreach (var ring in kept) polygon.AddContour(new MeshContour(ring));
        Mesh.SetPoints(polygon.FillIndirect());
        return true;
    }

    private bool ModeFill(bool inG1, bool inG2) => GeometryCombineMode switch
    {
        GeometryCombineMode.Union => inG1 || inG2,
        GeometryCombineMode.Intersect => inG1 && inG2,
        GeometryCombineMode.Exclude => inG1 && !inG2,
        _ => inG1 ^ inG2,
    };

    // The rings of an operand for the fast path; false for a non-zero area of several rings, whose nesting alone does
    // not tell what it fills.
    private static bool CollectRings((IReadOnlyList<Vector2<double>[]> Rings, FillRule Rule) area, int src,
        List<(Vector2<double>[] pts, int src)> rings)
    {
        if (area.Rule == FillRule.NonZero && area.Rings.Count > 1) return false;
        foreach (var ring in area.Rings) rings.Add((ring, src));
        return true;
    }

    // True if any two edges cross or touch, but for neighbours in one ring meeting at their shared vertex. X-sweep
    // broad-phase (~O(n log n) for clean input). Rings of the two operands meeting at a vertex or along an edge are not
    // clean: which side of such a ring is filled cannot be told from its vertices.
    private static bool AnyRingsCross(List<(Vector2<double>[] pts, int src)> rings)
    {
        var edges = new List<(Vector2<double> a, Vector2<double> b, int ring, int index, int count)>();
        for (var r = 0; r < rings.Count; r++)
        {
            var pts = rings[r].pts;
            for (var i = 0; i < pts.Length; i++)
                edges.Add((pts[i], pts[(i + 1) % pts.Length], r, i, pts.Length));
        }

        edges.Sort((e, f) => Math.Min(e.a.X, e.b.X).CompareTo(Math.Min(f.a.X, f.b.X)));

        // Nearer than a millionth of the size counts as touching: rings that close are the overlay's to combine exactly.
        double minX0 = double.MaxValue, minY0 = double.MaxValue, maxX0 = double.MinValue, maxY0 = double.MinValue;
        foreach (var e in edges)
        {
            (minX0, minY0, maxX0, maxY0) = (Math.Min(minX0, e.a.X), Math.Min(minY0, e.a.Y), Math.Max(maxX0, e.a.X), Math.Max(maxY0, e.a.Y));
        }

        var near = Math.Max(maxX0 - minX0, maxY0 - minY0) * NearFraction;
        var active = new List<(Vector2<double> a, Vector2<double> b, int ring, int index, int count)>();
        foreach (var e in edges)
        {
            var minX = Math.Min(e.a.X, e.b.X);
            for (var i = active.Count - 1; i >= 0; i--)
                if (Math.Max(active[i].a.X, active[i].b.X) < minX - near) active.RemoveAt(i);

            foreach (var o in active)
            {
                var neighbours = e.ring == o.ring &&
                                 ((e.index + 1) % e.count == o.index || (o.index + 1) % o.count == e.index);
                if (neighbours)
                {
                    // Neighbours meet at their shared corner; one folding back near the other is a spike.
                    var shared = e.a == o.a || e.a == o.b ? e.a : e.b;
                    var far1 = shared == e.a ? e.b : e.a;
                    var far2 = shared == o.a ? o.b : o.a;
                    if (Overlap(e.a, e.b, o.a, o.b) || Distance(far1, o.a, o.b) <= near || Distance(far2, e.a, e.b) <= near)
                        return true;
                    continue;
                }

                if (SegmentsIntersect(e.a, e.b, o.a, o.b) || Distance(e.a, o.a, o.b) <= near || Distance(e.b, o.a, o.b) <= near ||
                    Distance(o.a, e.a, e.b) <= near || Distance(o.b, e.a, e.b) <= near)
                    return true;
            }
            active.Add(e);
        }
        return false;
    }

    private const double NearFraction = 1e-6;

    private static double Distance(Vector2<double> p, Vector2<double> a, Vector2<double> b)
    {
        var d = b - a;
        var lengthSquared = d.X * d.X + d.Y * d.Y;
        var t = lengthSquared > 0 ? Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / lengthSquared, 0, 1) : 0;
        return (a + d * t - p).Length();
    }

    // Whether two neighbouring edges fold back along each other rather than only meeting at their shared vertex.
    private static bool Overlap(Vector2<double> a1, Vector2<double> b1, Vector2<double> a2, Vector2<double> b2)
    {
        var shared = a1 == a2 || a1 == b2 ? a1 : b1;
        var far1 = shared == a1 ? b1 : a1;
        var far2 = shared == a2 ? b2 : a2;
        var d1 = far1 - shared;
        var d2 = far2 - shared;
        return Math.Abs(d1.X * d2.Y - d1.Y * d2.X) <= 1e-9 && d1.X * d2.X + d1.Y * d2.Y > 0;
    }

    private static bool SegmentsIntersect(Vector2<double> p1, Vector2<double> p2, Vector2<double> p3, Vector2<double> p4)
    {
        int o1 = Orient(p1, p2, p3), o2 = Orient(p1, p2, p4), o3 = Orient(p3, p4, p1), o4 = Orient(p3, p4, p2);
        if (o1 != o2 && o3 != o4) return true;
        if (o1 == 0 && OnSegment(p1, p2, p3)) return true;
        if (o2 == 0 && OnSegment(p1, p2, p4)) return true;
        if (o3 == 0 && OnSegment(p3, p4, p1)) return true;
        if (o4 == 0 && OnSegment(p3, p4, p2)) return true;
        return false;
    }

    private static int Orient(Vector2<double> a, Vector2<double> b, Vector2<double> c)
    {
        var v = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return v > 1e-9 ? 1 : (v < -1e-9 ? -1 : 0);
    }

    private static bool OnSegment(Vector2<double> a, Vector2<double> b, Vector2<double> p) =>
        Math.Min(a.X, b.X) - 1e-9 <= p.X && p.X <= Math.Max(a.X, b.X) + 1e-9 &&
        Math.Min(a.Y, b.Y) - 1e-9 <= p.Y && p.Y <= Math.Max(a.Y, b.Y) + 1e-9;

    private static bool PointInPolygon(Vector2<double> p, Vector2<double>[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }
}
