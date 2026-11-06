// GPU stroke, drawn straight from its points (StrokePullVS): one quad per segment and a join fan per point. Nothing is
// cut: caps, the trim window and dash runs are per-fragment masks along the ribbon, since concave caps must subtract.

#include "Includes/CommonData.fxh"
#include "Includes/ClipMath.fxh"

// --- the stroke being drawn ---
// Read by every helper below. Invocation-private: a lone stroke's draw fills them from its uniforms (the U-named globals
// further down), a batch of strokes from the record its instance names - see LoadUniforms and LoadRecord.
static float2* Points;           // polyline points (PointCount of them) - every contour's, one after another, when batched
static uint PointCount;          // polyline points (all contours' when batched)
static uint IsClosed;            // 0 = open polyline (flat ends), 1 = closed loop (wrap-around miters + closing pair)

// Batched contours: many contours in one draw.
static uint Batched;             // 0 = Points is one contour; 1 = Points holds many, Contours says which is where
static uint* Contours;           // per contour: first point, point count, closed (0/1) - three uints each
static uint* PointContours;      // per point of Points: the contour it belongs to

static uint StartCap;            // start cap: 0 = flat, 1 = square, 2 = convex round, 3 = convex tri, 4 = concave tri, 5 = concave round
static uint EndCap;              // end cap (same codes)
static uint DashStartCap;        // cap on the STARTING end of each dash; the stroke's real ends use Start/EndCap
static uint DashEndCap;          // cap on the ENDING end of each dash - separate, so a dash can be an arrow
static uint JoinType;            // 0 = miter (bisector ribbon, clamped), 1 = bevel (per-segment rectangles + corner wedge), 2 = round (rectangles + disc fan)
static uint RoundSegments;       // triangles a point keeps for its join; 0 = none
static float HalfThickness;
static float Fringe;             // AA feather width in geometry-LOCAL units (~1 device px / scale); ribbon/discs widen by Fringe/2

// The runs: the trim window and the dash pattern, laid along the contour's length.
static float* Pattern;           // dash pattern (on,off,...); PatternCount entries, 0 = solid
static uint PatternCount;
static float DashOffset;
static float DashScale;          // the pattern's units to local ones: 1, or the window's units' for a pen measured in them
static float TrimStart;          // 0..1 fraction of total arc length
static float TrimEnd;            // 0..1
static uint Runs;                // the ends are the runs' - see StrokeCoverage
static float* Arcs;              // per point of Points: how far along its contour it is from the contour's first point

static float4x4 Wvp;             // the stroke's local units to clip space
static float4 StrokeColor;
static float PieceSalt;          // added to every piece id, so two strokes of one batch do not share them

// The contour the running vertex belongs to: the whole of Points, or one contour of a batch.
static float2* ContourPoints;
static uint ContourPointCount;
static uint ContourIsClosed;

// --- a lone stroke's uniforms (Projection, from CommonData, is its local units to clip space) ---
float2* UPoints;
uint UPointCount;
uint UIsClosed;
uint UBatched;
uint* UContours;
uint* UPointContours;
uint UStartCap;
uint UEndCap;
uint UDashStartCap;
uint UDashEndCap;
uint UJoinType;
uint URoundSegments;
float UHalfThickness;
float UFringe;
float* UPattern;
uint UPatternCount;
float UDashOffset;
float UDashScale;
float UTrimStart;
float UTrimEnd;
uint URuns;
float* UArcs;
float4 UStrokeColor;

void LoadUniforms()
{
    Points = UPoints;
    PointCount = UPointCount;
    IsClosed = UIsClosed;
    Batched = UBatched;
    Contours = UContours;
    PointContours = UPointContours;
    StartCap = UStartCap;
    EndCap = UEndCap;
    DashStartCap = UDashStartCap;
    DashEndCap = UDashEndCap;
    JoinType = UJoinType;
    RoundSegments = URoundSegments;
    HalfThickness = UHalfThickness;
    Fringe = UFringe;
    Pattern = UPattern;
    PatternCount = UPatternCount;
    DashOffset = UDashOffset;
    DashScale = UDashScale;
    TrimStart = UTrimStart;
    TrimEnd = UTrimEnd;
    Runs = URuns;
    Arcs = UArcs;
    Wvp = Projection;
    StrokeColor = UStrokeColor;
    PieceSalt = 0.0;
}

// --- a batch of strokes: one record each, drawn by an instance of its own (StrokeBatchCollector) ---
// Projection is the window to clip space here; each record carries its stroke's local matrix and transform slot.
struct StrokeRecord
{
    float4x4 Local;          // the stroke's local units to its slot's space
    float2* Points;
    float* Arcs;
    uint* Contours;
    uint* PointContours;
    float* Pattern;
    uint PointCount;
    uint PatternCount;
    uint Flags;              // 1 closed, 2 batched contours, 4 runs, 8 thickness and dashes in window units
    uint Caps;               // start | end << 4 | dash start << 8 | dash end << 12
    uint RoundSegments;
    uint JoinType;
    float4 Pen;              // .x thickness, .y least device thickness (0 none), .z dash offset, .w piece salt
    float4 Window;           // .x trim start, .y trim end, .z transform slot, .w opacity slot (-1 none)
    float4 Color;            // straight RGBA, the element's own alpha folded in
    float4 Extra;            // .x rounded clip slot (-1 none)
};

StrokeRecord* Records;
float RenderScale;           // device pixels per window unit
uint AntiAlias;              // 0 = hard edges

// Device pixels per local unit of a stroke drawn by `m`, at its point `p`: the area scale of its map to the viewport's
// pixels after the divide by w - the same everywhere under a flat transform, largest near the eye under a perspective.
float DevicePixelScale(float4x4 m, float2 p)
{
    float4 c = mul(float4(p, 0.0, 1.0), m);
    if (!(c.w > 1e-6)) return 0.0;
    float2 halfVp = ViewportSize * 0.5;
    float2 at = c.xy / c.w;
    float4 cx = mul(float4(1.0, 0.0, 0.0, 0.0), m);
    float4 cy = mul(float4(0.0, 1.0, 0.0, 0.0), m);
    float2 ax = (cx.xy - at * cx.w) / c.w * halfVp;
    float2 ay = (cy.xy - at * cy.w) / c.w * halfVp;
    return sqrt(abs(ax.x * ay.y - ax.y * ay.x));
}

StrokeRecord LoadRecord(uint index)
{
    StrokeRecord* records = Records;
    StrokeRecord r = records[index];
    NodeSlot* nodes = Transforms;
    Wvp = mul(mul(r.Local, nodes[(uint)r.Window.z].World), Projection);

    Points = r.Points;
    PointCount = r.PointCount;
    IsClosed = r.Flags & 1u;
    Batched = (r.Flags >> 1u) & 1u;
    Contours = r.Contours;
    PointContours = r.PointContours;
    StartCap = r.Caps & 15u;
    EndCap = (r.Caps >> 4u) & 15u;
    DashStartCap = (r.Caps >> 8u) & 15u;
    DashEndCap = (r.Caps >> 12u) & 15u;
    JoinType = r.JoinType;
    RoundSegments = r.RoundSegments;
    Pattern = r.Pattern;
    PatternCount = r.PatternCount;
    DashOffset = r.Pen.z;
    TrimStart = r.Window.x;
    TrimEnd = r.Window.y;
    Runs = (r.Flags >> 2u) & 1u;
    Arcs = r.Arcs;
    StrokeColor = r.Color;
    PieceSalt = r.Pen.w;

    // The width in local units, worked out from the matrix as it is now: a camera that moved the slot moved the record
    // with it, and a line measured in the window's units or held to a least width keeps it - its dashes too. A record is
    // flat (a stroke under a perspective is drawn on its own), so its scale is the same at every point.
    float2* points = r.Points;
    float device = max(DevicePixelScale(Wvp, r.PointCount > 0u ? points[0] : float2(0.0, 0.0)), 1e-6);
    float thickness = r.Pen.x;
    DashScale = (r.Flags & 8u) != 0u ? RenderScale / device : 1.0;
    thickness *= DashScale;

    if (r.Pen.y > 0.0) thickness = max(thickness, r.Pen.y / device);
    HalfThickness = thickness * 0.5;
    Fringe = AntiAlias != 0u ? 1.0 / device : 0.0;
    return r;
}

// --- the brush: a solid color, a gradient (IsGradient != 0), a pattern or noise, or a picture - each evaluated at a point
// of a box. Over the shape's bounds the point is where the pixel is and the box the shape's (the same box its fill
// spans); along the line the box is the stroke unrolled - its length by its thickness - and the point how far along the
// line the pixel is and how far across it. ---
uint BrushAlong;          // 0 = over BrushBounds, 1 = along the line
float4 BrushBounds;       // the shape's local bounds: minXY, sizeXY

#include "Includes/GradientMath.fxh"
#include "Includes/GradientUniforms.fxh"
#include "Includes/NoiseMath.fxh"
#include "Includes/PatternMath.fxh"
#include "Includes/TileMath.fxh"
#include "Includes/ColorMath.fxh"

// A PatternBrush / NoiseBrush, as PatternBrushRecord packs it (see PatternFill).
float4 PParams;           // .y the kind, .z the cell in local units
float4 PColor1;
float4 PColor2;
float4 PColor3;
float4 PNoise;
float4 PAnim;

// A picture, laid out by ImageTiling over the box (see TileSourceUv) - SourceTexture and SourceSampler, or a layer of a
// texture array.
Texture2DArray SourceTextureArray;
float TLayer;
float4 TTile;
float4 TRotation;
float4 TDrawn;
float4 TUvRect;
float4 TTint;
float4 TAdjust;           // the picture's colors, before the tint - see AdjustColor
float2 TRepeat;           // per axis: 1 = the tile repeats
float TMirror;            // mirror flags: 1 = X, 2 = Y, 3 = both
float TAlongLength;       // along the line: the length one copy takes, at the stroke's width

// --- union coverage (translucent strokes only) ---
// Blend once with the max coverage over overlapping pieces: draw writing depth 1 - coverage (Less), then again with
// Equal; the caller clears depth over the stroke first.

// The contour's two real ends: where each one is, which way the contour runs there (dA points INTO the stroke, dB OUT
// of it) and which cap each wears - capStart + 8*capEnd, with 6 meaning "this end is not an end" (a closed loop), which
// leaves that cap's mask inert.
struct PieceFrame
{
    float2 A;
    float2 dA;
    float2 B;
    float2 dB;
    float caps;
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
// Skipped only below a fiftieth of a pixel: antialiased, even a quarter-pixel slit shows as a dark nick in the edge.
bool JoinIsVisible(float2 dIn, float2 dOut)
{
    float hw = HalfThickness + Fringe * 0.5;
    float px = Fringe > 0.0 ? Fringe : 0.05;   // Fringe is ~1 device px in these local units; AA off -> a fixed floor
    return hw * length(dOut - dIn) > 0.02 * px;   // |dOut - dIn| = the turn angle, to first order
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

    float hw = HalfThickness + Fringe * 0.5;   // widen the ribbon by the AA feather; v carries +/-hw
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

struct PSInput
{
    float4 Position : SV_Position;
    float4 Cap0 : TEXCOORD0;   // perp, uA, vA, arcA
    float4 Cap1 : TEXCOORD1;   // caps, uB, vB, arcB
    float PieceId : TEXCOORD2;
    float Arc : TEXCOORD3;     // how far along its contour the point is; read by the runs
    nointerpolation float2 Contour : TEXCOORD4;   // the contour's length, and 1 when it is closed
    float2 Local : TEXCOORD5;                     // where the point is, in the contour's units
    nointerpolation uint3 Segment : TEXCOORD6;    // the contour's first point in Points, its point count, this segment
    nointerpolation float2 Span : TEXCOORD7;      // how far along the contour this piece of ribbon may say it is
    nointerpolation uint Record : TEXCOORD8;      // a batched stroke's record
};

// Runs != 0: the stroke's ends are worked out here - its trim window and its dash runs, each end shaped by its cap -
// rather than taken from the contour's ends the vertices carry. Nothing is cut: the ribbon runs the whole contour, and a
// convex end paints over the gap past it.

// The running vertex's contour length - the closing segment included when it is closed - its contour's first point in
// Points and its own segment (its point), for the runs.
static float ContourLength;
static uint ContourFirst;
static uint SegmentIndex;

// How far along the contour the corners being emitted may say they are: a segment's own span, so the ribbon past a
// corner - a miter's point, a join - reads as the corner's point rather than as more of the segment; unbounded at an
// open contour's real ends, where the cap needs the room.
static float2 CornerSpan;

// Signed distance to a cap boundary (positive = painted) from u (along the end's axis), v (across) and arc (along the
// contour, which gates caps where the path folds back). hBite caps concave bites at mid-piece.
float CapSd(uint cap, float u, float v, float arc, float h, float hBite)
{
    if (cap == 6u) return 1e9;                                    // this end is not an end (closed loop)
    if (arc > h + Fringe) return 1e9;                             // farther along the contour than this cap can reach
    if (cap == 1u) return u + h;                                  // square: half a thickness of flat nub
    if (cap == 2u) return (u >= 0.0) ? 1e9 : h - length(float2(u, v));        // convex round: half-disc out
    if (cap == 3u) return (u >= 0.0) ? 1e9 : (u + h - abs(v)) * 0.70710678;   // convex triangle: tip out at the center
    if (cap == 4u) return (u - hBite + abs(v)) * 0.70710678;      // concave triangle: V notch cut in
    if (cap == 5u) return length(float2(max(u, 0.0), v)) - hBite; // concave round: half-disc bitten out
    return u;                                                     // flat
}

// What the runs below know of the point they are asked about: an end near the point is shaped in its own straight
// frame - where the end lies on the axis and which way the contour runs there - found in the point's contour in Points.
static float2 RunLocal;
static uint RunFirst;
static uint RunCount;
static bool RunClosed;
static float RunLength;

// Where the contour is at `arc` along it, and which way it runs there. The segment is found by halving over the
// points' distances along the contour - however finely a curve was cut, an end within reach is found. Round a closed
// contour, an end past its seam is the same place a length earlier.
void EndFrame(float arc, out float2 at, out float2 dir)
{
    float* arcs = Arcs;
    float2* points = Points + RunFirst;
    uint segments = RunClosed ? RunCount : RunCount - 1u;
    if (RunClosed && RunLength > 0.0)
    {
        arc -= floor(arc / RunLength) * RunLength;
    }

    uint lo = 0u;
    uint hi = segments - 1u;
    while (lo < hi)
    {
        uint mid = (lo + hi + 1u) / 2u;
        if (arcs[RunFirst + mid] <= arc)
        {
            lo = mid;
        }
        else
        {
            hi = mid - 1u;
        }
    }

    uint k = (lo + 1u) % RunCount;
    dir = normalize(points[k] - points[lo]);
    at = points[lo] + dir * (arc - arcs[RunFirst + lo]);
}

// One run of the stroke, from s to e along the contour, its ends wearing capS and capE: the signed distance to its
// boundary at `arc`, `v` across - its sides included: along the run they are the ribbon's, past an end they are that
// end's own, in its frame, so a cap reaching over the next segment is not cut by that segment's sides.
float RunSd(float arc, float v, float s, float e, uint capS, uint capE)
{
    float uS = arc - s;
    float vS = v;
    float uE = e - arc;
    float vE = v;
    float reach = HalfThickness + Fringe;
    // Which side of an end a point is on is the contour's to say; the end's frame only shapes it. Past a sharp corner the
    // frame of an end on the next segment would claim points of the segment before it.
    if (abs(arc - s) <= reach)
    {
        float2 at;
        float2 dir;
        EndFrame(s, at, dir);
        float2 r = RunLocal - at;
        float u = dot(r, dir);
        if ((u >= 0.0) == (arc >= s))
        {
            uS = u;
            vS = dot(r, float2(-dir.y, dir.x));
        }
    }

    if (abs(arc - e) <= reach)
    {
        float2 at;
        float2 dir;
        EndFrame(e, at, dir);
        float2 r = RunLocal - at;
        float u = -dot(r, dir);
        if ((u >= 0.0) == (arc <= e))
        {
            uE = u;
            vE = dot(r, float2(-dir.y, dir.x));
        }
    }

    float hBite = min(HalfThickness, 0.5 * (e - s));
    float sdS = CapSd(capS, uS, vS, 0.0, HalfThickness, hBite);
    float sdE = CapSd(capE, uE, vE, 0.0, HalfThickness, hBite);
    float side = uS < 0.0 ? HalfThickness - abs(vS) : uE < 0.0 ? HalfThickness - abs(vE) : HalfThickness - abs(v);
    return min(min(sdS, sdE), side);
}

// The signed distance to the nearest painted run at `arc`: the trim window - an open contour's whole length untrimmed,
// its ends the stroke's start and end caps - and, with a pattern, every dash run inside it, its ends the dash caps but
// where the window cuts it. A closed contour untrimmed has no window and no real end.
float RunsSd(float arc, float v, float contourLength, bool closed)
{
    bool windowed = !closed || TrimStart > 0.0 || TrimEnd < 1.0;
    float ws = windowed ? TrimStart * contourLength : -1e30;
    float we = windowed ? TrimEnd * contourLength : 1e30;
    if (TrimEnd <= TrimStart)
    {
        return -1e9;   // an empty window - a progress ring at nought - paints nothing, not two caps on one point
    }

    float* pattern = Pattern;
    uint runs = PatternCount % 2u == 1u ? PatternCount * 2u : PatternCount;
    float total = 0.0;
    for (uint k = 0u; k < runs; ++k) total += pattern[k % PatternCount] * DashScale;
    if (PatternCount == 0u || total <= 0.0)
    {
        return windowed ? RunSd(arc, v, ws, we, StartCap, EndCap) : 1e9;
    }

    // Where the period holding `arc` starts along the contour; its runs and those of as many periods either side as an
    // end's reach spans are near enough - more than one for tight dots on a thick line.
    float at = arc + DashOffset * DashScale;
    float periodStart = arc - (at - floor(at / total) * total);
    int reach = min(int(ceil((HalfThickness + Fringe) / total)) + 1, 8);
    float sd = -1e9;
    for (int n = -reach; n <= reach; ++n)
    {
        float start = periodStart + float(n) * total;
        for (uint k = 0u; k < runs; ++k)
        {
            float end = start + pattern[k % PatternCount] * DashScale;
            if ((k % 2u) == 0u && end > ws && start < we)
            {
                uint capS = start <= ws ? StartCap : DashStartCap;
                uint capE = end >= we ? EndCap : DashEndCap;
                sd = max(sd, RunSd(arc, v, max(start, ws), min(end, we), capS, capE));
            }

            start = end;
        }
    }

    return sd;
}

// A corner of the ribbon at the width drawn now; `along` is how far along its contour it is, for the runs.
PSInput Corner(float2 p, float perp, float arcA, float arcB, PieceFrame f, float pieceId, float along)
{
    float2 rA = p - f.A;
    float2 rB = p - f.B;
    PSInput o;
    o.Position = mul(float4(p, 0.0, 1.0), Wvp);   // row-vector convention (matches engine effects)
    o.Cap0 = float4(perp, dot(rA, f.dA), dot(rA, float2(-f.dA.y, f.dA.x)), arcA);
    o.Cap1 = float4(f.caps, -dot(rB, f.dB), dot(rB, float2(-f.dB.y, f.dB.x)), arcB);
    o.PieceId = fmod(pieceId + PieceSalt, 65536.0);
    o.Arc = along;
    o.Record = 0u;
    o.Contour = float2(ContourLength, float(ContourIsClosed));
    o.Local = p;
    o.Segment = uint3(ContourFirst, ContourPointCount, SegmentIndex);
    o.Span = CornerSpan;
    return o;
}

// A vertex of a triangle that draws nothing: all three of it fall on one point.
PSInput NoCorner(float2 p)
{
    return Corner(p, 0.0, 1e6, 1e6, NoCaps(), 0.0, 0.0);
}

// StrokePullVS: the stroke drawn straight from its points, 6 + 3 * RoundSegments vertices a point - the segment quad to
// the next point, then its join fan - each worked out here, so a stroke keeps only its points and draws at whatever width
// and scale it is drawn at.
[shader("vertex")]
PSInput StrokePullVS(uint vertexId : SV_VertexID)
{
    LoadUniforms();
    return PullCorner(vertexId);
}

// A batch of strokes: each draw is one record's, named by the draw's first instance. One instance a draw, so whether
// SV_InstanceID counts from the first instance or from zero, the larger of the two is the record.
[shader("vertex")]
PSInput StrokeBatchVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID,
    uint firstInstance : SV_StartInstanceLocation)
{
    uint record = max(instanceId, firstInstance);
    LoadRecord(record);
    PSInput o = PullCorner(vertexId);
    o.Record = record;
    return o;
}

PSInput PullCorner(uint vertexId)
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
    uint lastPoint = ContourPointCount - 1u;
    ContourLength = arcs[first + lastPoint] +
        (ContourIsClosed != 0u ? length(points[0] - points[lastPoint]) : 0.0);
    ContourFirst = first;
    SegmentIndex = i;
    CornerSpan = float2(-1e30, 1e30);
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

        // The segment quad to the next point, its corners in order: aP, aM, bP, bP, aM, bM.
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
        bool realStart = ContourIsClosed == 0u && i == 0u;
        bool realEnd = ContourIsClosed == 0u && ni + 1u == ContourPointCount;
        CornerSpan = float2(realStart ? -1e30 : along, realEnd ? 1e30 : along + segLen);
        return Corner(q, plus ? hw : -hw, atB ? back + segLen : back, atB ? fwd - segLen : fwd, frame,
            float((t * 2u) % 65536u), along + dot(q - p, segDir));
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
    // A join is its corner's: it reads as the corner's point, whole where a run carries the corner, and its edges are
    // shaped by the ends near it in their own frames.
    float2 bisector = normalize(dIn + dOut + float2(1e-12, 0.0));
    CornerSpan = float2(along, along);
    if (JoinType == 2u && !isStart && !isEnd && JoinIsVisible(dIn, dOut))
    {
        if (v == 0u)
        {
            return Corner(p, 0.0, back, fwd, frame, fanId, along);
        }

        float angle = 6.28318530717958647692 * float(v == 1u ? tri : tri + 1u) / float(RoundSegments);
        float2 r = hw * float2(cos(angle), sin(angle));
        return Corner(p + r, hw, back + dot(r, dIn), fwd - dot(r, dOut), frame, fanId, along + dot(r, bisector));
    }

    if (JoinType == 1u && hasSegment && !isStart && tri < 2u)
    {
        if (v == 2u)
        {
            return Corner(p, 0.0, back, fwd, frame, fanId, along);
        }

        float side = tri == 0u ? 1.0 : -1.0;
        float2 n = v == 0u ? SegmentNormal(points[prev], p) : SegmentNormal(p, points[next]);
        float2 r = n * hw * side;
        return Corner(p + r, hw * side, back, fwd, frame, fanId, along + dot(r, bisector));
    }

    return NoCorner(p);
}

// Analytic AA coverage from the nearest boundary (edges and caps), feathered over one pixel as wide as it is here;
// Fringe == 0 gives hard edges. Shared by all three passes so their depths match bit for bit.
float StrokeCoverage(PSInput input)
{
    // A pixel's width across the stroke, in local units, where this pixel is - read from the screen, not given: under a
    // perspective it is not the same from one end of the stroke to the other.
    float across = length(float2(ddx(input.Cap0.x), ddy(input.Cap0.x)));
    float sd;
    if (Runs != 0u)
    {
        RunLocal = input.Local;
        RunFirst = input.Segment.x;
        RunCount = input.Segment.y;
        RunClosed = input.Contour.y > 0.5;
        RunLength = input.Contour.x;
        // The runs carry their own sides; a closed contour with no pattern and no trim has none, only the ribbon's.
        float arc = clamp(input.Arc, input.Span.x, input.Span.y);
        sd = RunsSd(arc, input.Cap0.x, input.Contour.x, input.Contour.y > 0.5);
        if (sd > 1e8) sd = HalfThickness - abs(input.Cap0.x);
    }
    else
    {
        uint caps = uint(round(input.Cap1.x));
        float hBite = min(HalfThickness, 0.5 * (input.Cap0.w + input.Cap1.w));
        sd = HalfThickness - abs(input.Cap0.x);
        sd = min(sd, CapSd(caps % 8u, input.Cap0.y, input.Cap0.z, input.Cap0.w, HalfThickness, hBite));
        sd = min(sd, CapSd(caps / 8u, input.Cap1.y, input.Cap1.z, input.Cap1.w, HalfThickness, hBite));
    }

    return Fringe > 0.0 ? saturate(sd / max(across, 1e-6) + 0.5) : (sd >= 0.0 ? 1.0 : 0.0);
}

// Depth = coverage in the high 8 bits and piece id in the low 16, exact in float32, so max coverage wins and ties never
// blend twice.
float CoverageDepth(float coverage, float pieceId)
{
    float covered = round(saturate(1.0 - coverage) * 255.0);
    float serial = clamp(round(pieceId), 0.0, 65535.0);
    return (covered * 65536.0 + serial) / 16777216.0;
}

// Where this pixel is in the brush's box, and the box.
void BrushPoint(PSInput input, out float2 p, out float4 box)
{
    if (BrushAlong != 0u)
    {
        float arc = clamp(input.Arc, input.Span.x, input.Span.y);
        float across = input.Cap0.x;
        if (input.Span.x == input.Span.y)
        {
            // A join: its disc reaches over both segments' ribbon, so a point on it reads as the segment it lies along -
            // as far along and across as that segment says - and as the corner itself only in the wedge between them.
            float2* points = Points + input.Segment.x;
            uint count = input.Segment.y;
            uint i = input.Segment.z;
            float2 corner = points[i];
            float2 dIn = normalize(corner - points[(i + count - 1u) % count]);
            float2 dOut = normalize(points[(i + 1u) % count] - corner);
            float2 r = input.Local - corner;
            float ahead = dot(r, dOut);
            float behind = dot(r, dIn);
            if (ahead > 0.0)
            {
                arc += ahead;
                across = dot(r, float2(-dOut.y, dOut.x));
            }
            else if (behind < 0.0)
            {
                arc += behind;
                across = dot(r, float2(-dIn.y, dIn.x));
            }
            else
            {
                float2 outward = float2(-dIn.y, dIn.x) + float2(-dOut.y, dOut.x);
                across = dot(r, outward) >= 0.0 ? length(r) : -length(r);
            }
        }

        p = float2(arc, across + HalfThickness);
        box = float4(0.0, 0.0, max(input.Contour.x, 1e-4), max(2.0 * HalfThickness, 1e-4));
    }
    else
    {
        p = input.Local;
        box = BrushBounds;
    }
}

// The stroke's color at this pixel. `family` is a literal from the entry point - 0 a solid color or a gradient, 1 a
// pattern or noise, 2 a picture, 3 a picture in a layer of a texture array - so each pass keeps only its own brush.
float4 StrokeColorAt(PSInput input, int family)
{
    if (family == 0 && IsGradient == 0)
    {
        return StrokeColor;
    }

    float2 p;
    float4 box;
    BrushPoint(input, p, box);
    if (family == 1)
    {
        PatternFill it;
        it.Params = PParams;
        it.Color1 = PColor1;
        it.Color2 = PColor2;
        it.Color3 = PColor3;
        it.Noise = PNoise;
        it.Anim = PAnim;
        return PatternFillColor(it, int(PParams.y), p - box.xy, p - (box.xy + box.zw * 0.5), max(box.w * 0.5, 1e-4));
    }

    if (family >= 2)
    {
        float inside;
        // Along the line one copy takes TAlongLength of it, at the stroke's width - the feather past either side reads the
        // picture's edge rather than nothing; over the bounds, the box is the picture's.
        float2 t = BrushAlong != 0u ? float2(p.x / max(TAlongLength, 1e-4), saturate(p.y / box.w))
                                    : (p - box.xy) / box.zw;
        float2 uv = TileSourceUv(t, TRotation, TTile, TDrawn, TUvRect, TRepeat, TMirror, inside);
        // SampleLevel, not Sample: frac() makes uv discontinuous at every tile seam, which would pick the smallest mip.
        float4 color = family == 3 ? SourceTextureArray.SampleLevel(SourceSampler, float3(uv, TLayer), 0.0)
                                   : SourceTexture.SampleLevel(SourceSampler, uv, 0.0);
        color.rgb = AdjustColor(color.rgb, TAdjust);
        color *= TTint;
        color.a *= inside;
        return color;
    }

    return GradientColorIn(p, box);
}

float4 StrokeShade(PSInput input, int family)
{
    float4 c = StrokeColorAt(input, family);
    c.a *= StrokeCoverage(input);
    return c;
}

[shader("fragment")] float4 StrokePS(PSInput input) : SV_Target { LoadUniforms(); return StrokeShade(input, 0); }
[shader("fragment")] float4 StrokePatternPS(PSInput input) : SV_Target { LoadUniforms(); return StrokeShade(input, 1); }
[shader("fragment")] float4 StrokeTexturePS(PSInput input) : SV_Target { LoadUniforms(); return StrokeShade(input, 2); }
[shader("fragment")] float4 StrokeLayerPS(PSInput input) : SV_Target { LoadUniforms(); return StrokeShade(input, 3); }

// A batched stroke's record, loaded for its fragment, and what its element's fade and rounded clip leave of it.
float LoadBatched(PSInput input)
{
    StrokeRecord r = LoadRecord(input.Record);
    NodeSlot* nodes = Transforms;
    int fadeSlot = int(r.Window.w);
    float fade = fadeSlot >= 0 ? nodes[fadeSlot].Params.x : 1.0;
    return fade * ClipCoverageBySlot(input.Position.xy, r.Extra.x);
}

[shader("fragment")]
float4 StrokeBatchPS(PSInput input) : SV_Target
{
    float kept = LoadBatched(input);
    float4 c = StrokeShade(input, 0);
    c.a *= kept;
    return c;
}

// Union pass 1: depth only, under Less over bounds the caller cleared to 1.0. Afterwards each pixel holds the depth of
// the single fragment with the greatest coverage over it - the coverage of the UNION.
[shader("fragment")]
float StrokeUnionDepthPS(PSInput input) : SV_Depth
{
    LoadUniforms();
    return CoverageDepth(StrokeCoverage(input), input.PieceId);
}

[shader("fragment")]
float StrokeBatchUnionDepthPS(PSInput input) : SV_Depth
{
    LoadBatched(input);
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
UnionOutput StrokeUnionShade(PSInput input, int family)
{
    // No discard for zero coverage: it would pull in DemoteToHelperInvocation, and it buys nothing - such a fragment
    // paints an alpha of zero anyway.
    float coverage = StrokeCoverage(input);

    UnionOutput o;
    o.Color = StrokeColorAt(input, family);
    o.Color.a *= coverage;
    o.Depth = CoverageDepth(coverage, input.PieceId);
    return o;
}

[shader("fragment")] UnionOutput StrokeUnionDrawPS(PSInput input) { LoadUniforms(); return StrokeUnionShade(input, 0); }
[shader("fragment")] UnionOutput StrokePatternUnionDrawPS(PSInput input) { LoadUniforms(); return StrokeUnionShade(input, 1); }
[shader("fragment")] UnionOutput StrokeTextureUnionDrawPS(PSInput input) { LoadUniforms(); return StrokeUnionShade(input, 2); }
[shader("fragment")] UnionOutput StrokeLayerUnionDrawPS(PSInput input) { LoadUniforms(); return StrokeUnionShade(input, 3); }

[shader("fragment")]
UnionOutput StrokeBatchUnionDrawPS(PSInput input)
{
    float kept = LoadBatched(input);
    UnionOutput o = StrokeUnionShade(input, 0);
    o.Color.a *= kept;
    return o;
}

technique Stroke
{
    // Draw: an opaque stroke, one pass.
    pass Draw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokePS;
    }

    // UnionDepth / UnionDraw: the two passes a TRANSLUCENT stroke takes so its self-overlaps blend once.
    pass UnionDepth
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokeUnionDepthPS;
    }

    pass UnionDraw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokeUnionDrawPS;
    }

    // The same for a pattern or noise brush, and for a picture; UnionDepth is shared, as it reads no color.
    pass PatternDraw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokePatternPS;
    }

    pass PatternUnionDraw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokePatternUnionDrawPS;
    }

    pass TextureDraw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokeTexturePS;
    }

    pass TextureUnionDraw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokeTextureUnionDrawPS;
    }

    pass LayerDraw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokeLayerPS;
    }

    pass LayerUnionDraw
    {
        VertexShader = StrokePullVS;
        PixelShader = StrokeLayerUnionDrawPS;
    }

    // A batch of solid strokes, one draw a record: one pass when they are opaque, the union's two when they show through.
    pass BatchDraw
    {
        VertexShader = StrokeBatchVS;
        PixelShader = StrokeBatchPS;
    }

    pass BatchUnionDepth
    {
        VertexShader = StrokeBatchVS;
        PixelShader = StrokeBatchUnionDepthPS;
    }

    pass BatchUnionDraw
    {
        VertexShader = StrokeBatchVS;
        PixelShader = StrokeBatchUnionDrawPS;
    }
}
