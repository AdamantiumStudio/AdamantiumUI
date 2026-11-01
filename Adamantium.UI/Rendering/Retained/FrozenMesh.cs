using System.Collections.Generic;
using Adamantium.Mathematics;
using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.UI.Core;
using Adamantium.UI.Rendering.RenderUnits;

namespace Adamantium.UI.Rendering.Retained;

// An immutable mesh snapshot taken on the record thread, so the instanced fill never reads the live Mesh; also computes
// the content fingerprint Key, so identical shapes merge.
internal sealed class FrozenMesh
{
    public UIVertex[] Vertices { get; }
    public int[] Indices { get; }
    public PrimitiveType Topology { get; }
    public Rect Bounds { get; }          // the GEOMETRY's local bounds (the gradient shader maps a fragment to a 0..1 uv)
    public GeometryKey Key { get; }
    public bool HasPoints => Vertices.Length > 0;

    /// <summary>The analytic-AA ring for this mesh: the same triangles every instance of it needs, so the collector
    /// uploads ONE of them per key and draws it instanced instead of one fringe draw per element. Empty when the mesh
    /// has no closed boundary to feather.</summary>
    public FringeVertex[] Ring { get; }

    /// <summary>The mesh's closed boundary loops in LOCAL space - the same ones the ring is built from, kept because a
    /// halo needs a distance field baked from them and re-deriving loops on the render thread would mean reading the
    /// live mesh again.</summary>
    public List<(Vector2<double>[] Points, bool IsClosed)> Loops { get; }

    private FrozenMesh(UIVertex[] vertices, int[] indices, PrimitiveType topology, Rect bounds, GeometryKey key,
        FringeVertex[] ring, List<(Vector2<double>[] Points, bool IsClosed)> loops)
    {
        Loops = loops;
        Vertices = vertices;
        Indices = indices;
        Topology = topology;
        Bounds = bounds;
        Key = key;
        Ring = ring;
    }

    /// <summary>Snapshot a live tessellated mesh (call AFTER ProcessGeometry). Null if it has no drawable vertices.
    /// ToUIVertices() already returns a fresh array (we own it); Indices is the mesh's internal array, so it is copied.</summary>
    public static FrozenMesh From(Core.Media.Geometry geometry)
    {
        var mesh = geometry.Mesh;
        var bounds = geometry.Bounds;
        if (mesh is not { HasPoints: true }) return null;
        var vertices = mesh.ToUIVertices();
        if (vertices.Length == 0) return null;
        var indices = mesh.Indices is { Length: > 0 } ix ? (int[])ix.Clone() : [];
        // The fringe ring is frozen with the mesh for the same reason the vertices are: it is built from the RESOLVED
        // fill boundary of THIS tessellation, and a later re-tessellation overwrites the live mesh in place.
        var loops = FillBoundary.LoopsOf(geometry);
        var ring = loops != null ? FringeGeometry.BuildRing(FringeGeometry.Build(loops)) : [];
        // A given edge is not a function of the triangles, so it is part of what the mesh IS: the same triangles with
        // another edge feather differently and must not share a segment's ring.
        var givenEdge = geometry.ContoursAreFillBoundary ? loops : null;
        return new FrozenMesh(vertices, indices, mesh.MeshTopology, bounds,
            GeometryKey.ArbitraryMesh(Fingerprint(vertices, indices, givenEdge)), ring, loops);
    }

    // Stable content hash of the LOCAL mesh (vertex bytes + indices, and the edge when it was given): identical
    // tessellations share a key/segment, and ANY difference - including size - yields a different key, so
    // same-topology-different-size shapes never merge.
    private static long Fingerprint(UIVertex[] vertices, int[] indices, List<(Vector2<double>[] Points, bool IsClosed)> edge)
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

            return (long)h;
        }
    }
}
