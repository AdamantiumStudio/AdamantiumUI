using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// The pieces of an overlay (<see cref="GeometryOverlay"/>) in bands across y - or across x - so a ray along x - or up
/// along y - asks only the pieces of the band it runs in for the winding they add up to, not every piece there is.
/// </summary>
internal sealed class Bands
{
    private readonly List<(int From, int To, int First, int Second)> pieces;
    private readonly List<Vector2<double>> points;
    private readonly bool horizontal;
    private readonly double low;
    private readonly double step;
    private readonly List<int>[] bands;

    /// <summary>Bands of <paramref name="pieces"/>, by their y when <paramref name="horizontal"/> - for rays along x -
    /// else by their x.</summary>
    public Bands(List<(int From, int To, int First, int Second)> pieces, List<Vector2<double>> points, bool horizontal)
    {
        this.pieces = pieces;
        this.points = points;
        this.horizontal = horizontal;
        var count = Math.Max(1, (int)Math.Sqrt(pieces.Count));
        double min = double.MaxValue, max = double.MinValue;
        foreach (var (from, to, _, _) in pieces)
        {
            min = Math.Min(min, Math.Min(Across(points[from]), Across(points[to])));
            max = Math.Max(max, Math.Max(Across(points[from]), Across(points[to])));
        }

        low = min;
        step = max > min ? (max - min) / count : 1;
        bands = new List<int>[count];
        for (var i = 0; i < pieces.Count; i++)
        {
            var (from, to, _, _) = pieces[i];
            var (a, b) = (Across(points[from]), Across(points[to]));
            for (var band = Band(Math.Min(a, b)); band <= Band(Math.Max(a, b)); band++)
            {
                (bands[band] ??= []).Add(i);
            }
        }
    }

    /// <summary>Each area's winding at <paramref name="m"/> by a ray to the right, the piece numbered
    /// <paramref name="skip"/> - the one it starts on - left out; pieces' ends count half-open.</summary>
    public (int First, int Second) WindingRight(Vector2<double> m, int skip)
    {
        int first = 0, second = 0;
        foreach (var i in bands[Band(m.Y)] ?? [])
        {
            var (a, b, runFirst, runSecond) = pieces[i];
            if (i == skip)
            {
                continue;
            }

            var (pa, pb) = (points[a], points[b]);
            var side = Cross(pb - pa, m - pa);
            if (pa.Y <= m.Y && pb.Y > m.Y && side > 0)
            {
                first += runFirst;
                second += runSecond;
            }
            else if (pb.Y <= m.Y && pa.Y > m.Y && side < 0)
            {
                first -= runFirst;
                second -= runSecond;
            }
        }

        return (first, second);
    }

    /// <summary>The same by a ray up: x and y trade places, which mirrors the plane and so turns the winding's sign.
    /// </summary>
    public (int First, int Second) WindingUp(Vector2<double> m, int skip)
    {
        int first = 0, second = 0;
        foreach (var i in bands[Band(m.X)] ?? [])
        {
            var (a, b, runFirst, runSecond) = pieces[i];
            if (i == skip)
            {
                continue;
            }

            var (pa, pb) = (points[a], points[b]);
            var side = Cross(pb - pa, m - pa);
            if (pa.X <= m.X && pb.X > m.X && side < 0)
            {
                first -= runFirst;
                second -= runSecond;
            }
            else if (pb.X <= m.X && pa.X > m.X && side > 0)
            {
                first += runFirst;
                second += runSecond;
            }
        }

        return (first, second);
    }

    private double Across(Vector2<double> p) => horizontal ? p.Y : p.X;

    private int Band(double value) => Math.Clamp((int)Math.Floor((value - low) / step), 0, bands.Length - 1);

    private static double Cross(Vector2<double> a, Vector2<double> b) => a.X * b.Y - a.Y * b.X;
}
