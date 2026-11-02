// The analytic-AA ring around a closed contour, as every fringe pass reads it: one record per EDGE, from which the vertex
// stage makes the edge's six corners - several times less to keep and to upload than the corners themselves. Reads
// ViewportSize and FringePixels, which the including effect declares.

// A corner of the ring. An INNER corner (on the contour, coverage 1) has zero directions and is never offset; an OUTER
// one has the two adjacent edge directions, from which the vertex stage builds the screen-space miter. Winding is folded
// into their SIGN, so a hole's inward fringe is just a sign in these vectors.
struct FringeVertex
{
    float2 Position : POSITION;
    float2 Dir0     : TEXCOORD0;   // incoming edge direction; zero on the contour itself
    float2 Dir1     : TEXCOORD1;   // outgoing edge direction
};

// One edge of a ring: where it runs, the direction of the edge before it, its own and the one after it - Winding folded
// into their signs. Matches RenderUnits/RingEdge.cs.
struct RingEdge
{
    float2 A;
    float2 B;
    float2 InDir;
    float2 EdgeDir;
    float2 OutDir;
};

RingEdge* RingEdges;

// Corner (vertexId % 6) of edge (vertexId / 6): two triangles between the edge itself and its outer copy - inner A,
// outer A, inner B, outer A, outer B, inner B.
FringeVertex RingCorner(uint vertexId)
{
    RingEdge* edges = RingEdges;
    RingEdge e = edges[vertexId / 6u];
    uint corner = vertexId % 6u;
    bool atB = corner == 2u || corner == 4u || corner == 5u;
    bool outer = corner == 1u || corner == 3u || corner == 4u;
    FringeVertex v;
    v.Position = atB ? e.B : e.A;
    v.Dir0 = outer ? (atB ? e.EdgeDir : e.InDir) : float2(0.0, 0.0);
    v.Dir1 = outer ? (atB ? e.OutDir : e.EdgeDir) : float2(0.0, 0.0);
    return v;
}

// The ring's corner, expanded. Shared by every fringe pass (solid / pattern / gradient / picture): they differ only in
// WHICH record supplies the matrix and the color, and the expansion itself must stay one definition - it is the thing
// that makes the ring exactly one device pixel wide. `coverage` comes out 1 on the contour and 0 on the outer edge.
float4 ExpandFringe(FringeVertex v, float4x4 m, out float coverage)
{
    float4 clip = mul(float4(v.Position, 0.0, 1.0), m);
    float outer = dot(v.Dir0, v.Dir0) + dot(v.Dir1, v.Dir1);
    if (outer > 0.0)
    {
        // Edge directions -> PIXEL space (w = 0 drops the translation), so the miter is perpendicular to the edge as the
        // rasterizer sees it - correct under anisotropic scale, skew and rotation alike.
        float2 halfVp = max(ViewportSize, float2(1.0, 1.0)) * 0.5;
        float w = max(clip.w, 1e-6);
        float2 e0 = mul(float4(v.Dir0, 0.0, 0.0), m).xy / w * halfVp;
        float2 e1 = mul(float4(v.Dir1, 0.0, 0.0), m).xy / w * halfVp;
        e0 = length(e0) > 1e-9 ? normalize(e0) : float2(0.0, 0.0);
        e1 = length(e1) > 1e-9 ? normalize(e1) : float2(0.0, 0.0);
        float2 n0 = float2(-e0.y, e0.x);
        float2 n1 = float2(-e1.y, e1.x);
        float2 sum = n0 + n1;
        float2 miter = length(sum) > 1e-4 ? normalize(sum) : n0;   // a 180-degree reversal has no bisector: use one normal
        float denom = max(dot(miter, n0), 0.25);                   // clamp the corner spike to <= 4x
        clip.xy += miter * (FringePixels / denom) / halfVp * w;
    }
    coverage = outer > 0.0 ? 0.0 : 1.0;
    return clip;
}
