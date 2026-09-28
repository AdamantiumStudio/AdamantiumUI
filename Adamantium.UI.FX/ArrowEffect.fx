// ARROW - a shaft and up to two heads drawn analytically as one shape: one instance per arrow, no geometry, crisp at
// any zoom, and no mitre spike at the tip.

#include "Includes/CommonData.fxh"
#include "Includes/ClipMath.fxh"
#include "Includes/ShapeMath.fxh"

// One record, one arrow. All float4, like every other batch record.
struct ArrowRecord
{
    float4 Ends;      // .xy the start, .zw the end - NODE-local
    float4 Params;    // .x half thickness (local), .y transform slot, .z opacity slot, .w head length (local)
    float4 Head;      // .x head half width (local), .y what sits on the start, .z what sits on the end, .w spare
    float4 Clip;      // .x rounded-clip slot or -1, .yzw spare
    float4 Color;     // straight RGBA, opacity folded into the alpha
};

struct ArrowPSInput
{
    float4 Position : SV_Position;
    float2 Local    : TEXCOORD0;   // the fragment in NODE-local units - the space the ends are stated in
    nointerpolation float2 Shape   : TEXCOORD1;   // .x half thickness (local), .y device pixels per local unit
    nointerpolation uint InstId    : TEXCOORD2;
    nointerpolation float Fade     : TEXCOORD3;
    nointerpolation float4 ClipBox   : TEXCOORD4;
    nointerpolation float4 ClipRadii : TEXCOORD5;
};

[shader("vertex")]
ArrowPSInput ArrowVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    ArrowRecord* items = (ArrowRecord*)InstancesAddress;
    ArrowRecord it = items[instanceId];

    ArrowPSInput o;

    NodeSlot* nodes = (NodeSlot*)TransformsAddress;
    float4x4 nodeWorld = nodes[(uint)it.Params.y].World;
    float2 px = SlotPixelScale(nodeWorld);
    float iso = min(px.x, px.y);

    // The box the whole arrow covers: the two ends, grown by whichever reaches further - half the thickness, or half
    // the head's width - plus one DEVICE pixel in local units so the analytic edge has somewhere to fade. A quad cut to
    // the exact shape clips the fade and leaves a hard, aliased rim.
    float reach = max(it.Params.x, it.Head.x) + 1.0 / max(iso, 1e-6);
    float2 low = min(it.Ends.xy, it.Ends.zw) - reach;
    float2 high = max(it.Ends.xy, it.Ends.zw) + reach;

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

// Distance from a point to a SEGMENT. The clamp is what makes the ends round, and a capsule is this minus a radius.
float ArrowDistanceToSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float t = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-12));

    return length(pa - ba * t);
}

// The pixel shader below is flat and branchless: both head forms are measured and the unwanted ones masked with
// step/lerp.

[shader("pixel")]
float4 ArrowPS(ArrowPSInput i) : SV_Target
{
    ArrowRecord* items = (ArrowRecord*)InstancesAddress;
    ArrowRecord it = items[i.InstId];

    float2 from = it.Ends.xy;
    float2 to = it.Ends.zw;
    float edge = i.Shape.x;
    float wide = it.Head.x;

    // Which way the arrow runs, once. Both heads are measured out from this - one forwards, one backwards - so there is
    // no direction worked out twice and no way for the two ends to disagree about where the axis is.
    float2 axis = to - from;
    float span = max(length(axis), 1e-6);
    float2 along = axis / span;
    float2 across = float2(-along.y, along.x);

    // Never longer than the line it sits on: on a very short arrow a head of the stated length would start behind the
    // tail and point the wrong way.
    float back = min(it.Params.w, span);

    // WHICH kind, as numbers rather than as branches. 1 is barbs, 2 is a filled triangle, anything below a half is
    // nothing at all - and "nothing" is a distance far outside any quad rather than a path not taken.
    float endWanted = step(0.5, it.Head.z);
    float endSolid = step(1.5, it.Head.z);
    float startWanted = step(0.5, it.Head.y);
    float startSolid = step(1.5, it.Head.y);

    // Where the shaft stops: at a triangle's base, or for barbs just short of the tip, where its round end fits inside
    // the narrowing head.
    float tuck = edge / max(wide * rsqrt(max(it.Params.w * it.Params.w + wide * wide, 1e-12)), 1e-6);

    float endBack = min(lerp(tuck, it.Params.w, endSolid), span);
    float startBack = min(lerp(tuck, it.Params.w, startSolid), span);

    float2 shaftFrom = lerp(from, from + along * startBack, startWanted);
    float2 shaftTo = lerp(to, to - along * endBack, endWanted);

    // The SHAFT and both HEADS in one distance, so the whole arrow blends once. Drawn as three shapes it composited
    // three times wherever they overlap, which a translucent arrow shows as dark seams at the head.
    float nearest = ArrowDistanceToSegment(i.Local, shaftFrom, shaftTo) - edge;

    // Each head in its own coordinates (back from the tip, unsigned distance off the axis), so mirrored barbs and
    // triangle sides fold into one segment.
    float reach = max(back, 1e-6);
    float slant = rsqrt(max(reach * reach + wide * wide, 1e-12));

    float2 endAt = float2(dot(to - i.Local, along), abs(dot(i.Local - to, across)));
    float2 startAt = float2(dot(i.Local - from, along), abs(dot(i.Local - from, across)));

    // One slanted line gives both heads: a triangle is inside it back to the base, barbs are a band just inside it,
    // closing to a sharp point.
    float endEdge = (endAt.y * reach - endAt.x * wide) * slant;
    float endBase = endAt.x - reach;
    float endFilled = max(endEdge, endBase);
    // HALF a thickness inward, not a whole one. A stroked V straddles its line - half the width inside the head, half
    // outside - so half is what it eats of the notch. Taken as a full thickness inward, the barb closed the notch to
    // almost nothing at these proportions: the head is only about a thickness and a fifth deep, so a barbed head and a
    // filled one came out the same shape.
    float endBarbed = max(max(endEdge, -(endEdge + edge)), endBase);
    float endHead = lerp(endBarbed, endFilled, endSolid);

    float startEdge = (startAt.y * reach - startAt.x * wide) * slant;
    float startBase = startAt.x - reach;
    float startFilled = max(startEdge, startBase);
    float startBarbed = max(max(startEdge, -(startEdge + edge)), startBase);
    float startHead = lerp(startBarbed, startFilled, startSolid);

    // FAR, not infinite: a head nobody asked for is pushed a million units away, which is past any quad and still a
    // number that arithmetic behaves with.
    nearest = min(nearest, lerp(1e6, endHead, endWanted));
    nearest = min(nearest, lerp(1e6, startHead, startWanted));

    // In DEVICE pixels, so the fade is one pixel wide however far the camera is zoomed.
    float distance = nearest * i.Shape.y;
    float coverage = saturate(0.5 - distance);
    if (coverage <= 0.0) discard;

    // NOT named "color". A pixel-stage local by that name makes this compile into a shader that loses the device - see
    // the note in InkEffect.fx. HLSL matches semantics case-insensitively and COLOR is a legacy pixel-stage output.
    float4 tint = it.Color;
    tint.a *= coverage * i.Fade * ClipCoverage(i.Position.xy, i.ClipBox, i.ClipRadii);
    if (tint.a <= 0.0) discard;

    // STRAIGHT, not premultiplied: the blend here is SrcAlpha/OneMinusSrcAlpha, so it does the multiply itself.
    return float4(tint.rgb, tint.a);
}

// =====================================================================================================================
// TECHNIQUE - one pass. An arrow is one thing done one way; what varies (where, how thick, what head, what color)
// varies per instance.
// =====================================================================================================================
technique Arrow
{
    pass Run
    {
        VertexShader = ArrowVS;
        PixelShader = ArrowPS;
    }
}
