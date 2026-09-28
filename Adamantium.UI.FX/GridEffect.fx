// THE CANVAS GRID - an unbounded plane of marks decided per fragment on one quad: analytic line coverage at any zoom,
// and adjacent step levels cross-fade.

#include "Includes/CommonData.fxh"
#include "Includes/ClipMath.fxh"
#include "Includes/ShapeMath.fxh"

// One grid instance. All float4 - there is exactly ONE of these in a draw, so packing colors into bytes would buy
// nothing and cost the question of how the two of them align against the fields after.
struct CanvasGridData
{
    float4 Bounds;     // NODE-local x, y, w, h
    float4 Params;     // .x transform slot, .y marks (1 dots, 2 lines), .z opacity slot, .w mark size (logical px)
    float4 Camera;     // .xy the lattice's PHASE within one cell (logical px) - NEVER the distance travelled, which a
                       // float cannot carry; .z screen px per world unit, .w spare
    float4 Step;       // .x the step to draw (world units, ALREADY coarsened); .y coarsening; .z 1 / the pitch a mark
                       // must keep on screen, as a RECIPROCAL - the shader must not divide; .w spare
    float4 Clip;       // .x the ancestor's rounded-clip slot, or -1; .yz where the world's ORIGIN sits (logical px),
                       // penned in to the element's reach - the axes' own place, which the phase cannot say
    float4 Background; // the GROUND, straight RGBA - carried here because the grid is flushed first of its clip group
    float4 GridColor;  // straight RGBA
    float4 AxisColor;  // straight RGBA; alpha 0 leaves the world's axes undrawn
};

struct GridPSInput
{
    float4 Position : SV_Position;
    float2 Local    : TEXCOORD0;   // fragment in the ELEMENT's own logical units - the space the camera is stated in
    nointerpolation uint InstId : TEXCOORD1;
    nointerpolation float Scale : TEXCOORD2;   // device pixels per logical unit
    nointerpolation float Fade  : TEXCOORD3;
    nointerpolation float4 ClipBox   : TEXCOORD4;
    nointerpolation float4 ClipRadii : TEXCOORD5;
};

[shader("vertex")]
GridPSInput CanvasGridVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    CanvasGridData* items = (CanvasGridData*)InstancesAddress;
    CanvasGridData it = items[instanceId];

    GridPSInput o;
    float2 corner = float2(vertexId & 1u, (vertexId >> 1u) & 1u);
    NodeSlot* nodes = (NodeSlot*)TransformsAddress;
    float4x4 nodeWorld = nodes[(uint)it.Params.x].World;
    float2 px = SlotPixelScale(nodeWorld);

    // No outset: the grid has no pen and must not paint one pixel outside the element - it is the ground the canvas
    // stands on, and the canvas clips to its bounds.
    float2 localPos = it.Bounds.xy + corner * it.Bounds.zw;
    float4 worldPos = mul(float4(localPos, 0.0, 1.0), nodeWorld);

    o.Position = mul(worldPos, Projection);
    // RELATIVE to the element, not to the node: the camera is stated in the canvas's own coordinates, and that is the
    // only space in which "where the origin sits" means anything.
    o.Local  = localPos - it.Bounds.xy;
    o.InstId = instanceId;
    o.Scale  = min(px.x, px.y);
    int fadeSlot = int(it.Params.z);
    o.Fade = lerp(1.0, nodes[max(fadeSlot, 0)].Params.x, step(0.0, float(fadeSlot)));
    o.ClipBox   = ClipShapeBox(it.Clip.x);
    o.ClipRadii = ClipShapeRadii(it.Clip.x);
    return o;
}

// How much of this pixel a mark of the given half-width covers, given the distance to the nearest one. Both in DEVICE
// pixels, so the ramp is one pixel wide by construction - which is what makes a line exactly its width at any zoom
// rather than snapping between one pixel and two.
float MarkCoverage(float distancePx, float halfWidthPx)
{
    return saturate(halfWidthPx + 0.5 - distancePx);
}

// Distance in DEVICE pixels from this fragment to the nearest line of the given step, per axis.
float2 DistanceToLines(float2 world, float step, float pixelsPerUnit)
{
    float2 cell = world / step;
    return abs(frac(cell + 0.5) - 0.5) * step * pixelsPerUnit;
}

// The marks: lines take the nearer of the two axes, dots want both at once - a dot is where the two crossings meet,
// which is the product and not the maximum.
float LevelCoverage(float2 world, float step, float pixelsPerUnit, float halfWidthPx, float marks)
{
    float2 d = DistanceToLines(world, step, pixelsPerUnit);
    float cx = MarkCoverage(d.x, halfWidthPx);
    float cy = MarkCoverage(d.y, halfWidthPx);

    return marks > 1.5 ? max(cx, cy) : cx * cy;
}

[shader("pixel")]
float4 CanvasGridPS(GridPSInput i) : SV_Target
{
    CanvasGridData* items = (CanvasGridData*)InstancesAddress;
    CanvasGridData it = items[i.InstId];

    float marks = it.Params.y;

    // Everything below is in DEVICE pixels: the camera is stated in logical ones, and Scale is how many device pixels a
    // logical unit is worth. Doing it once here is what keeps the grid the same weight on a high-DPI monitor.
    float pixelsPerUnit = it.Camera.z * i.Scale;

    // Camera.xy is the lattice phase within one cell, not the camera distance, so floats keep sub-pixel precision far out.
    float2 world = (i.Local - it.Camera.xy) / max(it.Camera.z, 1e-6);

    // The step arrives ALREADY COARSENED - the canvas works it out from the camera and hands it over, which it has to
    // do anyway for rulers and snapping; computing it twice is how the two come to disagree.
    float step = max(it.Step.x, 1e-6);
    float halfWidth = max(it.Params.w * i.Scale, 0.5) * 0.5;

    // Two readable levels, the step and one coarser; the main fades near the minimum pitch as the accent takes over. In
    // logical pixels, using the reciprocal the canvas sends.
    float mainPitch = step * it.Camera.z;
    float blend = saturate(mainPitch * it.Step.z - 1.0);
    float stepAccent = step * max(it.Step.y, 2.0);

    // marks < 0.5 means none at all: the ground still gets painted, because the grid IS the canvas's ground and turning
    // the marks off must not turn the canvas transparent.
    float coverage = 0.0;
    if (marks >= 0.5)
    {
        coverage = LevelCoverage(world, stepAccent, pixelsPerUnit, halfWidth, marks);
        coverage = max(coverage, LevelCoverage(world, step, pixelsPerUnit, halfWidth, marks) * blend);
    }

    float4 marksColor = it.GridColor;
    marksColor.a *= coverage;

    // The marks over the ground in one composite, so the element is one draw.
    float4 composited = float4(lerp(it.Background.rgb, marksColor.rgb, marksColor.a),
                           it.Background.a + marksColor.a * (1.0 - it.Background.a));

    // The world's OWN axes, over the grid: on a plane with no edges they are the only thing that says where the origin
    // is. Drawn as full lines whatever the marks are - an axis made of dots would not read as an axis.
    if (it.AxisColor.a > 0.0)
    {
        // FROM THE AXES' OWN PLACE, not from the phase: the phase says where the lattice stands, and every cell of a
        // lattice looks like every other - an axis worked out from it would be drawn once per cell. Clip.yz is where
        // the origin sits, penned in to the element's reach so the number stays exact.
        float2 axisDistance = abs(i.Local - it.Clip.yz) * i.Scale;
        float onAxis = max(MarkCoverage(axisDistance.x, halfWidth), MarkCoverage(axisDistance.y, halfWidth));
        float4 axis = it.AxisColor;
        axis.a *= onAxis;
        // OVER, not added: where an axis crosses a grid line the two must not brighten each other into a knot.
        composited = float4(lerp(composited.rgb, axis.rgb, axis.a), composited.a + axis.a * (1.0 - composited.a));
    }

    composited.a *= i.Fade * ClipCoverage(i.Position.xy, i.ClipBox, i.ClipRadii);
    if (composited.a <= 0.0) discard;

    // STRAIGHT, not premultiplied - the blend is SrcAlpha/OneMinusSrcAlpha and does the multiply itself. Every other
    // pass in the engine returns straight for the same reason.
    return float4(composited.rgb, composited.a);
}

// =====================================================================================================================
// TECHNIQUE - one pass. A grid is one thing done one way; what varies (dots or lines, how fine, what color) varies per
// instance and not per shader.
// =====================================================================================================================
technique CanvasGrid
{
    pass Marks
    {
        VertexShader = CanvasGridVS;
        PixelShader = CanvasGridPS;
    }
}
