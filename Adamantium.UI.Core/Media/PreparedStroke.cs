using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media;

/// <summary>
/// The contours a geometry's stroke runs along, as the renderer strokes them - each its points with the ones that
/// repeat dropped, and whether it closes - made where the geometry was processed, so whoever processes it on a worker
/// (a map's tiles) leaves the drawing thread nothing to work out. Immutable; tied to the <see cref="Geometry.Version"/>
/// it was made from.
/// </summary>
internal sealed class PreparedStroke
{
    private PreparedStroke(int version, List<(Vector2<double>[] Points, bool IsClosed)> contours)
    {
        Version = version;
        Contours = contours;
    }

    public int Version { get; }

    /// <summary>Two points or more each; empty for a geometry with nothing to stroke.</summary>
    public List<(Vector2<double>[] Points, bool IsClosed)> Contours { get; }

    public static PreparedStroke Of(Geometry geometry, int version)
    {
        var contours = new List<(Vector2<double>[] Points, bool IsClosed)>();
        if (geometry.Mesh is { } mesh)
        {
            foreach (var contour in mesh.Contours)
            {
                if (contour.Points is not { Length: >= 2 })
                {
                    continue;
                }

                var points = WithoutRepeats(contour.Points, contour.IsGeometryClosed);
                if (points.Length >= 2)
                {
                    contours.Add((points, contour.IsGeometryClosed));
                }
            }
        }

        return new PreparedStroke(version, contours);
    }

    private static Vector2<double>[] WithoutRepeats(Vector2<double>[] points, bool closed)
    {
        const double epsSq = 1e-6;
        var count = 1;
        var lastKept = points[0];
        for (var i = 1; i < points.Length; i++)
        {
            var d = points[i] - lastKept;
            if (d.X * d.X + d.Y * d.Y > epsSq)
            {
                count++;
                lastKept = points[i];
            }
        }

        var dropLast = false;
        if (closed && count > 2)
        {
            var d = lastKept - points[0];
            dropLast = d.X * d.X + d.Y * d.Y <= epsSq;
        }

        if (count == points.Length && !dropLast)
        {
            return points;
        }

        var result = new Vector2<double>[dropLast ? count - 1 : count];
        result[0] = points[0];
        var at = 1;
        var last = points[0];
        for (var i = 1; i < points.Length && at < result.Length; i++)
        {
            var d = points[i] - last;
            if (d.X * d.X + d.Y * d.Y > epsSq)
            {
                result[at++] = points[i];
                last = points[i];
            }
        }

        return result;
    }
}
