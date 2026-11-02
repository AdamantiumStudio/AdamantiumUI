using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Media.Fill;

/// <summary>
/// A geometry's fill as the renderer draws it - vertices, indices, the edge to antialias and its ring, and a fingerprint
/// of all that - made where the geometry was processed, so whoever processes it on a worker (a map's tiles) leaves the
/// drawing thread nothing to work out. Immutable; tied to the <see cref="Geometry.Version"/> it was made from.
/// </summary>
internal sealed class PreparedFill
{
    private PreparedFill(int version, UIVertex[] vertices, int[] indices, PrimitiveType topology, Rect bounds,
        List<(Vector2<double>[] Points, bool IsClosed)> loops, RingEdge[] ring, long fingerprint)
    {
        Version = version;
        Vertices = vertices;
        Indices = indices;
        Topology = topology;
        Bounds = bounds;
        Loops = loops;
        Ring = ring;
        Fingerprint = fingerprint;
    }

    public int Version { get; }
    public UIVertex[] Vertices { get; }
    public int[] Indices { get; }
    public PrimitiveType Topology { get; }

    /// <summary>The geometry's local bounds.</summary>
    public Rect Bounds { get; }

    /// <summary>The fill's closed edge loops in local space, or null when it has none.</summary>
    public List<(Vector2<double>[] Points, bool IsClosed)> Loops { get; }

    /// <summary>The antialiasing ring round <see cref="Loops"/>; empty when there are none.</summary>
    public RingEdge[] Ring { get; }

    /// <summary>A hash of the triangles and, when it was given, the edge: equal fills of different geometries share it.</summary>
    public long Fingerprint { get; }

    /// <summary>The fill of a processed geometry as of <paramref name="version"/> - read before its mesh, so a fill made
    /// while the geometry changes is never taken for the new one - or null when it has no triangles.</summary>
    public static PreparedFill Of(Geometry geometry, int version)
    {
        var bounds = geometry.Bounds;
        var mesh = geometry.Mesh;
        if (mesh is not { HasPoints: true }) return null;
        var vertices = mesh.ToUIVertices();
        if (vertices.Length == 0) return null;
        var indices = mesh.Indices is { Length: > 0 } ix ? (int[])ix.Clone() : [];
        var loops = FillBoundary.LoopsOf(geometry);
        var cut = CutBoxOf(geometry);
        var ring = loops != null ? FringeGeometry.BuildRing(FringeGeometry.Build(loops), cut) : [];
        // A given edge is not a function of the triangles, so it is part of what the fill IS: the same triangles with
        // another edge feather differently and must not share a ring.
        var givenEdge = geometry.ContoursAreFillBoundary ? loops : null;
        return new PreparedFill(version, vertices, indices, mesh.MeshTopology, bounds, loops, ring,
            Hash(vertices, indices, givenEdge, cut));
    }

    /// <summary>The geometry's <see cref="Geometry.FillCutBox"/> where its mesh lies: moved with its transform, as its
    /// points were.</summary>
    public static Rect? CutBoxOf(Geometry geometry)
    {
        if (geometry.FillCutBox is not { } box)
        {
            return null;
        }

        return geometry.Transform != null ? box.TransformToAABB(geometry.Transform.Matrix) : box;
    }

    private static long Hash(UIVertex[] vertices, int[] indices, List<(Vector2<double>[] Points, bool IsClosed)> edge,
        Rect? cut)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;   // FNV-1a
            foreach (var b in MemoryMarshal.AsBytes(vertices.AsSpan())) { h ^= b; h *= 1099511628211UL; }
            foreach (var i in indices) { h ^= (uint)i; h *= 1099511628211UL; }
            if (edge != null)
            {
                foreach (var (points, _) in edge)
                {
                    h ^= 0xFF; h *= 1099511628211UL;   // a ring's end, so splitting the same points differently differs
                    foreach (var b in MemoryMarshal.AsBytes(points.AsSpan())) { h ^= b; h *= 1099511628211UL; }
                }
            }

            if (cut is { } box)
            {
                h = (h ^ (ulong)HashCode.Combine(box.X, box.Y, box.Width, box.Height)) * 1099511628211UL;
            }

            return (long)h;
        }
    }
}
