// GPU stroke. A continuous polyline is drawn straight from its points (StrokePullVS), one quad per segment and a join fan
// per point; one cut into dashes with shaped ends or trimmed is cut by StrokeDashCut (compute) into a ribbon StrokeVS
// draws. Caps are a per-fragment mask, not geometry, since concave caps must subtract: each vertex carries its position
// in the straight end frames of its piece (a dash, trim run or open contour).

// --- stroke globals ---
float2* Points;           // polyline points (PointCount of them) - every contour's, one after another, when batched
float* Output;            // output vertices, VertexFloats each (see WriteVert)
uint PointCount;          // continuous mode: polyline points (all contours' when batched). dash mode: piece-points.
uint IsClosed;            // 0 = open polyline (flat ends), 1 = closed loop (wrap-around miters + closing pair)

// --- batched continuous mode: many contours in one dispatch ---
uint Batched;             // 0 = Points is one contour; 1 = Points holds many, Contours says which is where
uint* Contours;           // per contour: first point, point count, closed (0/1) - three uints each
uint* PointContours;      // per point of Points: the contour it belongs to

// The contour the running thread works on, set once at the top of each kernel: the whole of Points, or one contour of a
// batch. Thread-private, so every helper below reads the right one.
static float2* ContourPoints;
static uint ContourPointCount;
static uint ContourIsClosed;
// The caps the cut puts on the contour's real ends: Start/EndCap, or - closed and untrimmed, with no real end - the dash
// caps, which is what its seam wears.
static uint ContourStartCap;
static uint ContourEndCap;
uint StartCap;            // start cap: 0 = flat, 1 = square, 2 = convex round, 3 = convex tri, 4 = concave tri, 5 = concave round
uint EndCap;              // end cap (same codes)
uint DashStartCap;        // cap on the STARTING end of each dash/dot piece (cut path); the contour's real ends use Start/EndCap
uint DashEndCap;          // cap on the ENDING end of each dash/dot piece - separate, so a dash can be an arrow
uint JoinType;            // 0 = miter (bisector ribbon, clamped), 1 = bevel (per-segment rectangles + corner wedge), 2 = round (rectangles + disc fan)
uint RoundSegments;       // triangles a point keeps for its join; 0 = none
float HalfThickness;
float Fringe;             // AA feather width in geometry-LOCAL units (~1 device px / scale); ribbon/discs widen by Fringe/2

// --- one-pass GPU cut: the GPU walks the contour by arc length and emits dash/trim pieces ---
uint* Indirect;           // VkDrawIndirectCommand the cut writes (vertexCount = GPU-decided draw size)
float* Pattern;           // dash pattern (on,off,...); PatternCount entries, 0 = solid (one piece over trim)
uint PatternCount;
uint MaxVertices;         // output capacity guard for the cut (CPU sizes it from a worst-case piece bound)
float DashOffset;
float TrimStart;          // 0..1 fraction of total arc length
float TrimEnd;            // 0..1

// --- StrokeDraw (graphics) globals ---
float4x4 Projection;
float4 StrokeColor;

// --- union coverage (translucent strokes only) ---
// Blend once with the max coverage over overlapping pieces: draw writing depth 1 - coverage (Less), then again with
// Equal; the caller clears depth over the stroke first.

// The two ends of the piece being emitted: where each one is, which way the contour runs there (dA points INTO the
// piece, dB OUT of it) and which cap each wears - capStart + 8*capEnd, with 6 meaning "this end is not an end"
// (an interior join, a closed loop), which leaves that cap's mask inert.
struct PieceFrame
{
    float2 A;
    float2 dA;
    float2 B;
    float2 dB;
    float caps;
    // How much SHALLOWER than a half-thickness that end's bite has to be, as a scale on its coordinates: a concave cap
    // subtracts, and its formulas are homogeneous, so carving to depth c instead of h is exactly feeding them u, v and
    // arc multiplied by h/c. 1 = full depth. Keeping it here rather than as another vertex float costs nothing and
    // leaves the pixel shader unchanged. See BiteDepth for what limits c.
};

// A frame for geometry that has no ends at all.
PieceFrame NoCaps()
{
    PieceFrame f;
    f.A = float2(0.0, 0.0);
    f.dA = float2(1.0, 0.0);
    f.B = float2(0.0, 0.0);
    f.dB = float2(1.0, 0.0);
    f.caps = float(6u + 8u * 6u);
    return f;
}

// Unit normal of the segment a->b (perp of the normalized direction).
float2 SegmentNormal(float2 a, float2 b)
{
    float2 d = normalize(b - a);
    return float2(-d.y, d.x);
}

// Whether a join is needed: a missing join leaves a slit of width hw * t at the edge (first order in the turn angle t).
// Skipped only below a quarter pixel, since a join disc's rim shows through a dash.
bool JoinIsVisible(float2 dIn, float2 dOut)
{
    float hw = HalfThickness + Fringe * 0.5;
    float px = Fringe > 0.0 ? Fringe : 0.05;   // Fringe is ~1 device px in these local units; AA off -> a fixed floor
    return hw * length(dOut - dIn) > 0.25 * px;   // |dOut - dIn| = the turn angle, to first order
}

// How far past the geometry point a cap's quad has to REACH so the mask has material to shape: the convex caps and the
// square nub paint up to half a thickness beyond the end, the flat and concave ones never paint past it (concave caps
// carve backwards, so they need no room at all). The AA feather is added on top by the callers.
float CapExtend(uint cap)
{
    if (cap == 1u || cap == 2u || cap == 3u) return HalfThickness;   // square / convex round / convex triangle
    return 0.0;                                                      // flat, concave triangle, concave round
}

// How far point i's cross-section is pushed OUT past the geometry point by its cap (open contour ends only).
float EndExtend(uint i)
{
    if (ContourIsClosed != 0u) return 0.0;
    if (i == 0u) return CapExtend(StartCap) + Fringe * 0.5;
    if (i + 1u == ContourPointCount) return CapExtend(EndCap) + Fringe * 0.5;
    return 0.0;
}

// The two offset vertices (centerline +/- miter) at polyline point i, for the MITER join path only (bevel/round build
// full-width per-segment rectangles instead - see StrokePullVS). Open endpoints use the single adjacent segment
// normal (flat) or push out by the cap's reach; interior points and every point of a closed loop use the bisector
// miter, length clamped so a sharp corner can't shoot the tip to infinity.
void OffsetPair(uint i, out float2 plus, out float2 minus)
{
    float2* points = ContourPoints;
    float2 p = points[i];

    float hw = HalfThickness + Fringe * 0.5;   // widen the ribbon by the AA feather; v carries +/-hw (see EmitQuad)
    float2 miter;
    float miterLen;

    if (ContourIsClosed == 0 && i == 0)
    {
        float2 dir = normalize(points[1] - points[0]);   // inward; outward is -dir
        p -= dir * EndExtend(i);                          // room for the cap mask to shape (convex caps only)
        miter = float2(-dir.y, dir.x);
        miterLen = hw;
    }
    else if (ContourIsClosed == 0 && i + 1 == ContourPointCount)
    {
        float2 dir = normalize(points[i] - points[i - 1]);   // outward
        p += dir * EndExtend(i);
        miter = float2(-dir.y, dir.x);
        miterLen = hw;
    }
    else
    {
        uint prev = (i + ContourPointCount - 1) % ContourPointCount;
        uint next = (i + 1) % ContourPointCount;
        float2 n0 = SegmentNormal(points[prev], p);
        float2 n1 = SegmentNormal(p, points[next]);
        float2 sum = n0 + n1;
        // A line turning straight back has no miter: its ends meet flat.
        miter = dot(sum, sum) > 1e-8 ? normalize(sum) : n0;
        float denom = max(dot(miter, n0), 0.25);       // clamp -> miter length capped at 4*half on sharp corners
        miterLen = hw / denom;
    }

    plus = p + miter * miterLen;
    minus = p - miter * miterLen;
}

// Position + unit tangent at arc length `target` along the contour. The cut needs the piece's END before it writes the
// piece's FIRST vertex - a cap that carves inward reaches back over everything in between - and the end is known in arc
// length long before the walk gets there.
void PointAtArc(float target, out float2 pos, out float2 dir)
{
    float2* points = ContourPoints;
    uint segCount = ContourIsClosed != 0u ? ContourPointCount : ContourPointCount - 1u;
    pos = points[0];
    dir = normalize(points[1] - points[0]);
    float acc = 0.0;
    bool found = false;
    for (uint s = 0u; s < segCount; ++s)
    {
        float2 a = points[s];
        float2 b = points[(s + 1u) % ContourPointCount];
        float len = length(b - a);
        if (len > 1e-6)
        {
            float2 d = (b - a) / len;
            if (!found && (acc + len >= target || s + 1u == segCount))
            {
                pos = a + d * clamp(target - acc, 0.0, len);
                dir = d;
                found = true;
            }
            acc += len;
        }
    }
}

// Floats a vertex takes (see StrokeVertex).
static const uint VertexFloats = 20u;

// A vertex as two parts: what it is at its point on the contour's axis, `anchor` - which the width does not move - and
// how far the width it was written at, HalfThickness + Fringe / 2, moved it from there. Every part of it is the anchor's
// value plus that move, and the move grows with the width, so StrokeVS draws it at another width by scaling the move:
// a stroke whose width or feather changes - a zooming map's lines - is not written again.
// Layout: (x, y | perp, uA, vA, arcA | caps, uB, vB, arcB | piece | anchor x, y | uA, vA, arcA, arcB at the anchor |
// uB, vB at the anchor, the width written at). perp drives AA, u/v place the point in each end's frame, arc is the
// distance along the contour, caps = capStart + 8 * capEnd (6 = none). All affine along a segment.
void WriteVert(float* o, uint vi, float2 anchor, float2 p, float perp, float anchorArcA, float anchorArcB, float arcA,
    float arcB, PieceFrame f, float pieceId)
{
    float2 nA = float2(-f.dA.y, f.dA.x);
    float2 nB = float2(-f.dB.y, f.dB.x);
    float2 rA = anchor - f.A;
    float2 rB = anchor - f.B;
    float2 d = p - anchor;
    uint b = vi * VertexFloats;
    o[b + 0u] = d.x;
    o[b + 1u] = d.y;
    o[b + 2u] = perp;
    o[b + 3u] = dot(d, f.dA);
    o[b + 4u] = dot(d, nA);
    o[b + 5u] = arcA - anchorArcA;
    o[b + 6u] = f.caps;
    o[b + 7u] = -dot(d, f.dB);
    o[b + 8u] = dot(d, nB);
    o[b + 9u] = arcB - anchorArcB;
    o[b + 10u] = pieceId;   // ties between overlapping pieces, see StrokeVertex.PieceId
    o[b + 11u] = anchor.x;
    o[b + 12u] = anchor.y;
    o[b + 13u] = dot(rA, f.dA);
    o[b + 14u] = dot(rA, nA);
    o[b + 15u] = anchorArcA;
    o[b + 16u] = anchorArcB;
    o[b + 17u] = -dot(rB, f.dB);
    o[b + 18u] = dot(rB, nB);
    o[b + 19u] = HalfThickness + Fringe * 0.5;
}

// A vertex the width does not move.
void WritePoint(float* o, uint vi, float2 p, float arcA, float arcB, PieceFrame f, float pieceId)
{
    WriteVert(o, vi, p, p, 0.0, arcA, arcB, arcA, arcB, f, pieceId);
}

// Writes a quad (two triangles, 6 verts) from four corners into outVerts at vertex offset o.
// hw = widened half-extent (HalfThickness + Fringe/2): the P corners sit at +hw perpendicular, the M corners at -hw, and
// the signed distance v=+/-hw is interpolated across the quad so StrokePS feathers both long edges.
// a and b are the quad's two ends on the axis, the anchors its corners are moved from, with their arc distances.
void EmitQuad(float* outVerts, uint o, float2 a, float2 b, float2 aP, float2 aM, float2 bP, float2 bM, float hw,
              float aArcA0, float aArcB0, float bArcA0, float bArcB0,
              float aArcA, float aArcB, float bArcA, float bArcB, PieceFrame f, float pieceId)
{
    WriteVert(outVerts, o + 0u, a, aP,  hw, aArcA0, aArcB0, aArcA, aArcB, f, pieceId);
    WriteVert(outVerts, o + 1u, a, aM, -hw, aArcA0, aArcB0, aArcA, aArcB, f, pieceId);
    WriteVert(outVerts, o + 2u, b, bP,  hw, bArcA0, bArcB0, bArcA, bArcB, f, pieceId);
    WriteVert(outVerts, o + 3u, b, bP,  hw, bArcA0, bArcB0, bArcA, bArcB, f, pieceId);
    WriteVert(outVerts, o + 4u, a, aM, -hw, aArcA0, aArcB0, aArcA, aArcB, f, pieceId);
    WriteVert(outVerts, o + 5u, b, bM, -hw, bArcA0, bArcB0, bArcA, bArcB, f, pieceId);
}

// Round-join disc at V (radius half), at most `maxTris` triangles at `base`; returns the count. Arc distances advance
// across the disc like the ribbon's, so caps carve it correctly.
uint EmitDisc(float* outVerts, uint base, uint maxTris, float2 V, float arcA, float arcB,
              float2 dIn, float2 dOut, PieceFrame f, float pieceId)
{
    float hw = HalfThickness + Fringe * 0.5;   // widened radius; perp = radial distance so the rim feathers
    uint n = 0u;
    for (uint k = 0u; k < RoundSegments && n < maxTris; ++k)
    {
        float a0 = 6.28318530717958647692 * float(k) / float(RoundSegments);
        float a1 = 6.28318530717958647692 * float(k + 1u) / float(RoundSegments);
        float2 r0 = hw * float2(cos(a0), sin(a0));
        float2 r1 = hw * float2(cos(a1), sin(a1));
        WritePoint(outVerts, base + n * 3u + 0u, V, arcA, arcB, f, pieceId);
        WriteVert(outVerts, base + n * 3u + 1u, V, V + r0, hw, arcA, arcB, arcA + dot(r0, dIn), arcB - dot(r0, dOut), f,
            pieceId);
        WriteVert(outVerts, base + n * 3u + 2u, V, V + r1, hw, arcA, arcB, arcA + dot(r1, dIn), arcB - dot(r1, dOut), f,
            pieceId);
        ++n;
    }
    return n;
}


// The bevel/round join where a dash crosses corner V (miter needs none), carrying the piece's frame so caps carve it
// like the ribbon.
uint EmitJoin(float* outVerts, uint vCount, uint maxV, float2 V, float2 dIn, float2 dOut,
              float arcA, float arcB, PieceFrame f, float pieceId)
{
    float h = HalfThickness + Fringe * 0.5;   // widened; the outer corner verts carry perp=+/-h, the center V carries 0
    float2 nInU = float2(-dIn.y, dIn.x);
    float2 nOutU = float2(-dOut.y, dOut.x);
    if (JoinType == 2u)   // round join: a disc of radius half
    {
        uint tris = EmitDisc(outVerts, vCount, (maxV - vCount) / 3u, V, arcA, arcB, dIn, dOut, f, pieceId);
        return vCount + tris * 3u;
    }

    // BEVEL wedge, filled on BOTH sides (mirrors StrokePullVS's bevel): the outer triangle closes the gap the turn
    // opens, the inner one lands inside the two quads' overlap (harmless for an opaque stroke). Doing both sides avoids
    // ever having to pick - and mis-pick - the outer side. MITER is not routed here (its quad ends already meet).
    if (vCount + 6u <= maxV)
    {
        WriteVert(outVerts, vCount + 0u, V, V + nInU * h, h, arcA, arcB, arcA, arcB, f, pieceId);
        WriteVert(outVerts, vCount + 1u, V, V + nOutU * h, h, arcA, arcB, arcA, arcB, f, pieceId);
        WritePoint(outVerts, vCount + 2u, V, arcA, arcB, f, pieceId);
        WriteVert(outVerts, vCount + 3u, V, V - nInU * h, -h, arcA, arcB, arcA, arcB, f, pieceId);
        WriteVert(outVerts, vCount + 4u, V, V - nOutU * h, -h, arcA, arcB, arcA, arcB, f, pieceId);
        WritePoint(outVerts, vCount + 5u, V, arcA, arcB, f, pieceId);
        vCount += 6u;
    }
    return vCount;
}

// Which cap each end of a piece [pieceStart, pieceEnd] wears, packed as capStart + 8*capEnd. A piece end that IS the
// contour's real end (the trim window's edge) wears Start/EndCap; every other one wears the dash caps. ONE cap per end,
// decided once for the whole piece - deciding it per emitted quad is what used to put a line cap AND a dash cap on the
// same end of the first and last dash.
float PieceCaps(float pieceStart, float pieceEnd, float tStart, float tEnd)
{
    uint capS = (pieceStart <= tStart + 1e-4) ? ContourStartCap : DashStartCap;
    uint capE = (pieceEnd >= tEnd - 1e-4) ? ContourEndCap : DashEndCap;
    return float(capS + 8u * capE);
}

// Dash/trim mode: the running thread walks its contour by arc length and writes the visible pieces and their joins to
// outVerts, at most maxV vertices, numbering the pieces from serialBase; returns how many vertices it wrote.
uint CutContour(float* outVerts, uint maxV, uint serialBase)
{
    bool hasRealEnds = (ContourIsClosed == 0u) || (TrimStart > 0.0) || (TrimEnd < 1.0);
    ContourStartCap = hasRealEnds ? StartCap : DashStartCap;
    ContourEndCap = hasRealEnds ? EndCap : DashEndCap;
    float2* points = ContourPoints;
    float* pattern = Pattern;
    uint segCount = ContourIsClosed != 0u ? ContourPointCount : ContourPointCount - 1u;

    float total = 0.0;
    for (uint s = 0u; s < segCount; ++s)
        total += length(points[(s + 1u) % ContourPointCount] - points[s]);
    float tStart = TrimStart * total;
    float tEnd = TrimEnd * total;

    float patTotal = 0.0;
    for (uint k = 0u; k < PatternCount; ++k) patTotal += pattern[k];

    uint pi = 0u;
    float rem = PatternCount > 0u ? pattern[0] : 1e30;
    bool on = true;
    if (PatternCount > 0u && patTotal > 0.0)
    {
        // An odd pattern repeats on and off swapped, so it comes back to itself after two of it.
        float period = PatternCount % 2u == 1u ? patTotal * 2.0 : patTotal;
        float off = DashOffset - floor(DashOffset / period) * period;
        while (off > 1e-6)
        {
            float take = min(off, rem);
            off -= take; rem -= take;
            if (rem <= 1e-6) { pi = (pi + 1u) % PatternCount; rem = pattern[pi]; on = !on; }
        }
    }

    // The ON run currently being drawn, as a PIECE: its two arc-length ends, the frames there and the caps they wear.
    // Everything the piece emits carries this, so the caps mask the piece as ONE shape. The run the walk starts inside
    // began before arc 0 (the offset ate into it), hence the pattern[pi] - rem.
    float pieceStart = -1e30;
    float pieceEnd = 1e30;
    if (PatternCount > 0u && on) { pieceStart = -(pattern[pi] - rem); pieceEnd = rem; }
    // An untrimmed closed contour has no ends: a run crossing the seam keeps its true arc bounds, so no caps appear at the
    // seam.
    bool wraps = (ContourIsClosed != 0u) && (TrimStart <= 0.0) && (TrimEnd >= 1.0);
    pieceStart = wraps ? pieceStart : max(pieceStart, tStart);
    pieceEnd = wraps ? pieceEnd : min(pieceEnd, tEnd);
    PieceFrame frame;
    PointAtArc(pieceStart < 0.0 ? pieceStart + total : pieceStart, frame.A, frame.dA);
    PointAtArc(pieceEnd > total ? pieceEnd - total : pieceEnd, frame.B, frame.dB);
    frame.caps = PieceCaps(pieceStart, pieceEnd, tStart, tEnd);

    uint vCount = 0u;
    uint pieceSerial = serialBase;   // every quad and every join the walk emits is its own piece (see StrokeVertex.PieceId)
    float arc = 0.0;
    bool contInto = (ContourIsClosed != 0u) && on;   // does a live dash cross the vertex ENTERING the current segment (miter start)
    for (uint s = 0u; s < segCount; ++s)
    {
        float2 a = points[s];
        float2 b = points[(s + 1u) % ContourPointCount];
        float2 d = b - a;
        float segLen = length(d);
        if (segLen < 1e-6) continue;
        float2 dir = d / segLen;
        float hw = HalfThickness + Fringe * 0.5;
        float2 nrm = float2(-dir.y, dir.x) * hw;

        // MITER shares the continuous path's trick: where a dash crosses a polyline vertex the two quads meet on the
        // bisector miter offset (constant width, no gap), NOT perpendicular ends bridged by a wedge - the latter piles
        // stray triangles all over a densely-tessellated curve at large thickness. Precompute the miter offsets at THIS
        // segment's two vertices; used only for the ends that land on a crossed vertex (bevel/round keep perpendicular).
        float2 mSp, mSm, mEp, mEm;
        OffsetPair(s, mSp, mSm);
        OffsetPair((s + 1u) % ContourPointCount, mEp, mEm);

        float pos = 0.0;
        bool lastSpanOn = on;   // on-state of the span ending at this segment's corner (captured BEFORE any end-toggle)
        while (pos < segLen - 1e-6)
        {
            lastSpanOn = on;
            float take = PatternCount > 0u ? min(rem, segLen - pos) : (segLen - pos);
            float drawA = max(arc + pos, tStart);
            float drawB = min(arc + pos + take, tEnd);
            if (on && drawB > drawA + 1e-6)
            {
                float2 p0 = a + (drawA - arc) * dir;
                float2 p1 = a + (drawB - arc) * dir;

                // Only the quad that actually STARTS (ENDS) the piece is stretched, and only for a cap that paints past
                // the end - the mask then carves the bulge/tip out of it. A quad in the middle of a piece (a dash
                // spanning several segments of a curve) is stretched nowhere; its corner is closed by the join below.
                float extA = (drawA <= pieceStart + 1e-4) ? CapExtend(uint(frame.caps) % 8u) + Fringe * 0.5 : 0.0;
                float extB = (drawB >= pieceEnd - 1e-4) ? CapExtend(uint(frame.caps) / 8u) + Fringe * 0.5 : 0.0;
                float2 e0 = p0 - dir * extA;
                float2 e1 = p1 + dir * extB;

                // Arc distance from this quad's two ends to the piece's two ends - the REACH gate the straight cap axis
                // cannot provide. Stretching an end moves it past the piece end, so its own distances move with it.
                float aArcA = (drawA - extA) - pieceStart;
                float aArcB = pieceEnd - (drawA - extA);
                float bArcA = (drawB + extB) - pieceStart;
                float bArcB = pieceEnd - (drawB + extB);

                // For MITER, a quad end that lands on a CROSSED vertex uses the shared bisector offset (no wedge);
                // every other end stays perpendicular. startCont = first piece of a segment a live dash entered; endCont =
                // last piece, dash continues into the next segment (reaches the seg end, doesn't end the dash here).
                bool endsDash = PatternCount > 0u && (rem - take) <= 1e-6;
                bool hasNextSeg = (ContourIsClosed != 0u) || (s + 1u < segCount);
                float arcE = arc + segLen;
                bool startCont = (JoinType == 0u) && (pos == 0.0) && contInto;
                bool endCont = (JoinType == 0u) && !endsDash && (pos + take >= segLen - 1e-6) && hasNextSeg
                               && (arcE > tStart + 1e-4) && (arcE < tEnd - 1e-4);
                float2 sTop = startCont ? mSp : e0 + nrm;
                float2 sBot = startCont ? mSm : e0 - nrm;
                float2 eTop = endCont ? mEp : e1 + nrm;
                float2 eBot = endCont ? mEm : e1 - nrm;
                if (vCount + 6u <= maxV)
                {
                    EmitQuad(outVerts, vCount, p0, p1, sTop, sBot, eTop, eBot, hw,
                             drawA - pieceStart, pieceEnd - drawA, drawB - pieceStart, pieceEnd - drawB,
                             aArcA, aArcB, bArcA, bArcB, frame, float(pieceSerial++));
                    vCount += 6u;
                }
            }
            pos += take;
            if (PatternCount > 0u)
            {
                rem -= take;
                if (rem <= 1e-6)
                {
                    pi = (pi + 1u) % PatternCount; rem = pattern[pi]; on = !on;
                    if (on)
                    {
                        // A new dash begins here and its length is already known (rem), so the whole piece - both ends,
                        // both frames - is settled before a single vertex of it is written. That is what lets the far
                        // end's cap mask reach back over everything in between.
                        pieceStart = wraps ? (arc + pos) : max(arc + pos, tStart);
                        pieceEnd = wraps ? (arc + pos + rem) : min(arc + pos + rem, tEnd);
                        PointAtArc(pieceStart, frame.A, frame.dA);
                        PointAtArc(pieceEnd > total ? pieceEnd - total : pieceEnd, frame.B, frame.dB);
                        frame.caps = PieceCaps(pieceStart, pieceEnd, tStart, tEnd);
                    }
                }
            }
        }
        arc += segLen;

        // A dash crosses this segment's END vertex when it is ON on both sides (lastSpanOn && on) and inside the trim.
        // `lastSpanOn && on` requires BOTH sides to be on: if the pattern toggles EXACTLY at the corner, one side is empty
        // and a lone join would jut out as a triangular tab. MITER needs no join here (the mitered quad ends already meet);
        // BEVEL/ROUND add the fan. Either way the flag propagates so the NEXT segment mitres its start off this vertex.
        bool hasNext = (ContourIsClosed != 0u) || (s + 1u < segCount);
        bool joinHere = hasNext && lastSpanOn && on && arc > tStart + 1e-4 && arc < tEnd - 1e-4;
        if (joinHere && JoinType != 0u)
        {
            uint cv = (s + 1u) % ContourPointCount;
            float2 dOut = normalize(points[(s + 2u) % ContourPointCount] - points[cv]);
            if (JoinIsVisible(dir, dOut))
            {
                vCount = EmitJoin(outVerts, vCount, maxV, points[cv], dir, dOut,
                                  arc - pieceStart, pieceEnd - arc, frame, float(pieceSerial++));
            }
        }
        contInto = joinHere;
    }

    return vCount;
}

// StrokeDashCutCS: one contour, one thread; writes the vertex count for DrawIndirect.
[shader("compute")]
[numthreads(1, 1, 1)]
void StrokeDashCutCS(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x != 0u)
        return;

    ContourPoints = Points;
    ContourPointCount = PointCount;
    ContourIsClosed = IsClosed;
    uint vCount = CutContour(Output, MaxVertices, 0u);

    uint* indirect = Indirect;
    indirect[0] = vCount;
    indirect[1] = 1u;
    indirect[2] = 0u;
    indirect[3] = 0u;
}

// --- batched cut: many dashed or trimmed contours in one dispatch and one draw ---
uint ContourCount;        // contours in the batch (Contours holds them)
uint* Slots;              // per contour: its first output vertex and how many it may write - two uints each

// StrokeDashCutBatchCS: one thread per contour, each cutting its own into its own range of the output; what a contour
// leaves of its range is filled with nothing to draw, so the batch is one plain draw of every range.
[shader("compute")]
[numthreads(64, 1, 1)]
void StrokeDashCutBatchCS(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= ContourCount)
        return;

    uint* contours = Contours;
    uint* slots = Slots;
    uint c = tid.x * 3u;
    ContourPoints = Points + contours[c];
    ContourPointCount = contours[c + 1u];
    ContourIsClosed = contours[c + 2u];

    // A piece is six vertices at least, so a contour's first vertex over six numbers its pieces apart from every other
    // contour's - in the 16 bits the union depth holds, wrapping past them.
    uint first = slots[tid.x * 2u];
    float* outVerts = Output + first * VertexFloats;
    uint capacity = slots[tid.x * 2u + 1u];
    uint vCount = CutContour(outVerts, capacity, (first / 6u) % 65536u);

    PieceFrame none = NoCaps();
    for (uint k = vCount; k < capacity; ++k)
        WritePoint(outVerts, k, float2(0.0, 0.0), 1e6, 1e6, none, 0.0);
}

// What the width moved the vertex by - its position and frame values - and the rest at its anchor (see WriteVert).
struct VSInput
{
    float2 Offset : POSITION;
    float4 Cap0 : TEXCOORD0;   // perp, uA, vA, arcA - moves
    float4 Cap1 : TEXCOORD1;   // caps, uB, vB, arcB - moves but caps
    float PieceId : TEXCOORD2;
    float2 Anchor : TEXCOORD3;
    float4 AnchorA : TEXCOORD4;   // uA, vA, arcA, arcB at the anchor
    float3 AnchorB : TEXCOORD5;   // uB, vB at the anchor, the width the moves were written at
};

struct PSInput
{
    float4 Position : SV_Position;
    float4 Cap0 : TEXCOORD0;
    float4 Cap1 : TEXCOORD1;
    float PieceId : TEXCOORD2;
    float Arc : TEXCOORD3;     // how far along its contour the point is; read only by a dash mask
};

// A continuous stroke's dashes as a mask (DashMask != 0): the pattern - Pattern, PatternCount, DashOffset - laid along
// the contour's length, its runs' ends square and feathered like the stroke's edge. What the cut's real pieces are for
// is ends of other shapes; flat-ended dashes are this. An odd pattern is taken twice, its runs on and off in turn.
uint DashMask;

// `feather` is a pixel's length along the contour, in local units; 0 gives hard ends.
float DashCoverage(float arc, float feather)
{
    float* pattern = Pattern;
    uint runs = PatternCount % 2u == 1u ? PatternCount * 2u : PatternCount;
    float total = 0.0;
    for (uint k = 0u; k < runs; ++k) total += pattern[k % PatternCount];
    if (total <= 0.0) return 1.0;

    float at = arc + DashOffset;
    at -= floor(at / total) * total;

    // The signed distance to the nearest end of an "on" run: positive inside one.
    float start = 0.0;
    float sd = -1e9;
    for (uint k = 0u; k < runs; ++k)
    {
        float end = start + pattern[k % PatternCount];
        if ((k % 2u) == 0u)
        {
            // This run and its repeats a period either side, so a run across the pattern's seam is whole.
            sd = max(sd, min(at - start, end - at));
            sd = max(sd, min(at - start + total, end - total - at));
            sd = max(sd, min(at - start - total, end + total - at));
        }

        start = end;
    }

    return feather > 0.0 ? saturate(sd / feather + 0.5) : (sd >= 0.0 ? 1.0 : 0.0);
}

// Whether a dash covers `arc` and a feather's width either side of it in full.
bool DashRunsThrough(float arc)
{
    float reach = max(Fringe, 1e-3);
    return DashCoverage(arc - reach, Fringe) >= 1.0 && DashCoverage(arc, Fringe) >= 1.0 &&
        DashCoverage(arc + reach, Fringe) >= 1.0;
}

[shader("vertex")]
PSInput StrokeVS(VSInput input)
{
    // The moves scaled to the width drawn now.
    float scale = (HalfThickness + Fringe * 0.5) / max(input.AnchorB.z, 1e-20);
    float2 position = input.Anchor + input.Offset * scale;

    PSInput o;
    o.Position = mul(float4(position, 0.0, 1.0), Projection);   // row-vector convention (matches engine effects)
    o.Cap0 = float4(input.Cap0.x * scale, input.AnchorA.x + input.Cap0.y * scale, input.AnchorA.y + input.Cap0.z * scale,
        input.AnchorA.z + input.Cap0.w * scale);
    o.Cap1 = float4(input.Cap1.x, input.AnchorB.x + input.Cap1.y * scale, input.AnchorB.y + input.Cap1.z * scale,
        input.AnchorA.w + input.Cap1.w * scale);
    o.PieceId = input.PieceId;
    o.Arc = 0.0;
    return o;
}

// --- pulled continuous draw: no ribbon is kept - each vertex of it is worked out where it is drawn, from the points ---
float* Arcs;              // per point of Points: how far along its contour it is from the contour's first point

// A corner of the ribbon at the width drawn now; `along` is how far along its contour it is, for a dash mask.
PSInput Corner(float2 p, float perp, float arcA, float arcB, PieceFrame f, float pieceId, float along)
{
    float2 rA = p - f.A;
    float2 rB = p - f.B;
    PSInput o;
    o.Position = mul(float4(p, 0.0, 1.0), Projection);
    o.Cap0 = float4(perp, dot(rA, f.dA), dot(rA, float2(-f.dA.y, f.dA.x)), arcA);
    o.Cap1 = float4(f.caps, -dot(rB, f.dB), dot(rB, float2(-f.dB.y, f.dB.x)), arcB);
    o.PieceId = pieceId;
    o.Arc = along;
    return o;
}

// A vertex of a triangle that draws nothing: all three of it fall on one point.
PSInput NoCorner(float2 p)
{
    return Corner(p, 0.0, 1e6, 1e6, NoCaps(), 0.0, 0.0);
}

// StrokePullVS: the continuous stroke drawn straight from its points, as many vertices a point as StrokeExpandCS writes
// for it - the segment quad to the next point, then its join fan - each worked out here, so a stroke keeps only its
// points and draws at whatever width and scale it is drawn at.
[shader("vertex")]
PSInput StrokePullVS(uint vertexId : SV_VertexID)
{
    uint vpp = 6u + RoundSegments * 3u;
    uint t = vertexId / vpp;
    uint corner = vertexId % vpp;

    uint i = t;
    uint first = 0u;
    ContourPoints = Points;
    ContourPointCount = PointCount;
    ContourIsClosed = IsClosed;
    if (Batched != 0u)
    {
        uint* contours = Contours;
        uint* pointContours = PointContours;
        uint c = pointContours[t] * 3u;
        first = contours[c];
        ContourPoints = Points + first;
        ContourPointCount = contours[c + 1u];
        ContourIsClosed = contours[c + 2u];
        i = t - first;
    }

    float2* points = ContourPoints;
    float* arcs = Arcs;
    float2 p = points[i];
    float along = arcs[first + i];
    bool isStart = (ContourIsClosed == 0u) && (i == 0u);
    bool isEnd = (ContourIsClosed == 0u) && (i + 1u == ContourPointCount);
    bool hasSegment = (ContourIsClosed != 0u) || (i + 1u < ContourPointCount);

    PieceFrame frame = NoCaps();
    float back = 1e6;
    float fwd = 1e6;
    if (ContourIsClosed == 0u)
    {
        uint last = ContourPointCount - 1u;
        frame.A = points[0];
        frame.dA = normalize(points[1] - points[0]);
        frame.B = points[last];
        frame.dB = normalize(points[last] - points[last - 1u]);
        frame.caps = float(StartCap + 8u * EndCap);
        back = arcs[first + i];
        fwd = arcs[first + last] - back;
    }

    float hw = HalfThickness + Fringe * 0.5;
    if (corner < 6u)
    {
        if (!hasSegment)
        {
            return NoCorner(p);
        }

        // The segment quad to the next point, its corners in EmitQuad's order: aP, aM, bP, bP, aM, bM.
        uint ni = (i + 1u) % ContourPointCount;
        float segLen = length(points[ni] - p);
        float2 aP, aM, bP, bM;
        if (JoinType == 0u)
        {
            OffsetPair(i, aP, aM);
            OffsetPair(ni, bP, bM);
        }
        else
        {
            float2 a = p;
            float2 b = points[ni];
            float2 dir = normalize(b - a);
            float2 nrm = float2(-dir.y, dir.x) * hw;
            if (ContourIsClosed == 0u)
            {
                if (i == 0u) a -= dir * EndExtend(i);
                if (ni + 1u == ContourPointCount) b += dir * EndExtend(ni);
            }

            aP = a + nrm;
            aM = a - nrm;
            bP = b + nrm;
            bM = b - nrm;
        }

        // The length along the contour at a corner is its point's projection on the segment - exact over the whole quad,
        // past a cap's pushed out end and a miter's corner too.
        bool atB = corner == 2u || corner == 3u || corner == 5u;
        bool plus = corner == 0u || corner == 2u || corner == 3u;
        float2 q = atB ? (plus ? bP : bM) : (plus ? aP : aM);
        float2 segDir = normalize(points[ni] - p);
        return Corner(q, plus ? hw : -hw, atB ? back + segLen : back, atB ? fwd - segLen : fwd, frame,
            float((t * 2u) % 65536u), along + dot(q - p, segDir));
    }

    // A dashed stroke joins two segments only where a dash runs on through the corner; elsewhere a join would jut out
    // of a dash's end, or into a gap.
    if (DashMask != 0u && !DashRunsThrough(along))
    {
        return NoCorner(p);
    }

    // The join fan: triangle `tri` of it, vertex `v` of that.
    uint k = corner - 6u;
    uint tri = k / 3u;
    uint v = k % 3u;
    uint prev = (i + ContourPointCount - 1u) % ContourPointCount;
    uint next = (i + 1u) % ContourPointCount;
    float2 dIn = normalize(p - points[prev]);
    float2 dOut = normalize(points[next] - p);
    float fanId = float((t * 2u + 1u) % 65536u);
    if (JoinType == 2u && !isStart && !isEnd && JoinIsVisible(dIn, dOut))
    {
        if (v == 0u)
        {
            return Corner(p, 0.0, back, fwd, frame, fanId, along);
        }

        float angle = 6.28318530717958647692 * float(v == 1u ? tri : tri + 1u) / float(RoundSegments);
        float2 r = hw * float2(cos(angle), sin(angle));
        return Corner(p + r, hw, back + dot(r, dIn), fwd - dot(r, dOut), frame, fanId, along);
    }

    if (JoinType == 1u && hasSegment && !isStart && tri < 2u)
    {
        if (v == 2u)
        {
            return Corner(p, 0.0, back, fwd, frame, fanId, along);
        }

        float side = tri == 0u ? 1.0 : -1.0;
        float2 n = v == 0u ? SegmentNormal(points[prev], p) : SegmentNormal(p, points[next]);
        return Corner(p + n * hw * side, hw * side, back, fwd, frame, fanId, along);
    }

    return NoCorner(p);
}

// Signed distance to a cap boundary (positive = painted) from u (along the end's axis), v (across) and arc (along the
// contour, which gates caps where the path folds back). hBite caps concave bites at mid-piece.
float CapSd(uint cap, float u, float v, float arc, float h, float hBite)
{
    if (cap == 6u) return 1e9;                                    // this end is not an end (interior join, closed loop)
    if (arc > h + Fringe) return 1e9;                             // farther along the contour than this cap can reach
    if (cap == 1u) return u + h;                                  // square: half a thickness of flat nub
    if (cap == 2u) return (u >= 0.0) ? 1e9 : h - length(float2(u, v));        // convex round: half-disc out
    if (cap == 3u) return (u >= 0.0) ? 1e9 : (u + h - abs(v)) * 0.70710678;   // convex triangle: tip out at the center
    if (cap == 4u) return (u - hBite + abs(v)) * 0.70710678;      // concave triangle: V notch cut in
    if (cap == 5u) return length(float2(max(u, 0.0), v)) - hBite; // concave round: half-disc bitten out
    return u;                                                     // flat
}

// Analytic AA coverage from the nearest boundary (edges and caps), feathered over one pixel as wide as it is here;
// Fringe == 0 gives hard edges. Shared by all three passes so their depths match bit for bit.
float StrokeCoverage(PSInput input)
{
    uint caps = uint(round(input.Cap1.x));
    float hBite = min(HalfThickness, 0.5 * (input.Cap0.w + input.Cap1.w));
    float sd = HalfThickness - abs(input.Cap0.x);
    sd = min(sd, CapSd(caps % 8u, input.Cap0.y, input.Cap0.z, input.Cap0.w, HalfThickness, hBite));
    sd = min(sd, CapSd(caps / 8u, input.Cap1.y, input.Cap1.z, input.Cap1.w, HalfThickness, hBite));

    // A pixel's width across the stroke and along it, in local units, where this pixel is - read from the screen, not
    // given: under a perspective it is not the same from one end of the stroke to the other.
    float across = length(float2(ddx(input.Cap0.x), ddy(input.Cap0.x)));
    float along = length(float2(ddx(input.Arc), ddy(input.Arc)));
    float coverage = Fringe > 0.0 ? saturate(sd / max(across, 1e-6) + 0.5) : (sd >= 0.0 ? 1.0 : 0.0);
    return DashMask != 0u ? coverage * DashCoverage(input.Arc, Fringe > 0.0 ? along : 0.0) : coverage;
}

// Depth = coverage in the high 8 bits and piece id in the low 16, exact in float32, so max coverage wins and ties never
// blend twice.
float CoverageDepth(float coverage, float pieceId)
{
    float covered = round(saturate(1.0 - coverage) * 255.0);
    float serial = clamp(round(pieceId), 0.0, 65535.0);
    return (covered * 65536.0 + serial) / 16777216.0;
}

[shader("fragment")]
float4 StrokePS(PSInput input) : SV_Target
{
    float4 c = StrokeColor;
    c.a *= StrokeCoverage(input);
    return c;
}

// Union pass 1: depth only, under Less over bounds the caller cleared to 1.0. Afterwards each pixel holds the depth of
// the single fragment with the greatest coverage over it - the coverage of the UNION.
[shader("fragment")]
float StrokeUnionDepthPS(PSInput input) : SV_Depth
{
    return CoverageDepth(StrokeCoverage(input), input.PieceId);
}

struct UnionOutput
{
    float4 Color : SV_Target;
    float Depth : SV_Depth;
};

// Union pass 2: Equal, no depth write. Exactly the fragment pass 1 settled on gets through, so the union blends ONCE -
// which is the whole point. Drawing it under Less in a single pass instead looks nearly right and is not: the order two
// pieces reach a pixel is fixed along their whole shared edge, so wherever the weaker one lands first it paints a
// second time down the entire length, and that reads as a hard line through the stroke.
[shader("fragment")]
UnionOutput StrokeUnionDrawPS(PSInput input)
{
    // No discard for zero coverage: it would pull in DemoteToHelperInvocation, and it buys nothing - such a fragment
    // paints an alpha of zero anyway.
    float coverage = StrokeCoverage(input);

    UnionOutput o;
    o.Color = StrokeColor;
    o.Color.a *= coverage;
    o.Depth = CoverageDepth(coverage, input.PieceId);
    return o;
}

technique Stroke
{
    // DashCut (compute): one-pass dash/trim cut, single thread, writes the VkDrawIndirectCommand. DrawIndirect.
    pass DashCut
    {
        ComputeShader = StrokeDashCutCS;
    }

    // DashCutBatch (compute): many contours' cut, a thread each, into fixed ranges of one output. Plain Draw.
    pass DashCutBatch
    {
        ComputeShader = StrokeDashCutBatchCS;
    }

    // Draw (graphics): rasterize the compute-produced ribbon with a solid color.
    pass Draw
    {
        VertexShader = StrokeVS;
        PixelShader = StrokePS;
    }

    // UnionDepth / UnionDraw (graphics): the two passes a TRANSLUCENT stroke takes so its self-overlaps blend once.
    pass UnionDepth
    {
        VertexShader = StrokeVS;
        PixelShader = StrokeUnionDepthPS;
    }

    pass UnionDraw
    {
        VertexShader = StrokeVS;
        PixelShader = StrokeUnionDrawPS;
    }

    // The same three, for a continuous stroke drawn straight from its points (StrokePullVS): no vertex buffer.
    pass PullDraw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokePS;
    }

    pass PullUnionDepth
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokeUnionDepthPS;
    }

    pass PullUnionDraw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokeUnionDrawPS;
    }
}
