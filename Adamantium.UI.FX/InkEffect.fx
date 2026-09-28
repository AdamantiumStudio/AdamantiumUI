// INK - each stroke's coverage is decided against its whole polyline, so translucent ink blends once without darkened
// joints. One buffer: a header record per stroke followed by its point records.

#include "Includes/CommonData.fxh"
#include "Includes/ClipMath.fxh"
#include "Includes/ShapeMath.fxh"

// One record. All float4, and the same shape for both kinds - a buffer of one type is a buffer the draw can index
// without knowing which kind a slot holds.
struct InkRecord
{
    // HEADER: .xy the box's low corner, .zw its high corner - NODE-local. POINT: .xy the point, .zw unused.
    float4 Segment;
    // HEADER: .x half width (node-local), .y transform slot, .z opacity slot, .w 1 = header. POINT: .w 0.
    float4 Params;
    // HEADER: .x rounded-clip slot or -1, .y how many points, .z how far past THIS record the first of them is, .w spare.
    float4 Clip;
    float4 Color;     // straight RGBA, opacity folded into the alpha
};

struct InkPSInput
{
    float4 Position : SV_Position;
    float2 Local    : TEXCOORD0;   // the fragment in NODE-local units - the space the points are stated in
    nointerpolation float2 Shape   : TEXCOORD1;   // .x half width (local), .y device pixels per local unit
    nointerpolation uint InstId    : TEXCOORD2;
    nointerpolation float Fade     : TEXCOORD3;
    nointerpolation float4 ClipBox   : TEXCOORD4;
    nointerpolation float4 ClipRadii : TEXCOORD5;
};

[shader("vertex")]
InkPSInput InkSegmentVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    InkRecord* items = (InkRecord*)InstancesAddress;
    InkRecord it = items[instanceId];

    InkPSInput o;

    // A POINT record draws nothing: every corner to the same place, so the quad has no area and no fragment is raised.
    if (it.Params.w < 0.5)
    {
        o.Position = float4(2.0, 2.0, 0.0, 1.0);
        o.Local = float2(0.0, 0.0);
        o.Shape = float2(0.0, 1.0);
        o.InstId = instanceId;
        o.Fade = 0.0;
        o.ClipBox = float4(0.0, 0.0, 0.0, 0.0);
        o.ClipRadii = float4(0.0, 0.0, 0.0, 0.0);
        return o;
    }

    NodeSlot* nodes = (NodeSlot*)TransformsAddress;
    float4x4 nodeWorld = nodes[(uint)it.Params.y].World;
    float2 px = SlotPixelScale(nodeWorld);
    float iso = min(px.x, px.y);

    // This record's segment box, grown by the radius plus one device pixel so the antialiased edge is not clipped.
    float grow = it.Params.x + 1.0 / max(iso, 1e-6);
    float2 low = min(it.Segment.xy, it.Segment.zw) - grow;
    float2 high = max(it.Segment.xy, it.Segment.zw) + grow;

    float2 corner = float2(vertexId & 1u, (vertexId >> 1u) & 1u);
    float2 localPos = lerp(low, high, corner);

    o.Position = mul(mul(float4(localPos, 0.0, 1.0), nodeWorld), Projection);

    o.Local   = localPos;
    o.Shape   = float2(it.Params.x, iso);
    o.InstId  = instanceId;

    int fadeSlot = int(it.Params.z);
    o.Fade = lerp(1.0, nodes[max(fadeSlot, 0)].Params.x, step(0.0, float(fadeSlot)));
    o.ClipBox   = ClipShapeBox(it.Clip.x);
    o.ClipRadii = ClipShapeRadii(it.Clip.x);
    return o;
}

// Distance from a point to a SEGMENT - the whole shape, in one expression. The clamp is what makes the ends round.
float DistanceToSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float t = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-12));

    return length(pa - ba * t);
}

[shader("pixel")]
float4 InkSegmentPS(InkPSInput i) : SV_Target
{
    InkRecord* items = (InkRecord*)InstancesAddress;
    InkRecord it = items[i.InstId];

    // Both offsets are counted FROM THIS RECORD, because the draw bases the buffer at the run it is flushing - see the
    // note where the header is written.
    // SIGNED, because every record but the first of a stroke sits AFTER the point it counts from.
    uint count = (uint)it.Clip.y;
    uint first = (uint)((int)i.InstId + (int)it.Clip.z);

    // The nearest segment of the whole polyline: where segment quads overlap, only the quad owning it keeps the pixel,
    // so the stroke blends once.
    float nearest = 1e30;
    uint owner = 0u;

    // NEARER BY A MARGIN, not merely nearer: each segment's quad interpolates its own position for the pixel it
    // covers, and those agree only to floating-point - so two fragments comparing all-but-equal distances could name
    // different owners, and the pixel would be drawn twice or not at all. A thousandth of a device pixel is far above
    // that error and far below anything the coverage can express.
    float tie = 1e-3 / max(i.Shape.y, 1e-6);
    float2 at = i.Local;
    float2 previous = items[first].Segment.xy;

    // A stroke of one point is a dot: the loop below does not execute and the distance is to that point alone.
    if (count == 1u) nearest = length(at - previous);

    for (uint s = 1u; s < count; s++)
    {
        float2 current = items[first + s].Segment.xy;
        float away = DistanceToSegment(at, previous, current);

        // STRICTLY nearer, so a tie goes to the earlier segment. Every segment's fragment measures from the same
        // rebuilt position and walks the same points in the same order, so they all name the same winner - which is
        // what makes `exactly one of us draws` true rather than likely.
        if (away < nearest - tie)
        {
            nearest = away;
            owner = s - 1u;
        }

        previous = current;
    }

    // NOT MINE: some other segment of this stroke is nearer to this pixel, and that one is drawing it.
    if (owner != (uint)max(it.Params.w - 1.0, 0.0)) discard;

    // In DEVICE pixels, so the fade is one pixel wide however far the camera is zoomed - which keeps the edge of the
    // ink the same softness at every scale instead of blurring as it grows.
    float distance = (nearest - i.Shape.x) * i.Shape.y;
    float coverage = saturate(0.5 - distance);
    if (coverage <= 0.0) discard;

    // NOT named "color". A pixel-stage local by that name makes this compile into a shader that loses the device -
    // measured 6 starts of 6, against 0 of 6 for the same code under any other name. HLSL semantics are matched
    // case-insensitively and COLOR is a legacy pixel-stage output semantic, so the front end evidently treats the name
    // as one. Nothing else about the pass changes; only the name does.
    float4 tint = it.Color;
    tint.a *= coverage * i.Fade * ClipCoverage(i.Position.xy, i.ClipBox, i.ClipRadii);
    if (tint.a <= 0.0) discard;

    // STRAIGHT, not premultiplied: the blend here is SrcAlpha/OneMinusSrcAlpha, so it does the multiply itself.
    return float4(tint.rgb, tint.a);
}

// =====================================================================================================================
// TECHNIQUE - one pass. Ink is one thing done one way; what varies (where, how wide, what color) varies per instance.
// =====================================================================================================================
technique Ink
{
    pass Segments
    {
        VertexShader = InkSegmentVS;
        PixelShader = InkSegmentPS;
    }
}
