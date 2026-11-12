using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// Two areas - closed rings, each set under its fill rule - combined by a rule of which side is filled. Points nearer
/// than a ten-millionth of the areas' size (more far from the origin, as far as doubles carry) are one point, and an end
/// that near another edge lies on it - what is that close in the shapes given is meant to meet; the points themselves
/// stay where they are. Every edge is cut wherever another crosses or
/// touches it, along a shared edge too, and the pieces lying on one another are made one, adding up how often, and
/// which way, each area's rings run along it. With the plane so cut, the midpoint of a piece lies on no other piece: a
/// ray from it, across the piece and never along it, counts each area's winding on the side it leaves into, and the
/// piece's own runs give the other side - exact, nothing tried a little to either side. A piece is kept where the rule fills one side and not
/// the other, turned so the filled side is on its left, and the pieces are joined into closed rings, each turning at a
/// vertex to the first piece clockwise from the one it came in by, so rings meeting at a point part there; a ring that
/// passes a point twice is parted into simple rings.
/// </summary>
internal sealed class GeometryOverlay
{
    private const double SnapFraction = 1e-9;
    private const double PositionFraction = 1e-14;
    private const double MergeSteps = 100;

    private readonly double grid;
    private readonly double merge;
    private readonly List<Vector2<double>> points = [];
    private readonly Dictionary<(long, long), int> lattice = [];
    private readonly Dictionary<(long, long), int> corners = [];
    private readonly List<(int From, int To, int Area)> edges = [];
    private readonly List<(int From, int To, int First, int Second)> pieces = [];
    private Bands rows;
    private Bands columns;

    private GeometryOverlay(double grid)
    {
        this.grid = grid;
        merge = grid * MergeSteps;
    }

    /// <summary>The rings bounding where <paramref name="fills"/> says the area is filled, given whether a point is in
    /// the first area and the second; each ring's filled side on its left. An area is its rings under its rule.</summary>
    public static List<Vector2<double>[]> Combine((IReadOnlyList<Vector2<double>[]> Rings, FillRule Rule) first,
        (IReadOnlyList<Vector2<double>[]> Rings, FillRule Rule) second, Func<bool, bool, bool> fills)
    {
        var grid = Grid(first.Rings, second.Rings);
        if (grid <= 0)
        {
            return [];
        }

        var overlay = new GeometryOverlay(grid);
        overlay.AddRings(first.Rings, 0);
        overlay.AddRings(second.Rings, 1);
        overlay.Split();
        return overlay.Boundary(first.Rule, second.Rule, fills);
    }

    private static double Grid(IReadOnlyList<Vector2<double>[]> first, IReadOnlyList<Vector2<double>[]> second)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var rings in new[] { first, second })
        {
            foreach (var ring in rings)
            {
                foreach (var p in ring)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
            }
        }

        if (maxX < minX)
        {
            return 0;
        }

        var extent = Math.Max(maxX - minX, maxY - minY);
        var position = Math.Max(Math.Max(Math.Abs(minX), Math.Abs(maxX)), Math.Max(Math.Abs(minY), Math.Abs(maxY)));
        return Math.Max(Math.Max(extent * SnapFraction, position * PositionFraction), 1e-300);
    }

    private void AddRings(IReadOnlyList<Vector2<double>[]> rings, int area)
    {
        foreach (var ring in rings)
        {
            for (var i = 0; i < ring.Length; i++)
            {
                var from = Corner(ring[i]);
                var to = Corner(ring[(i + 1) % ring.Length]);
                if (from != to)
                {
                    edges.Add((from, to, area));
                }
            }
        }
    }

    private int Corner(Vector2<double> p)
    {
        var key = ((long)Math.Round(p.X / merge), (long)Math.Round(p.Y / merge));
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (corners.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var near) && (points[near] - p).Length() <= merge)
                {
                    return near;
                }
            }
        }

        var id = Point(p);
        corners.TryAdd(key, id);
        return id;
    }

    private void Split()
    {
        var cuts = new List<int>[edges.Count];
        var order = Enumerable.Range(0, edges.Count)
            .OrderBy(e => Math.Min(points[edges[e].From].X, points[edges[e].To].X)).ToList();
        var active = new List<int>();
        foreach (var e in order)
        {
            var minX = Math.Min(points[edges[e].From].X, points[edges[e].To].X);
            active.RemoveAll(o => Math.Max(points[edges[o].From].X, points[edges[o].To].X) < minX - 2 * merge);
            foreach (var o in active)
            {
                Cut(e, o, cuts);
            }

            active.Add(e);
        }

        var runs = new Dictionary<(int, int), (int First, int Second)>();
        for (var e = 0; e < edges.Count; e++)
        {
            var (from, to, area) = edges[e];
            var along = new List<int> { from, to };
            if (cuts[e] != null)
            {
                along.AddRange(cuts[e]);
            }

            var a = points[from];
            var d = points[to] - a;
            var chain = along.Distinct().OrderBy(id => Vector2<double>.Dot(points[id] - a, d)).ToList();
            for (var i = 0; i + 1 < chain.Count; i++)
            {
                var (u, v) = (chain[i], chain[i + 1]);
                var key = u < v ? (u, v) : (v, u);
                var way = u < v ? 1 : -1;
                runs.TryGetValue(key, out var count);
                runs[key] = area == 0 ? (count.First + way, count.Second) : (count.First, count.Second + way);
            }
        }

        foreach (var (key, count) in runs)
        {
            if (count.First != 0 || count.Second != 0)
            {
                pieces.Add((key.Item1, key.Item2, count.First, count.Second));
            }
        }

        rows = new Bands(pieces, points, horizontal: true);
        columns = new Bands(pieces, points, horizontal: false);
    }

    private void Cut(int e, int f, List<int>[] cuts)
    {
        var (e1, e2, _) = edges[e];
        var (f1, f2, _) = edges[f];
        var (p1, p2, p3, p4) = (points[e1], points[e2], points[f1], points[f2]);
        if (Math.Max(p1.Y, p2.Y) < Math.Min(p3.Y, p4.Y) - 2 * merge || Math.Max(p3.Y, p4.Y) < Math.Min(p1.Y, p2.Y) - 2 * merge)
        {
            return;
        }

        // An end of one lying on the other - along a shared stretch, or where one meets the other's side.
        if (OnInterior(f1, e1, e2)) (cuts[e] ??= []).Add(f1);
        if (OnInterior(f2, e1, e2)) (cuts[e] ??= []).Add(f2);
        if (OnInterior(e1, f1, f2)) (cuts[f] ??= []).Add(e1);
        if (OnInterior(e2, f1, f2)) (cuts[f] ??= []).Add(e2);

        var d1 = p2 - p1;
        var d2 = p4 - p3;
        var denominator = Cross(d1, d2);
        // Edges on one line up to rounding have no crossing to find - one would land anywhere along it, cutting these two
        // and not the others on the line; their ends lying on each other cut them where they overlap.
        var length = d1.Length();
        if (denominator == 0 ||
            (Math.Abs(Cross(d1, p3 - p1)) / length <= merge && Math.Abs(Cross(d1, p4 - p1)) / length <= merge))
        {
            return;
        }

        var t = Cross(p3 - p1, d2) / denominator;
        var u = Cross(p3 - p1, d1) / denominator;
        if (t <= 0 || t >= 1 || u <= 0 || u >= 1)
        {
            return;
        }

        // A crossing near a point already there - an end, a corner of a third edge - is that point.
        var crossing = Corner(p1 + d1 * t);
        if (crossing != e1 && crossing != e2)
        {
            (cuts[e] ??= []).Add(crossing);
        }

        if (crossing != f1 && crossing != f2)
        {
            (cuts[f] ??= []).Add(crossing);
        }
    }

    private bool OnInterior(int point, int from, int to)
    {
        if (point == from || point == to)
        {
            return false;
        }

        var (p, a, b) = (points[point], points[from], points[to]);
        var d = b - a;
        var length = d.Length();
        var t = Vector2<double>.Dot(p - a, d) / (length * length);
        return t > 0 && t < 1 && Math.Abs(Cross(d, p - a)) / length <= merge;
    }

    private List<Vector2<double>[]> Boundary(FillRule firstRule, FillRule secondRule, Func<bool, bool, bool> fills)
    {
        var outgoing = new Dictionary<int, List<int>>();
        var kept = new List<(int From, int To)>();
        for (var i = 0; i < pieces.Count; i++)
        {
            var (u, v, first, second) = pieces[i];
            var (leftFirst, leftSecond) = WindingLeftOf(i);
            var leftFilled = fills(Inside(leftFirst, firstRule), Inside(leftSecond, secondRule));
            var rightFilled = fills(Inside(leftFirst - first, firstRule), Inside(leftSecond - second, secondRule));
            if (leftFilled == rightFilled)
            {
                continue;
            }

            var edge = leftFilled ? (u, v) : (v, u);
            if (!outgoing.TryGetValue(edge.Item1, out var list))
            {
                outgoing[edge.Item1] = list = [];
            }

            list.Add(kept.Count);
            kept.Add(edge);
        }

        var used = new bool[kept.Count];
        var rings = new List<Vector2<double>[]>();
        for (var start = 0; start < kept.Count; start++)
        {
            if (used[start])
            {
                continue;
            }

            var walk = new List<int>();
            var current = start;
            var closed = false;
            while (current >= 0 && !used[current])
            {
                used[current] = true;
                walk.Add(kept[current].From);
                current = Next(kept, outgoing, used, current, start);
                if (current == start)
                {
                    closed = true;
                    break;
                }
            }

            // A boundary that does not close has nothing whole to fill: it is left out.
            if (closed)
            {
                AddSimpleRings(walk, rings);
            }
        }

        return rings;
    }

    private (int First, int Second) WindingLeftOf(int index)
    {
        var (u, v, runFirst, runSecond) = pieces[index];
        var (pu, pv) = (points[u], points[v]);
        var m = (pu + pv) / 2;
        var d = pv - pu;
        // The ray leaves the piece across it, never along: up from a piece nearer level, right from one nearer upright.
        var up = Math.Abs(d.X) >= Math.Abs(d.Y);
        var (first, second) = up ? columns.WindingUp(m, index) : rows.WindingRight(m, index);
        // It counts the side it leaves into: the left one when the piece runs rightwards (a ray up) or down (a ray right).
        var rayOnLeft = up ? d.X > 0 : d.Y < 0;
        return rayOnLeft ? (first, second) : (first + runFirst, second + runSecond);
    }

    private static bool Inside(int winding, FillRule rule) => rule == FillRule.NonZero ? winding != 0 : (winding & 1) != 0;

    private int Next(List<(int From, int To)> kept, Dictionary<int, List<int>> outgoing, bool[] used, int current, int start)
    {
        var (from, at) = kept[current];
        if (!outgoing.TryGetValue(at, out var candidates))
        {
            return -1;
        }

        // The first piece out clockwise from the way back - the one the walk started from too, which closes the ring.
        var back = Angle(points[from] - points[at]);
        var best = -1;
        var bestTurn = double.MaxValue;
        foreach (var candidate in candidates)
        {
            if (used[candidate] && candidate != start)
            {
                continue;
            }

            var turn = back - Angle(points[kept[candidate].To] - points[at]);
            while (turn <= 0)
            {
                turn += 2 * Math.PI;
            }

            if (turn < bestTurn)
            {
                bestTurn = turn;
                best = candidate;
            }
        }

        return best;
    }

    private void AddSimpleRings(List<int> walk, List<Vector2<double>[]> rings)
    {
        // A face's boundary can pass a point twice - a hole touching its outer ring there: the loop between the passes
        // is a ring of its own.
        var path = new List<int>();
        var at = new Dictionary<int, int>();
        foreach (var id in walk)
        {
            if (at.TryGetValue(id, out var first))
            {
                var loop = path.GetRange(first, path.Count - first);
                path.RemoveRange(first, path.Count - first);
                foreach (var gone in loop)
                {
                    at.Remove(gone);
                }

                AddRing(loop, rings);
            }

            at[id] = path.Count;
            path.Add(id);
        }

        AddRing(path, rings);
    }

    private void AddRing(List<int> ids, List<Vector2<double>[]> rings)
    {
        if (ids.Count < 3)
        {
            return;
        }

        var ring = new Vector2<double>[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            ring[i] = points[ids[i]];
        }

        rings.Add(ring);
    }

    private int Point(Vector2<double> p)
    {
        var key = ((long)Math.Round(p.X / grid), (long)Math.Round(p.Y / grid));
        // A point a lattice step from one already there is that point.
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (lattice.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var near))
                {
                    return near;
                }
            }
        }

        // The point stays where it is: the lattice only finds its neighbours, so exact contacts stay exact.
        var id = points.Count;
        points.Add(p);
        lattice[key] = id;
        return id;
    }

    private static double Angle(Vector2<double> d) => Math.Atan2(d.Y, d.X);

    private static double Cross(Vector2<double> a, Vector2<double> b) => a.X * b.Y - a.Y * b.X;
}
