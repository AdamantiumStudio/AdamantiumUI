// THE BRUSHES: computed or sampled fills (gradients, patterns, noise, textures, fractals). One technique per family, one
// pass per carrier (Sdf, Mesh, Curve, Fringe); shares CommonData.fxh with BatchEffect.fx.

// In dependency order - each header builds on the ones above it, and BrushData is last because everything in it is
// built from the other four. NOTHING after the path on these lines: a trailing comment on an #include stops the
// preprocessor with "unexpected tokens after directive".
#include "Includes/CommonData.fxh"
#include "Includes/ClipMath.fxh"
#include "Includes/ShapeMath.fxh"
#include "Includes/Ring.fxh"
#include "Includes/StrokeMath.fxh"
#include "Includes/NoiseMath.fxh"
#include "Includes/PatternMath.fxh"
#include "Includes/TileMath.fxh"
#include "Includes/BrushData.fxh"
#include "Includes/GradientMath.fxh"
#include "Includes/ColorMath.fxh"
#include "Includes/CurveMath.fxh"
#include "Includes/CurveTiles.fxh"

// The draw's instances, by the kind it draws.
GradientRectData* GradientRects;
GradGeomData* GradientGeometries;
PatternRectData* PatternRects;
PatternGeomData* PatternGeometries;
TexRectData* TextureRects;
TextureGeomData* TextureGeometries;
FractalRectData* FractalRects;

// GPU-resident FRACTAL REFERENCE ORBITS (perturbation deep-zoom): every deep-zoom fractal instance's reference orbit Z_n
// concatenated. Each FractalRectData.Ref.x is this instance's START INDEX into it and .y the length. Null when no
// deep-zoom fractal is live - the shader only dereferences it on the deep path.
float4* Orbits;

// ---- GradientRect: the SDF rect batch filled with a linear or radial gradient (up to 8 stops) per fragment. Matches
// the CPU GradientRectItem; strokes via CompositeFillStroke.
struct GradientRectData
{
    float4 Bounds;       // world x, y, w, h
    float4 Params;       // .x LARGEST corner radius, .y type (1 linear/2 radial/3 conic), .z stop count, .w spread (0 pad/1 reflect/2 repeat)
    float4 Radii;        // corner radii: x = TL, y = TR, z = BR, w = BL
    float4 Geom0;        // LOCAL 0..1: linear (startXY, endXY) | radial (centerXY, radiusXY)
    float4 Geom1;        // radial focal (originXY, _, _); unused for linear
    uint8_t4 StrokeColor; // straight stroke RGBA in four bytes (alpha 0 -> no stroke)
    float4 Stroke0;      // width_px, align, dashOn, dashGap
    float4 Stroke1;      // dashOffset, trimStart, trimEnd, flags
    float4 Dash;         // dash runs 2..5 (device px); runs 0 and 1 ride in Stroke0.zw, the count in Stroke1.w
    uint8_t4 Stop0; uint8_t4 Stop1; uint8_t4 Stop2; uint8_t4 Stop3;   // straight stop RGBA in four bytes, only .z of Params valid
    uint8_t4 Stop4; uint8_t4 Stop5; uint8_t4 Stop6; uint8_t4 Stop7;
    float4 Offsets0;     // stop offsets 0..3
    float4 Offsets1;     // stop offsets 4..7
    float4 Clip;         // .x = the ROUNDED CLIP's slot, or -1; .y = the pen's floor in device px (0 none); .zw spare
};


// ---- sRGB <-> OKLab (Bjorn Ottosson) for PERCEPTUAL gradient interpolation. Blending in sRGB muddies midpoints (a gray
// dead-zone between complements) and bands; OKLab is perceptually uniform, so the blend keeps even brightness + hue. Used
// only when a stop's interpolation mode is OKLab (mode 1); mode 0 (sRGB) leaves the colors untouched.
float3 SrgbToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow((c + 0.055) / 1.055, float3(2.4, 2.4, 2.4));
    return float3(c.x <= 0.04045 ? lo.x : hi.x, c.y <= 0.04045 ? lo.y : hi.y, c.z <= 0.04045 ? lo.z : hi.z);
}

float3 LinearToSrgb(float3 c)
{
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(max(c, 0.0), float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) - 0.055;
    return float3(c.x <= 0.0031308 ? lo.x : hi.x, c.y <= 0.0031308 ? lo.y : hi.y, c.z <= 0.0031308 ? lo.z : hi.z);
}

float3 LinearToOklab(float3 c)
{
    float l = 0.4122214708 * c.x + 0.5363325363 * c.y + 0.0514459929 * c.z;
    float m = 0.2119034982 * c.x + 0.6806995451 * c.y + 0.1073969566 * c.z;
    float s = 0.0883024619 * c.x + 0.2817188376 * c.y + 0.6299787005 * c.z;
    float l_ = pow(max(l, 0.0), 1.0 / 3.0);
    float m_ = pow(max(m, 0.0), 1.0 / 3.0);
    float s_ = pow(max(s, 0.0), 1.0 / 3.0);
    return float3(
        0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
        1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
        0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_);
}

float3 OklabToLinear(float3 c)
{
    float l_ = c.x + 0.3963377774 * c.y + 0.2158037573 * c.z;
    float m_ = c.x - 0.1055613458 * c.y - 0.0638541728 * c.z;
    float s_ = c.x - 0.0894841775 * c.y - 1.2914855480 * c.z;
    float l = l_ * l_ * l_;
    float m = m_ * m_ * m_;
    float s = s_ * s_ * s_;
    return float3(
        4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
}

// The color at spread-mapped t from the sorted stops, already unpacked by the caller. Transitions are at least `aa` (one
// pixel) wide, so hard stops antialias; `mode` 1 blends in OKLab.
float4 GradColor(GradientRectData it, float4 cols[8], float t, float aa, int mode)
{
    int n = int(it.Params.z);
    if (n <= 0) return float4(0.0, 0.0, 0.0, 0.0);
    float offs[8];
    offs[0] = it.Offsets0.x; offs[1] = it.Offsets0.y; offs[2] = it.Offsets0.z; offs[3] = it.Offsets0.w;
    offs[4] = it.Offsets1.x; offs[5] = it.Offsets1.y; offs[6] = it.Offsets1.z; offs[7] = it.Offsets1.w;

    if (mode == 1)   // straight-sRGB stop colors -> OKLab (alpha stays linear)
    {
        for (int k = 0; k < n; k++)
        {
            cols[k] = float4(LinearToOklab(SrgbToLinear(cols[k].xyz)), cols[k].w);
        }
    }

    float4 col = cols[0];
    for (int i = 1; i < n; i++)
    {
        float lo = offs[i - 1];
        float hi = offs[i];
        float w = hi - lo;
        float bl;
        if (w > aa)
        {
            bl = saturate((t - lo) / max(w, 1e-6));                       // linear across the real segment
        }
        else
        {
            bl = saturate((t - 0.5 * (lo + hi)) / max(aa, 1e-6) + 0.5);   // 1px ramp centered on a hard stop
        }
        col = lerp(col, cols[i], bl);
    }

    if (mode == 1)   // blended in OKLab -> back to straight sRGB
    {
        col = float4(LinearToSrgb(OklabToLinear(col.xyz)), col.w);
    }
    return col;
}

struct GradPSInput
{
    float4 Position : SV_Position;
    float2 Local    : TEXCOORD0;   // fragment relative to the rect CENTER (SDF space, device px)
    float2 Half     : TEXCOORD1;   // rect half-size (device px)
    float4 Radii    : TEXCOORD2;   // corner radii (TL, TR, BR, BL) in device px
    nointerpolation uint InstId : TEXCOORD3;   // instance -> re-read GradientRectData in the PS for its gradient
    nointerpolation float Scale : TEXCOORD4;   // slot unit -> device px: the PS re-reads the stroke record, which is
                                               // baked in slot units, and has to match a pixel-space SDF
    nointerpolation float Fade  : TEXCOORD5;   // the element's alpha, fetched in the vertex stage
    nointerpolation float4 ClipBox   : TEXCOORD6;   // the rounded ancestor clip's shape, also fetched in the vertex stage
    nointerpolation float4 ClipRadii : TEXCOORD7;
};

[shader("vertex")]
GradPSInput GradientRectInstancedVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    GradientRectData* items = GradientRects;
    GradientRectData it = items[instanceId];

    GradPSInput o;
    float2 corner = float2(vertexId & 1u, (vertexId >> 1u) & 1u);
    // Node-local -> world via the transform table (slot in Geom1.w - Geom1.z is the shape flag; slot 0 = identity),
    // and the SDF inputs in DEVICE PIXELS, same as RectBatchInstancedVS. The gradient uv is Local/Half - a RATIO - so
    // the change of unit leaves the gradient itself untouched; the STROKE record is not a ratio, hence Scale.
    NodeSlot* nodes = Transforms;
    float4x4 nodeWorld = nodes[(uint)it.Geom1.w].World;
    float2 px = SlotPixelScale(nodeWorld);
    float iso = min(px.x, px.y);

    float widthPx = FloorWidthPx(it.Stroke0.x * iso, it.Clip.y);
    float outsetPx = max(widthPx * (0.5 * (1.0 + it.Stroke0.y) + 0.5), 0.0) + 1.0;
    float2 localPos = it.Bounds.xy + corner * it.Bounds.zw + (corner * 2.0 - 1.0) * (outsetPx / px);
    float4 worldPos = mul(float4(localPos, 0.0, 1.0), nodeWorld);
    o.Position = mul(worldPos, Projection);
    o.Half   = it.Bounds.zw * 0.5 * px;
    o.Local  = (corner - 0.5) * it.Bounds.zw * px + (corner * 2.0 - 1.0) * outsetPx;
    o.Radii = ScaleShapeNumbers(it.Radii, iso, step(1.5, it.Geom1.z));   // Geom1.z: 2 = regular polygon
    o.InstId = instanceId;
    o.Scale  = iso;
    // Params.w packs spread (low 3 bits), interp mode (bit 3) and the OPACITY SLOT above them, biased by 1 so 0 = none.
    float fadeSlot = floor(it.Params.w / 16.0) - 1.0;
    float fade = nodes[(uint)max(fadeSlot, 0.0)].Params.x;
    o.Fade = lerp(1.0, fade, step(0.0, fadeSlot));
    o.ClipBox   = ClipShapeBox(it.Clip.x);
    o.ClipRadii = ClipShapeRadii(it.Clip.x);
    return o;
}

// ONE gradient pixel shader for BOTH shapes (rounded rect + ellipse), branched on the per-instance shape flag (Geom1.z:
// >=0.5 = ellipse). A single gradient shader instead of two - so there is not a second near-identical BDA-reading shader.
[shader("fragment")]
float4 GradientPS(GradPSInput input) : SV_Target
{
    GradientRectData* items = GradientRects;
    GradientRectData it = items[input.InstId];

    float shape = it.Geom1.z;          // 0 rect, 1 ellipse, 2 regular polygon (its numbers ride in Radii)
    bool ellipse = shape >= 0.5 && shape < 1.5;
    bool polygon = shape >= 1.5;
    float lim = min(input.Half.x, input.Half.y);
    float4 r4 = lerp(min(input.Radii, float4(lim, lim, lim, lim)), input.Radii, step(1.5, shape));
    int joinType = int(fmod(floor(it.Stroke1.w / 4096.0), 8.0));
    float d = BrushShapeDistance(input.Local, input.Half, r4, joinType, shape);

    float2 uv = input.Local / max(input.Half * 2.0, float2(1e-4, 1e-4)) + 0.5;   // 0..1 across the bounds
    // Params.w packs spread (low 3 bits), interp mode (bit 3) and the opacity slot above them; the slot is unpacked in
    // the vertex stage (o.Fade).
    int packedW = int(it.Params.w + 0.5);
    float gt = GradientSpread(GradientParam(int(it.Params.y), it.Geom0, it.Geom1, uv), packedW & 7);
    // Wrap-aware AA width: at a conic/repeat seam gt jumps 1->0 so fwidth(gt) spikes to ~1 (the whole gradient collapses to
    // hard-stop ramps -> a colored line). Shifting by half a turn moves the discontinuity to the far side, so min() picks
    // the TRUE small derivative everywhere. Harmless for linear/radial (min keeps the real value).
    // Widened in THIS stage's own body - see GradColor.
    float4 sdfCols[8] = { float4(it.Stop0) * (1.0 / 255.0), float4(it.Stop1) * (1.0 / 255.0),
                          float4(it.Stop2) * (1.0 / 255.0), float4(it.Stop3) * (1.0 / 255.0),
                          float4(it.Stop4) * (1.0 / 255.0), float4(it.Stop5) * (1.0 / 255.0),
                          float4(it.Stop6) * (1.0 / 255.0), float4(it.Stop7) * (1.0 / 255.0) };
    float4 grad = GradColor(it, sdfCols, gt, min(fwidth(gt), fwidth(frac(gt + 0.5))), (packedW >> 3) & 1);
    // MESH gradient (type 4): four CORNER colors blended bilinearly across the shape - no axis, no stops, so the
    // gradient maths above has nothing meaningful to chew on for it (GradientBake packs zero geometry). The corners ride
    // the stop slots. Selected BRANCH-FREE: this pass has a history of device-losing on a ?:, so both are computed.
    float4 mesh = lerp(lerp(float4(it.Stop0) * (1.0 / 255.0), float4(it.Stop1) * (1.0 / 255.0), uv.x),
                       lerp(float4(it.Stop2) * (1.0 / 255.0), float4(it.Stop3) * (1.0 / 255.0), uv.x), uv.y);
    float4 fill = lerp(grad, mesh, step(3.5, it.Params.y));

    // The stroke record is baked in SLOT units; the SDF above is in device pixels, so its LENGTHS convert (align, trim
    // and the packed cap/join flags are unitless). Arc length `perim` already comes out in pixels, so dashes match.
    float sc = input.Scale;
    float widthPx = it.Stroke0.x * sc;
    float mask = 1.0;
    if (it.Stroke0.z > 0.0 || it.Stroke1.y > 0.0 || it.Stroke1.z < 1.0)
    {
        float halfW = widthPx * 0.5;
        float perim;
        float s = ellipse ? EllipseArc(input.Local, input.Half, perim)
                          : RoundRectArc(input.Local, input.Half, r4, perim);
        float dPerp = d - it.Stroke0.y * halfW;
        float capScl = ArcCapScale(ellipse ? EllipseCurvRadius(input.Local, input.Half) : RoundRectCurvRadius(input.Local, input.Half, r4), dPerp);
        mask = DashTrimMask(s, s, perim, it.Stroke0.z * sc, it.Stroke0.w * sc, it.Stroke1.x * sc, it.Stroke1.y,
                            it.Stroke1.z, dPerp * capScl, halfW * capScl, it.Stroke1.w, it.Dash * sc);
    }
    // The element's fade came across from the vertex stage - fill and stroke both take it. So did the ancestor clip's
    // shape, and for the same reason; here it is only arithmetic.
    float4 outColor = CompositeFillStroke(d, input.Local, ShapeDistanceRate(shape, joinType), float4(fill.rgb, fill.a * input.Fade),
                               float4(it.StrokeColor.x, it.StrokeColor.y, it.StrokeColor.z,
                                      float(it.StrokeColor.w) * input.Fade) * (1.0 / 255.0), widthPx, it.Stroke0.y, mask, 0.0);
    return float4(outColor.rgb, outColor.a * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii));
}

// ---- GradientFill: a shared mesh drawn N times with a linear/radial gradient fill. The pixel shader re-reads the record
// by BDA, so only local position and instance id are interpolated.
struct GradGeomData
{
    float4x4 Local;      // element local -> SLOT space (the slot's matrix is applied on top, from the transform table)
    float4 Params;       // .x type (1 linear/2 radial), .y spread, .z stop count, .w interp mode (0 sRGB/1 OKLab)
    float4 Geom0;        // LOCAL 0..1: linear (startXY, endXY) | radial (centerXY, radiusXY)
    float4 Geom1;        // radial focal (originXY, _); .w = transform-table slot
    float4 LocalBounds;  // shape local bounds: minXY, sizeXY
    uint8_t4 Stop0; uint8_t4 Stop1; uint8_t4 Stop2; uint8_t4 Stop3;   // straight stop RGBA in four bytes, as the rect record
    uint8_t4 Stop4; uint8_t4 Stop5; uint8_t4 Stop6; uint8_t4 Stop7;
    float4 Offsets0; float4 Offsets1;
    float4 Clip;         // .x = the ROUNDED CLIP's slot, or -1; .yzw spare
};

// The opacity slot is packed in Params.w with the interpolation mode (0 sRGB, 1 OKLab), since the record has no free
// component. Vertex stages unpack it inline (see GradientFillVS).
int GradGeomInterp(GradGeomData it)   { return int(fmod(it.Params.w, 2.0)); }

struct GradFillPSInput
{
    float4 Position : SV_Position;
    float2 Local : TEXCOORD0;                   // varying: fragment's local mesh xy (for uv)
    nointerpolation uint InstId : TEXCOORD1;    // instance -> re-read GradGeomData in the PS (light signature)
    // The opacity slot's alpha, fetched once per vertex and carried down as a varying.
    nointerpolation float Fade : TEXCOORD2;
    nointerpolation float4 ClipBox   : TEXCOORD3;   // the ancestor's rounded clip, fetched the same way
    nointerpolation float4 ClipRadii : TEXCOORD4;
};

[shader("vertex")]
GradFillPSInput GradientFillVS(UI_VERTEX v, uint instanceId : SV_InstanceID)
{
    GradGeomData* items = GradientGeometries;
    // FIELD BY FIELD, not a copy of the whole record: this stage needs four float4s out of it and never looks at a
    // stop, and pulling the struct in wholesale drags the eight FOUR-BYTE colors through the vertex stage with it.
    float4x4 local = items[instanceId].Local;
    float4 geom1 = items[instanceId].Geom1;
    float4 gparams = items[instanceId].Params;
    float4 clip = items[instanceId].Clip;
    // local -> slot space -> world, as InstancedFillVS: the slot matrix lives in the transform table, so a node move
    // rewrites 64 bytes there and every instance under it follows without this buffer being touched.
    NodeSlot* nodes = Transforms;
    float4 world = mul(mul(float4(v.position, 1.0), local), nodes[(uint)geom1.w].World);

    GradFillPSInput o;
    o.Position = mul(world, Projection);
    o.Local = v.position.xy;
    o.InstId = instanceId;
    int gradFadeSlot = int(gparams.w * 0.5) - 1;
    o.Fade = lerp(1.0, nodes[max(gradFadeSlot, 0)].Params.x, step(0.0, float(gradFadeSlot)));
    o.ClipBox   = ClipShapeBox(clip.x);
    o.ClipRadii = ClipShapeRadii(clip.x);
    return o;
}

// The gradient color at a LOCAL mesh position, for instanced geometry. The fill and its analytic-AA fringe both call
// this, so the ring is colored by exactly the same gradient as the body it feathers.
float4 GradGeomColor(GradGeomData it, float2 local)
{
    // Reconstruct a GradientRectData for the shared GradientParam/GradColor (Bounds/stroke fields unused by the fill eval).
    GradientRectData gd;
    gd.Bounds = float4(0.0, 0.0, 0.0, 0.0);
    gd.Params = float4(0.0, it.Params.x, it.Params.z, it.Params.y);   // (_, type, stopCount, spread)
    gd.Geom0 = it.Geom0; gd.Geom1 = it.Geom1;
    gd.StrokeColor = uint8_t4(0, 0, 0, 0);
    gd.Stroke0 = float4(0.0, 0.0, 0.0, 0.0);
    gd.Stroke1 = float4(0.0, 0.0, 0.0, 0.0);
    gd.Stop0 = it.Stop0; gd.Stop1 = it.Stop1; gd.Stop2 = it.Stop2; gd.Stop3 = it.Stop3;
    gd.Stop4 = it.Stop4; gd.Stop5 = it.Stop5; gd.Stop6 = it.Stop6; gd.Stop7 = it.Stop7;
    gd.Offsets0 = it.Offsets0; gd.Offsets1 = it.Offsets1;

    float2 uv = (local - it.LocalBounds.xy) / max(it.LocalBounds.zw, float2(1e-4, 1e-4));
    // Between two circles nothing is painted where none passes, and that edge is covered as a shape's is. Worked out for
    // every instance and chosen after, so its derivatives are taken in flow the whole quad shares.
    float reach;
    float circlesRadius;
    float circlesT = TwoCirclesRoot(gd.Geom0, gd.Geom1, uv, reach, circlesRadius);
    float circlesCovered = TwoCirclesCoverage(reach, circlesRadius);
    bool twoCircles = int(gd.Params.y) == 5;
    float raw = twoCircles ? circlesT : GradientParam(int(gd.Params.y), gd.Geom0, gd.Geom1, uv);
    float gt = GradientSpread(raw, int(gd.Params.w));
    // Widened in THIS stage's own body - see GradColor.
    float4 meshCols[8] = { float4(it.Stop0) * (1.0 / 255.0), float4(it.Stop1) * (1.0 / 255.0),
                           float4(it.Stop2) * (1.0 / 255.0), float4(it.Stop3) * (1.0 / 255.0),
                           float4(it.Stop4) * (1.0 / 255.0), float4(it.Stop5) * (1.0 / 255.0),
                           float4(it.Stop6) * (1.0 / 255.0), float4(it.Stop7) * (1.0 / 255.0) };
    float4 grad = GradColor(gd, meshCols, gt, min(fwidth(gt), fwidth(frac(gt + 0.5))), GradGeomInterp(it));   // wrap-aware AA (conic/repeat seam)
    // MESH (type 4) here too: a mesh brush has NO axis geometry, so without this branch the maths above runs on zeros and
    // walks the stop table with a meaningless parameter. Same branch-free select as the rect pass.
    float4 mesh = lerp(lerp(meshCols[0], meshCols[1], uv.x), lerp(meshCols[2], meshCols[3], uv.x), uv.y);
    float4 color = lerp(grad, mesh, step(3.5, gd.Params.y) * step(gd.Params.y, 4.5));
    color.a *= twoCircles ? circlesCovered : 1.0;
    return color;
}

[shader("fragment")]
float4 GradientFillPS(GradFillPSInput input) : SV_Target
{
    GradGeomData* items = GradientGeometries;
    float4 c = GradGeomColor(items[input.InstId], input.Local);
    return float4(c.rgb, c.a * input.Fade * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii));
}

// The same gradient instances drawn from their curves (see CurveMath.fxh): a quad each over the fill's box, the mesh's
// local space carried down so the gradient is mapped exactly as on the mesh.
[shader("vertex")]
GradFillPSInput GradientCurveVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    GradGeomData* items = GradientGeometries;
    // Field by field, as GradientFillVS.
    float4x4 local = items[instanceId].Local;
    float4 geom1 = items[instanceId].Geom1;
    float4 gparams = items[instanceId].Params;
    float4 clip = items[instanceId].Clip;
    NodeSlot* nodes = Transforms;
    float4x4 toSlot = mul(local, nodes[(uint)geom1.w].World);
    float2 at = CurveQuadCorner(vertexId, toSlot);

    GradFillPSInput o;
    o.Position = mul(mul(float4(at, 0.0, 1.0), toSlot), Projection);
    o.Local = at;
    o.InstId = instanceId;
    int gradFadeSlot = int(gparams.w * 0.5) - 1;
    o.Fade = lerp(1.0, nodes[max(gradFadeSlot, 0)].Params.x, step(0.0, float(gradFadeSlot)));
    o.ClipBox   = ClipShapeBox(clip.x);
    o.ClipRadii = ClipShapeRadii(clip.x);
    return o;
}

[shader("fragment")]
float4 GradientCurvePS(GradFillPSInput input) : SV_Target
{
    GradGeomData* items = GradientGeometries;
    float4 c = GradGeomColor(items[input.InstId], input.Local);
    float coverage = CurveCoverage(input.Local);
    if (coverage <= 0.0)
    {
        discard;
    }

    return float4(c.rgb, c.a * coverage * input.Fade * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii));
}

// The analytic-AA fringe of those gradient instances: same shared ring, same instance buffer, one draw. Unlike the
// solid and pattern fringes it needs its own PS - the ring's color varies per fragment, so it cannot be resolved in
// the vertex stage and handed over as one color.
struct GradFringePSInput
{
    float4 Position : SV_Position;
    float2 Local    : TEXCOORD0;   // LOCAL mesh position -> the gradient uv
    float  Coverage : TEXCOORD1;
    nointerpolation uint InstId : TEXCOORD2;
    nointerpolation float Fade : TEXCOORD3;   // fetched in the VERTEX stage - see GradFillPSInput
    nointerpolation float4 ClipBox   : TEXCOORD4;   // ...and so is the ancestor's rounded clip
    nointerpolation float4 ClipRadii : TEXCOORD5;
};

[shader("vertex")]
GradFringePSInput InstancedGradientFringeVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    FringeVertex v = RingCorner(vertexId);
    GradGeomData* items = GradientGeometries;
    // Field by field, as GradientFillVS - the stops have no business in a vertex stage.
    float4x4 local = items[instanceId].Local;
    float4 geom1 = items[instanceId].Geom1;
    float4 gparams = items[instanceId].Params;
    float4 clip = items[instanceId].Clip;
    NodeSlot* nodes = Transforms;
    float4x4 m = mul(mul(local, nodes[(uint)geom1.w].World), Projection);

    GradFringePSInput o;
    float coverage;
    o.Position = ExpandFringe(v, m, coverage);
    o.Local = v.Position;
    o.Coverage = coverage;
    o.InstId = instanceId;
    int gradFadeSlot = int(gparams.w * 0.5) - 1;
    o.Fade = lerp(1.0, nodes[max(gradFadeSlot, 0)].Params.x, step(0.0, float(gradFadeSlot)));
    o.ClipBox   = ClipShapeBox(clip.x);
    o.ClipRadii = ClipShapeRadii(clip.x);
    return o;
}

[shader("fragment")]
float4 InstancedGradientFringePS(GradFringePSInput input) : SV_Target
{
    GradGeomData* items = GradientGeometries;
    float4 c = GradGeomColor(items[input.InstId], input.Local);
    c.a *= saturate(input.Coverage) * input.Fade   // 1 at the contour -> 0 at the outer edge
         * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii);
    return c;
}

// ---- Pattern batch: the SAME SDF rounded-rect (self-AA shape + the shared stroke), but the FILL is a PROCEDURAL two-color
// PATTERN (checkerboard/stripes/dots/grid) evaluated per fragment - resolution-independent, no texture. Per-instance
// PatternRectData from a BDA storage buffer by SV_InstanceID; the PS re-reads the record (light interpolator signature, like
// GradientPS) and mixes Color1/Color2 by the pattern. Solid/gradient rects stay in their own passes.
struct PatternRectData
{
    float4 Bounds;       // NODE-local x, y, w, h (world for slot-0 legacy bakes - identity matrix)
    float4 Params;       // .x corner radius, .y pattern type (0 checker/1 stripes/2 dots/3 grid/4 FBM noise), .z cell (px), .w slot
    float4 Radii;        // corner radii: x = TL, y = TR, z = BR, w = BL
    float4 Color1;        // straight RGBA, opacity folded
    float4 Color2;        // straight RGBA, opacity folded
    uint8_t4 StrokeColor; // straight stroke RGBA in four bytes (alpha 0 -> no stroke)
    float4 Stroke0;      // width_px, align, dashOn, dashGap
    float4 Stroke1;      // dashOffset, trimStart, trimEnd, flags
    float4 Dash;         // dash runs 2..5 (device px); runs 0 and 1 ride in Stroke0.zw, the count in Stroke1.w
    float4 Noise;        // FBM noise (type 4 only): x octaves, y seed, z lacunarity, w gain
    float4 Color3;       // optional MID color for a 3-color noise gradient-map (Color1->Color3->Color2); .w==0 = off
    float4 Anim;         // .x = offset subtracted from the clock while animating, .y = the phase held while paused,
                         // .z = an opacity slot the CPU stamps that NO pass reads (this carrier folds the chain into
                         // Color1/Color2), .w = the ROUNDED CLIP's slot, -1 = none
    float4 Floor;        // .x the pen's floor in device pixels (0 none), .yzw spare
};

struct PatternPSInput
{
    float4 Position : SV_Position;
    float2 Local    : TEXCOORD0;   // fragment relative to the rect CENTER (SDF space, device px)
    float2 Half     : TEXCOORD1;   // rect half-size
    float4 Radii    : TEXCOORD2;   // corner radii (TL, TR, BR, BL) in device px
    nointerpolation uint InstId : TEXCOORD3;   // instance -> re-read PatternRectData in the PS
    nointerpolation float Scale : TEXCOORD4;   // slot unit -> device px. The PS re-reads the record, whose stroke AND
                                               // cell size (PatternBrush.CellSize / NoiseBrush.Scale, one field) are
                                               // absolute lengths in slot units - they can't ride a ratio like uv.
    nointerpolation float4 ClipBox   : TEXCOORD5;   // the ancestor's rounded clip, fetched in the VERTEX stage
    nointerpolation float4 ClipRadii : TEXCOORD6;
    // The element's alpha from the OPACITY SLOT, fetched in the same stage. The CPU has stamped that slot into the
    // record all along and nothing read it, while the bake had already taken the opacity CHAIN out of the color
    // (RenderCache calls FadeBySlot for this family) - so a faded ancestor left a pattern at full strength. Measured on
    // the Opacity stand: every other family sat at 0.58 of its reference and the pattern at 1.28.
    nointerpolation float Fade : TEXCOORD7;
};

[shader("vertex")]
PatternPSInput PatternRectInstancedVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    PatternRectData* items = PatternRects;
    PatternRectData it = items[instanceId];

    PatternPSInput o;
    float2 corner = float2(vertexId & 1u, (vertexId >> 1u) & 1u);
    // SDF inputs in DEVICE PIXELS, same scheme as RectBatchInstancedVS (see SlotPixelScale).
    NodeSlot* nodes = Transforms;
    float4x4 nodeWorld = nodes[(uint)it.Params.w].World;
    float2 px = SlotPixelScale(nodeWorld);
    float iso = min(px.x, px.y);

    float widthPx = FloorWidthPx(it.Stroke0.x * iso, it.Floor.x);
    float outsetPx = max(widthPx * (0.5 * (1.0 + it.Stroke0.y) + 0.5), 0.0) + 1.0;
    float2 localPos = it.Bounds.xy + corner * it.Bounds.zw + (corner * 2.0 - 1.0) * (outsetPx / px);
    float4 worldPos = mul(float4(localPos, 0.0, 1.0), nodeWorld);
    o.Position = mul(worldPos, Projection);
    o.Half   = it.Bounds.zw * 0.5 * px;
    o.Local  = (corner - 0.5) * it.Bounds.zw * px + (corner * 2.0 - 1.0) * outsetPx;
    o.Radii = ScaleShapeNumbers(it.Radii, iso, step(it.Params.x, -1.5));   // Params.x: -2 = regular polygon
    o.InstId = instanceId;
    o.Scale  = iso;
    ClipShapeBoxAndRadii(it.Anim.w, o.ClipBox, o.ClipRadii);   // the clip slot rides in Anim.w - see PatternRectData
    // ...and the opacity slot in Anim.z.
    int patFadeSlot = int(it.Anim.z);
    o.Fade = patFadeSlot < 0 ? 1.0 : nodes[(uint)patFadeSlot].Params.x;
    return o;
}



float4 PatternSdfShade(PatternPSInput input, int kind)
{
    PatternRectData* items = PatternRects;
    PatternRectData it = items[input.InstId];

    // The baked corner radius is the shape flag: >= 0 a rect, -1 an ellipse, -2 a regular polygon (whose corners, start
    // angle and ring ride in the Radii it has no use for otherwise).
    float isPolygon = step(it.Params.x, -1.5);
    float shape = isPolygon * 2.0 + step(it.Params.x, -0.0001) * (1.0 - isPolygon);
    bool ellipse = shape > 0.5 && shape < 1.5;   // the arc-length branch below still needs to know which curve it is
    float lim = min(input.Half.x, input.Half.y);
    float4 r4 = lerp(min(input.Radii, float4(lim, lim, lim, lim)), input.Radii, step(1.5, shape));
    int joinType = int(fmod(floor(it.Stroke1.w / 4096.0), 8.0));
    float d = BrushShapeDistance(input.Local, input.Half, r4, joinType, shape);

    // The record is baked in SLOT units, the SDF above is in device pixels - so the CELL (PatternBrush.CellSize /
    // NoiseBrush.Scale share this field) converts too, or the pattern's cell / the noise's grain would change size with
    // the slot's scale. The noise's own knobs (octaves, seed, lacunarity, gain) are unitless and stay put.
    float sc = input.Scale;
    PatternFill itPx;
    itPx.Params = float4(it.Params.x, it.Params.y, it.Params.z * sc, it.Params.w);
    itPx.Color1 = it.Color1;
    itPx.Color2 = it.Color2;
    itPx.Color3 = it.Color3;
    itPx.Noise = it.Noise;
    itPx.Anim = it.Anim;

    float2 p = input.Local + input.Half;   // fragment from the shape's TOP-LEFT (stable pattern origin at the corner)
    float4 fill = PatternFillColor(itPx, kind, p, input.Local, input.Half.y);
    // Not faded through the slot: the opacity chain stays in the color here.

    float widthPx = it.Stroke0.x * sc;
    float mask = 1.0;
    if (it.Stroke0.z > 0.0 || it.Stroke1.y > 0.0 || it.Stroke1.z < 1.0)
    {
        float halfW = widthPx * 0.5;
        float perim;
        float s = ellipse ? EllipseArc(input.Local, input.Half, perim)
                          : RoundRectArc(input.Local, input.Half, r4, perim);
        float dPerp = d - it.Stroke0.y * halfW;
        float capScl = ArcCapScale(ellipse ? EllipseCurvRadius(input.Local, input.Half) : RoundRectCurvRadius(input.Local, input.Half, r4), dPerp);
        mask = DashTrimMask(s, s, perim, it.Stroke0.z * sc, it.Stroke0.w * sc, it.Stroke1.x * sc, it.Stroke1.y,
                            it.Stroke1.z, dPerp * capScl, halfW * capScl, it.Stroke1.w, it.Dash * sc);
    }
    // The shape's own edge is the SDF above; this is the ANCESTOR's rounding, as coverage - and its FADE beside it.
    float4 patOut = CompositeFillStroke(d, input.Local, ShapeDistanceRate(shape, joinType), fill, float4(it.StrokeColor) * (1.0 / 255.0), widthPx, it.Stroke0.y, mask, 0.0);
    return float4(patOut.rgb, patOut.a * input.Fade * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii));
}

// ---- PatternFill: general instanced geometry (a shared tessellated mesh drawn N times) whose FILL is a PROCEDURAL
// pattern/noise brush - the pattern sibling of GradientFill, so pattern/noise work on ANY geometry (Path/Polygon/glyphs),
// not just the SDF rect. Per-instance PatternGeomData from a BDA buffer by SV_InstanceID; the PS reconstructs a PatternRectData
// and calls the SAME PatternFillColor the SDF rect pattern PS uses (fed the fragment's LOCAL mesh position).
struct PatFillPSInput
{
    float4 Position : SV_Position;
    float2 Local : TEXCOORD0;                   // varying: fragment's local mesh xy
    nointerpolation uint InstId : TEXCOORD1;    // instance -> re-read PatternGeomData in the PS (light signature)
    nointerpolation float Fade : TEXCOORD2;     // fetched in the VERTEX stage - see GradFillPSInput
    nointerpolation float4 ClipBox   : TEXCOORD3;   // the ancestor's rounded clip, fetched the same way
    nointerpolation float4 ClipRadii : TEXCOORD4;
};

[shader("vertex")]
PatFillPSInput PatternFillVS(UI_VERTEX v, uint instanceId : SV_InstanceID)
{
    PatternGeomData* items = PatternGeometries;
    PatternGeomData it = items[instanceId];
    // local -> slot space -> world, as InstancedFillVS / GradientFillVS.
    NodeSlot* nodes = Transforms;
    float4 world = mul(mul(float4(v.position, 1.0), it.Local), nodes[(uint)it.Params.w].World);

    PatFillPSInput o;
    o.Position = mul(world, Projection);
    o.Local = v.position.xy;
    o.InstId = instanceId;
    int patFadeSlot = int(it.Params.x);
    o.Fade = lerp(1.0, nodes[max(patFadeSlot, 0)].Params.x, step(0.0, float(patFadeSlot)));
    o.ClipBox   = ClipShapeBox(it.Anim.w);     // the clip slot rides in Anim.w, as in the SDF pattern record
    o.ClipRadii = ClipShapeRadii(it.Anim.w);
    return o;
}

// The pattern FRINGE is not here: a one-pixel ring does not evaluate the pattern, it takes the brush's low color, so
// it is the SAME flat pass the solid fringe uses and lives with it in BatchEffect (pass PatternFringe). Only the fringes
// that genuinely compute their color - the gradient's and the texture's - are brushes and stay in this file.

float4 PatternMeshShade(PatFillPSInput input, int kind)
{
    PatternGeomData* items = PatternGeometries;
    PatternGeomData it = items[input.InstId];

    PatternFill pd;
    pd.Params = it.Params;
    pd.Color1 = it.Color1;
    pd.Color2 = it.Color2;
    pd.Color3 = it.Color3;
    pd.Noise = it.Noise;
    pd.Anim = it.Anim;

    float2 pTopLeft = input.Local - it.LocalBounds.xy;                                   // fragment from the shape top-left
    float2 centerRel = input.Local - (it.LocalBounds.xy + it.LocalBounds.zw * 0.5);      // fragment from the shape center
    float4 c = PatternFillColor(pd, kind, pTopLeft, centerRel, max(it.LocalBounds.w * 0.5, 1.0));
    return float4(c.rgb, c.a * input.Fade * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii));
}

// The same pattern instances drawn from their curves (see CurveMath.fxh), the mesh's local space carried down.
[shader("vertex")]
PatFillPSInput PatternCurveVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    PatternGeomData* items = PatternGeometries;
    PatternGeomData it = items[instanceId];
    NodeSlot* nodes = Transforms;
    float4x4 toSlot = mul(it.Local, nodes[(uint)it.Params.w].World);
    float2 at = CurveQuadCorner(vertexId, toSlot);

    PatFillPSInput o;
    o.Position = mul(mul(float4(at, 0.0, 1.0), toSlot), Projection);
    o.Local = at;
    o.InstId = instanceId;
    int patFadeSlot = int(it.Params.x);
    o.Fade = lerp(1.0, nodes[max(patFadeSlot, 0)].Params.x, step(0.0, float(patFadeSlot)));
    o.ClipBox   = ClipShapeBox(it.Anim.w);
    o.ClipRadii = ClipShapeRadii(it.Anim.w);
    return o;
}

float4 PatternCurveShade(PatFillPSInput input, int kind)
{
    float4 c = PatternMeshShade(input, kind);
    float coverage = CurveCoverage(input.Local);
    if (coverage <= 0.0)
    {
        discard;
    }

    return float4(c.rgb, c.a * coverage);
}

// ---- ONE ENTRY POINT PER KIND ---------------------------------------------------------------------------------------
// The same body with a compile-time literal, so each pass keeps only its branch. Literals are the Params.y values:
// 0 checker, 1 stripes, 2 dots, 3 grid, 5 hexagon, 6 hatch; noise 4 simplex, 7 perlin, 8 value, 9 worley, 10 ridged,
// 11 turbulence, 12 voronoi borders, 13 combustible voronoi.

[shader("fragment")] float4 PatternCheckerboardSdfPS(PatternPSInput i) : SV_Target { return PatternSdfShade(i, 0); }
[shader("fragment")] float4 PatternStripesSdfPS(PatternPSInput i)      : SV_Target { return PatternSdfShade(i, 1); }
[shader("fragment")] float4 PatternDotsSdfPS(PatternPSInput i)         : SV_Target { return PatternSdfShade(i, 2); }
[shader("fragment")] float4 PatternGridSdfPS(PatternPSInput i)         : SV_Target { return PatternSdfShade(i, 3); }
[shader("fragment")] float4 PatternHexagonSdfPS(PatternPSInput i)      : SV_Target { return PatternSdfShade(i, 4); }
[shader("fragment")] float4 PatternHatchSdfPS(PatternPSInput i)        : SV_Target { return PatternSdfShade(i, 5); }
[shader("fragment")] float4 PatternWeaveSdfPS(PatternPSInput i)        : SV_Target { return PatternSdfShade(i, 6); }

[shader("fragment")] float4 PatternCheckerboardMeshPS(PatFillPSInput i) : SV_Target { return PatternMeshShade(i, 0); }
[shader("fragment")] float4 PatternStripesMeshPS(PatFillPSInput i)      : SV_Target { return PatternMeshShade(i, 1); }
[shader("fragment")] float4 PatternDotsMeshPS(PatFillPSInput i)         : SV_Target { return PatternMeshShade(i, 2); }
[shader("fragment")] float4 PatternGridMeshPS(PatFillPSInput i)         : SV_Target { return PatternMeshShade(i, 3); }
[shader("fragment")] float4 PatternHexagonMeshPS(PatFillPSInput i)      : SV_Target { return PatternMeshShade(i, 4); }
[shader("fragment")] float4 PatternHatchMeshPS(PatFillPSInput i)        : SV_Target { return PatternMeshShade(i, 5); }
[shader("fragment")] float4 PatternWeaveMeshPS(PatFillPSInput i)        : SV_Target { return PatternMeshShade(i, 6); }

[shader("fragment")] float4 NoiseSimplexSdfPS(PatternPSInput i)     : SV_Target { return PatternSdfShade(i, 100); }
[shader("fragment")] float4 NoisePerlinSdfPS(PatternPSInput i)      : SV_Target { return PatternSdfShade(i, 101); }
[shader("fragment")] float4 NoiseValueSdfPS(PatternPSInput i)       : SV_Target { return PatternSdfShade(i, 102); }
[shader("fragment")] float4 NoiseWorleySdfPS(PatternPSInput i)      : SV_Target { return PatternSdfShade(i, 103); }
[shader("fragment")] float4 NoiseRidgedSdfPS(PatternPSInput i)      : SV_Target { return PatternSdfShade(i, 104); }
[shader("fragment")] float4 NoiseTurbulenceSdfPS(PatternPSInput i)  : SV_Target { return PatternSdfShade(i, 105); }
[shader("fragment")] float4 NoiseVoronoiSdfPS(PatternPSInput i)     : SV_Target { return PatternSdfShade(i, 106); }
[shader("fragment")] float4 NoiseCombustibleSdfPS(PatternPSInput i) : SV_Target { return PatternSdfShade(i, 107); }
[shader("fragment")] float4 NoiseCircuitSdfPS(PatternPSInput i)     : SV_Target { return PatternSdfShade(i, 108); }

[shader("fragment")] float4 NoiseSimplexMeshPS(PatFillPSInput i)     : SV_Target { return PatternMeshShade(i, 100); }
[shader("fragment")] float4 NoisePerlinMeshPS(PatFillPSInput i)      : SV_Target { return PatternMeshShade(i, 101); }
[shader("fragment")] float4 NoiseValueMeshPS(PatFillPSInput i)       : SV_Target { return PatternMeshShade(i, 102); }
[shader("fragment")] float4 NoiseWorleyMeshPS(PatFillPSInput i)      : SV_Target { return PatternMeshShade(i, 103); }
[shader("fragment")] float4 NoiseRidgedMeshPS(PatFillPSInput i)      : SV_Target { return PatternMeshShade(i, 104); }
[shader("fragment")] float4 NoiseTurbulenceMeshPS(PatFillPSInput i)  : SV_Target { return PatternMeshShade(i, 105); }
[shader("fragment")] float4 NoiseVoronoiMeshPS(PatFillPSInput i)     : SV_Target { return PatternMeshShade(i, 106); }
[shader("fragment")] float4 NoiseCombustibleMeshPS(PatFillPSInput i) : SV_Target { return PatternMeshShade(i, 107); }
[shader("fragment")] float4 NoiseCircuitMeshPS(PatFillPSInput i)     : SV_Target { return PatternMeshShade(i, 108); }

[shader("fragment")] float4 PatternCheckerboardCurvePS(PatFillPSInput i) : SV_Target { return PatternCurveShade(i, 0); }
[shader("fragment")] float4 PatternStripesCurvePS(PatFillPSInput i)      : SV_Target { return PatternCurveShade(i, 1); }
[shader("fragment")] float4 PatternDotsCurvePS(PatFillPSInput i)         : SV_Target { return PatternCurveShade(i, 2); }
[shader("fragment")] float4 PatternGridCurvePS(PatFillPSInput i)         : SV_Target { return PatternCurveShade(i, 3); }
[shader("fragment")] float4 PatternHexagonCurvePS(PatFillPSInput i)      : SV_Target { return PatternCurveShade(i, 4); }
[shader("fragment")] float4 PatternHatchCurvePS(PatFillPSInput i)        : SV_Target { return PatternCurveShade(i, 5); }
[shader("fragment")] float4 PatternWeaveCurvePS(PatFillPSInput i)        : SV_Target { return PatternCurveShade(i, 6); }

[shader("fragment")] float4 NoiseSimplexCurvePS(PatFillPSInput i)     : SV_Target { return PatternCurveShade(i, 100); }
[shader("fragment")] float4 NoisePerlinCurvePS(PatFillPSInput i)      : SV_Target { return PatternCurveShade(i, 101); }
[shader("fragment")] float4 NoiseValueCurvePS(PatFillPSInput i)       : SV_Target { return PatternCurveShade(i, 102); }
[shader("fragment")] float4 NoiseWorleyCurvePS(PatFillPSInput i)      : SV_Target { return PatternCurveShade(i, 103); }
[shader("fragment")] float4 NoiseRidgedCurvePS(PatFillPSInput i)      : SV_Target { return PatternCurveShade(i, 104); }
[shader("fragment")] float4 NoiseTurbulenceCurvePS(PatFillPSInput i)  : SV_Target { return PatternCurveShade(i, 105); }
[shader("fragment")] float4 NoiseVoronoiCurvePS(PatFillPSInput i)     : SV_Target { return PatternCurveShade(i, 106); }
[shader("fragment")] float4 NoiseCombustibleCurvePS(PatFillPSInput i) : SV_Target { return PatternCurveShade(i, 107); }
[shader("fragment")] float4 NoiseCircuitCurvePS(PatFillPSInput i)     : SV_Target { return PatternCurveShade(i, 108); }


// ---- TEXTURED rounded rect: a fill whose color is SAMPLED rather than computed - SDF, one uv wrap, one sample, one
// multiply. No stroke: a textured fill with a pen falls back to the per-unit path.

// The source when it is one layer of an array - an atlas of map tiles: pass SdfArray samples this instead of
// SourceTexture, at the record's Clip.z.
Texture2DArray SourceTextureArray;

struct TexRectData
{
    float4 Bounds;     // NODE-local x, y, w, h - the SHAPE, which never shrinks with the picture
    float4 Params;     // .x corner radius, .y transform slot, .z repeat flag, .w mirror flags (1 = X, 2 = Y, 3 = both)
    float4 Radii;        // corner radii: x = TL, y = TR, z = BR, w = BL
    float4 Tile;       // tile grid over the bounds: tiles per axis (.xy), grid origin in tiles (.zw)
    float4 Rotation;   // 2x2 mapping a fragment back into the unturned grid, row-major (identity = 1,0,0,1)
    float4 Drawn;      // the content's rect inside ONE tile: offsetXY, scaleXY, both in 0..1 of the tile
    float4 UvRect;     // sub-rectangle of the source: x, y, w, h (normalized)
    uint8_t4 Tint;     // multiplied into the sample, straight RGBA in four bytes
    float4 Clip;       // .x = the ROUNDED CLIP's slot, or -1; .y the fade slot; .z the layer of an array source; .w spare
    float4 Adjust;     // before the tint, zero for none: .x brightness, .y contrast - 1, .z saturation - 1
};

struct TexPSInput
{
    float4 Position : SV_Position;
    float2 Local    : TEXCOORD0;   // fragment relative to the rect CENTER (SDF space, device px)
    float2 Half     : TEXCOORD1;   // rect half-size
    float4 Radii    : TEXCOORD2;   // corner radii (TL, TR, BR, BL) in device px
    nointerpolation uint InstId : TEXCOORD3;
    nointerpolation float4 ClipBox   : TEXCOORD4;   // the ancestor's rounded clip, fetched in the VERTEX stage
    nointerpolation float4 ClipRadii : TEXCOORD5;
    nointerpolation float Fade : TEXCOORD6;         // ...and the element's alpha from its opacity slot
};

[shader("vertex")]
TexPSInput TexRectInstancedVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    TexRectData* items = TextureRects;
    TexRectData it = items[instanceId];

    TexPSInput o;
    float2 corner = float2(vertexId & 1u, (vertexId >> 1u) & 1u);
    NodeSlot* nodes = Transforms;
    float4x4 nodeWorld = nodes[(uint)it.Params.y].World;
    float2 px = SlotPixelScale(nodeWorld);
    float iso = min(px.x, px.y);

    // One pixel of outset so the analytic edge has room to feather (no stroke, so no width to account for).
    float2 localPos = it.Bounds.xy + corner * it.Bounds.zw + (corner * 2.0 - 1.0) * (1.0 / px);
    float4 worldPos = mul(float4(localPos, 0.0, 1.0), nodeWorld);
    o.Position = mul(worldPos, Projection);
    o.Half   = it.Bounds.zw * 0.5 * px;
    o.Local  = (corner - 0.5) * it.Bounds.zw * px + (corner * 2.0 - 1.0);
    o.Radii = ScaleShapeNumbers(it.Radii, iso, step(it.Params.x, -1.5));   // Params.x: -2 = regular polygon
    o.InstId = instanceId;
    o.ClipBox   = ClipShapeBox(it.Clip.x);
    o.ClipRadii = ClipShapeRadii(it.Clip.x);
    int texSdfFadeSlot = int(it.Clip.y);   // int test + branch - see the pattern VS above for why, not the lerp/step form
    o.Fade = texSdfFadeSlot < 0 ? 1.0 : nodes[(uint)texSdfFadeSlot].Params.x;
    return o;
}

// The source is a plain picture (0) or one layer of an array, Clip.z (1): a compile-time literal from the entry point, so
// each pass binds and samples only its own kind of texture.
float4 TexRectShade(TexPSInput input, int source)
{
    TexRectData* items = TextureRects;
    TexRectData it = items[input.InstId];

    // A NEGATIVE baked corner radius is the ELLIPSE shape flag (a rect passes radius >= 0) - same signal the pattern
    // pass uses. Both distances are computed and picked by a step.
    float isPolygon = step(it.Params.x, -1.5);
    float isEllipse = step(it.Params.x, -0.0001) * (1.0 - isPolygon);
    float lim = min(input.Half.x, input.Half.y);
    float4 r4 = lerp(min(input.Radii, float4(lim, lim, lim, lim)), input.Radii, isPolygon);
    // round join: the plain Euclidean offset, no stroke here
    float d = BrushShapeDistance(input.Local, input.Half, r4, 2, isEllipse + isPolygon * 2.0);

    // 0..1 across the shape -> TILE space -> the content's rect inside one tile -> the source's sub-rectangle. The
    // REPEAT is done here with frac() rather than by a wrapping sampler: the sampler would wrap the WHOLE texture, and
    // a slice of it needs its own strip wrapped.
    float2 t = input.Local / max(input.Half * 2.0, float2(1e-4, 1e-4)) + 0.5;
    float inside;
    float2 uv = TileSourceUv(t, it.Rotation, it.Tile, it.Drawn, it.UvRect, float2(it.Params.z, it.Params.z), it.Params.w,
        inside);

    // SampleLevel, not Sample: frac() above makes uv DISCONTINUOUS at every tile seam, so the hardware's derivative -
    // which is what Sample picks a mip level by - spikes there and that one column of pixels is drawn from the smallest
    // mip. That is the thin line down each seam. The level is explicit here because the footprint is ours to state.
    float4 fill;
    if (source == 1)
    {
        fill = SourceTextureArray.SampleLevel(SourceSampler, float3(uv, it.Clip.z), 0.0);
    }
    else
    {
        fill = SourceTexture.SampleLevel(SourceSampler, uv, 0.0);
    }
    fill.rgb = AdjustColor(fill.rgb, it.Adjust);
    fill *= float4(it.Tint) * (1.0 / 255.0);

    // A SQUARE piece is drawn CRISP, not feathered. Nine-slice cuts a picture into nine quads that share edges, and a
    // coverage ramp puts 0.5 on both sides of every shared edge - alpha-composited that is ~0.75, a dark hairline down
    // every joint. Feathering is for a shape with a curve to it, so only a corner radius earns the ramp.
    // Picked between the two with a step on the radius.
    float aa = max(EdgeRamp(d, input.Local, ShapeDistanceRate(isEllipse + isPolygon * 2.0, 2)), 1e-4);
    float ramp = saturate(0.5 - d / aa);
    float crisp = step(d, 0.0);
    fill.a *= lerp(crisp, ramp, max(step(0.001, max(max(r4.x, r4.y), max(r4.z, r4.w))), isEllipse));   // an ellipse is ALL curve, so it always earns the ramp
    // Both of the ancestor's numbers, fetched in the VERTEX stage and applied here for one multiply each: its FADE and
    // its rounded CLIP. The fade used to be missing - the record dropped the slot the CPU handed it while the bake had
    // already taken the chain out of the tint, so a faded ancestor barely dimmed a picture.
    fill.a *= inside * input.Fade * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii);
    return fill;
}

[shader("fragment")] float4 TexRectPS(TexPSInput input) : SV_Target { return TexRectShade(input, 0); }
[shader("fragment")] float4 TexRectArrayPS(TexPSInput input) : SV_Target { return TexRectShade(input, 1); }


// ---- TexFill: a shared mesh drawn N times with a texture fill, mapped over the shape's local bounds with the SDF batch's
// tiling. The texture is bound per draw, not stored in the record.
struct TextureGeomData
{
    float4x4 Local;      // element local -> SLOT space (the slot's matrix is applied on top, from the transform table)
    float4 Params;       // .x repeat flag, .y mirror flags (1 = X, 2 = Y, 3 = both), .w transform slot
    float4 LocalBounds;  // shape local bounds: minXY, sizeXY - the box the picture is mapped across
    float4 Tile;         // tile grid over that box: tiles per axis (.xy), grid origin in tiles (.zw)
    float4 Rotation;     // 2x2 mapping a fragment back into the unturned grid, row-major (identity = 1,0,0,1)
    float4 Drawn;        // the content's rect inside ONE tile: offsetXY, scaleXY, both in 0..1 of the tile
    float4 UvRect;       // the sub-rectangle of the source one copy samples
    uint8_t4 Tint;       // multiplied into the sample, straight RGBA in four bytes
    float4 Clip;         // .x = the ROUNDED CLIP's slot, or -1; .yzw spare
};

struct TexFillPSInput
{
    float4 Position : SV_Position;
    float2 Local : TEXCOORD0;                   // varying: fragment's local mesh xy
    nointerpolation uint InstId : TEXCOORD1;    // instance -> re-read TextureGeomData in the PS (light signature)
    nointerpolation float Fade : TEXCOORD2;     // fetched in the VERTEX stage - see GradFillPSInput
    nointerpolation float4 ClipBox   : TEXCOORD3;   // ...and so is the ancestor's rounded clip
    nointerpolation float4 ClipRadii : TEXCOORD4;
};

[shader("vertex")]
TexFillPSInput TexFillVS(UI_VERTEX v, uint instanceId : SV_InstanceID)
{
    TextureGeomData* items = TextureGeometries;
    TextureGeomData it = items[instanceId];
    NodeSlot* nodes = Transforms;
    float4 world = mul(mul(float4(v.position, 1.0), it.Local), nodes[(uint)it.Params.w].World);

    TexFillPSInput o;
    o.Position = mul(world, Projection);
    o.Local = v.position.xy;
    o.InstId = instanceId;
    int texFadeSlot = int(it.Params.z);
    o.Fade = lerp(1.0, nodes[max(texFadeSlot, 0)].Params.x, step(0.0, float(texFadeSlot)));
    o.ClipBox   = ClipShapeBox(it.Clip.x);
    o.ClipRadii = ClipShapeRadii(it.Clip.x);
    return o;
}

[shader("fragment")]
float4 TexFillPS(TexFillPSInput input) : SV_Target
{
    TextureGeomData* items = TextureGeometries;
    TextureGeomData it = items[input.InstId];

    // 0..1 across the shape's box -> TILE space -> the content's rect inside one tile -> the source's sub-rectangle.
    float2 t = (input.Local - it.LocalBounds.xy) / max(it.LocalBounds.zw, float2(1e-4, 1e-4));
    float inside;
    float2 uv = TileSourceUv(t, it.Rotation, it.Tile, it.Drawn, it.UvRect, float2(it.Params.x, it.Params.x), it.Params.y,
        inside);

    // SampleLevel, not Sample: frac() breaks derivatives at tile seams, which would pick the smallest mip there.
    float4 tint = float4(it.Tint.x, it.Tint.y, it.Tint.z, it.Tint.w) * (1.0 / 255.0);
    float4 color = SourceTexture.SampleLevel(SourceSampler, uv, 0.0) * tint;
    color.a *= inside * input.Fade * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii);

    return color;
}

// The same textured instances drawn from their curves (see CurveMath.fxh), the mesh's local space carried down.
[shader("vertex")]
TexFillPSInput TexCurveVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    TextureGeomData* items = TextureGeometries;
    TextureGeomData it = items[instanceId];
    NodeSlot* nodes = Transforms;
    float4x4 toSlot = mul(it.Local, nodes[(uint)it.Params.w].World);
    float2 at = CurveQuadCorner(vertexId, toSlot);

    TexFillPSInput o;
    o.Position = mul(mul(float4(at, 0.0, 1.0), toSlot), Projection);
    o.Local = at;
    o.InstId = instanceId;
    int texFadeSlot = int(it.Params.z);
    o.Fade = lerp(1.0, nodes[max(texFadeSlot, 0)].Params.x, step(0.0, float(texFadeSlot)));
    o.ClipBox   = ClipShapeBox(it.Clip.x);
    o.ClipRadii = ClipShapeRadii(it.Clip.x);
    return o;
}

[shader("fragment")]
float4 TexCurvePS(TexFillPSInput input) : SV_Target
{
    TextureGeomData* items = TextureGeometries;
    TextureGeomData it = items[input.InstId];

    // Clamped to the box, as the fringe is: a pixel the edge cuts can have its centre just past the box, and unclamped
    // the single-copy clip would erase what of it the fill covers.
    float2 t = saturate((input.Local - it.LocalBounds.xy) / max(it.LocalBounds.zw, float2(1e-4, 1e-4)));
    float inside;
    float2 uv = TileSourceUv(t, it.Rotation, it.Tile, it.Drawn, it.UvRect, float2(it.Params.x, it.Params.x), it.Params.y,
        inside);

    float4 tint = float4(it.Tint.x, it.Tint.y, it.Tint.z, it.Tint.w) * (1.0 / 255.0);
    float4 color = SourceTexture.SampleLevel(SourceSampler, uv, 0.0) * tint;
    float coverage = CurveCoverage(input.Local);
    if (coverage <= 0.0)
    {
        discard;
    }

    color.a *= coverage * inside * input.Fade * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii);
    return color;
}

struct TexFringePSInput
{
    float4 Position : SV_Position;
    float2 Local    : TEXCOORD0;
    float Coverage  : TEXCOORD1;
    nointerpolation uint InstId : TEXCOORD2;
    nointerpolation float Fade : TEXCOORD3;   // fetched in the VERTEX stage - see GradFillPSInput
    nointerpolation float4 ClipBox   : TEXCOORD4;   // ...and so is the ancestor's rounded clip
    nointerpolation float4 ClipRadii : TEXCOORD5;
};

// The analytic-AA fringe of those textured instances: the SAME shared ring and the SAME instance buffer as the body, so
// N elements cost one draw instead of N. Unlike the pattern fringe - which takes the brush's low color - a picture has
// no single edge color, so the ring SAMPLES it, which is what makes the edge of a textured shape read as that shape's
// own edge rather than a colored halo.
[shader("vertex")]
TexFringePSInput InstancedTexFringeVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    FringeVertex v = RingCorner(vertexId);
    TextureGeomData* items = TextureGeometries;
    TextureGeomData it = items[instanceId];
    NodeSlot* nodes = Transforms;
    float4x4 m = mul(mul(it.Local, nodes[(uint)it.Params.w].World), Projection);

    TexFringePSInput o;
    float coverage;
    o.Position = ExpandFringe(v, m, coverage);
    o.Local = v.Position;
    o.Coverage = coverage;
    o.InstId = instanceId;
    int texFadeSlot = int(it.Params.z);
    o.Fade = lerp(1.0, nodes[max(texFadeSlot, 0)].Params.x, step(0.0, float(texFadeSlot)));
    o.ClipBox   = ClipShapeBox(it.Clip.x);
    o.ClipRadii = ClipShapeRadii(it.Clip.x);
    return o;
}

[shader("fragment")]
float4 TexFringePS(TexFringePSInput input) : SV_Target
{
    TextureGeomData* items = TextureGeometries;
    TextureGeomData it = items[input.InstId];

    // The ring is expanded a pixel OUTWARD, so its outer edge lies just outside the shape's box: clamp before mapping,
    // or the band would sample past the picture (and the single-copy clip below would erase the fringe entirely).
    float2 t = saturate((input.Local - it.LocalBounds.xy) / max(it.LocalBounds.zw, float2(1e-4, 1e-4)));
    float inside;
    float2 uv = TileSourceUv(t, it.Rotation, it.Tile, it.Drawn, it.UvRect, float2(it.Params.x, it.Params.x), it.Params.y,
        inside);

    float4 tint = float4(it.Tint.x, it.Tint.y, it.Tint.z, it.Tint.w) * (1.0 / 255.0);
    float4 color = SourceTexture.SampleLevel(SourceSampler, uv, 0.0) * tint;
    color.a *= inside * input.Coverage * input.Fade * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii);

    return color;
}

// ---- Fractal batch: the SAME SDF rounded-rect (self-AA shape + shared stroke), but the FILL is an escape-time FRACTAL
// (Julia/Mandelbrot) iterated per fragment - resolution-independent, no texture. Per-instance FractalRectData from a BDA
// storage buffer by SV_InstanceID; the PS re-reads the record, maps the fragment to the complex plane, iterates z=z^2+c and
// colors by the smooth escape count. With the animate flag set, a Julia's C drifts on a Lissajous over the global Time.
struct FractalRectData
{
    float4 Bounds;       // NODE-local x, y, w, h
    float4 Params;       // .x corner radius, .y type (0 Julia/1 Mandelbrot), .z transform slot, .w max iterations
    float4 Radii;        // corner radii: x = TL, y = TR, z = BR, w = BL
    float4 Geom;         // .x/.y complex-plane center, .z zoom, .w morph speed
    float4 Julia;        // .x/.y Julia constant C, .z animate flag, .w reserved
    uint8_t4 Color1;      // straight RGBA in four bytes
    uint8_t4 Color2;      // straight RGBA in four bytes
    uint8_t4 StrokeColor; // straight stroke RGBA in four bytes (alpha 0 -> no stroke)
    float4 Stroke0;      // width_px, align, dashOn, dashGap
    float4 Stroke1;      // dashOffset, trimStart, trimEnd, flags
    float4 Dash;         // dash runs 2..5 (device px); runs 0 and 1 ride in Stroke0.zw, the count in Stroke1.w
    float4 Ref;          // perturbation: .x orbit start index (into Orbits), .y orbit length, .zw the delta offset
    float4 Clip;         // .x = the ROUNDED CLIP's slot, or -1; .y the opacity slot; .z = the pen's floor in device
                         // px (0 none); .w spare
};

struct FractalPSInput
{
    float4 Position : SV_Position;
    float2 Local    : TEXCOORD0;   // fragment relative to the rect CENTER (SDF space, device px)
    float2 Half     : TEXCOORD1;   // rect half-size
    float4 Radii    : TEXCOORD2;   // corner radii (TL, TR, BR, BL) in device px
    nointerpolation uint InstId : TEXCOORD3;   // instance -> re-read FractalRectData in the PS
    nointerpolation float Scale : TEXCOORD4;   // slot unit -> device px, for the stroke record the PS re-reads
    nointerpolation float4 ClipBox   : TEXCOORD5;   // the ancestor's rounded clip, fetched in the VERTEX stage
    nointerpolation float4 ClipRadii : TEXCOORD6;
    nointerpolation float Fade : TEXCOORD7;         // ...and the element's alpha from its opacity slot
};

[shader("vertex")]
FractalPSInput FractalRectInstancedVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    FractalRectData* items = FractalRects;
    FractalRectData it = items[instanceId];

    FractalPSInput o;
    float2 corner = float2(vertexId & 1u, (vertexId >> 1u) & 1u);
    // SDF inputs in DEVICE PIXELS, same scheme as RectBatchInstancedVS. The fractal plane is Local/Half - a RATIO - so
    // the change of unit leaves the image itself untouched; only the stroke record needs Scale.
    NodeSlot* nodes = Transforms;
    float4x4 nodeWorld = nodes[(uint)it.Params.z].World;
    float2 px = SlotPixelScale(nodeWorld);
    float iso = min(px.x, px.y);

    float widthPx = FloorWidthPx(it.Stroke0.x * iso, it.Clip.z);
    float outsetPx = max(widthPx * (0.5 * (1.0 + it.Stroke0.y) + 0.5), 0.0) + 1.0;
    float2 localPos = it.Bounds.xy + corner * it.Bounds.zw + (corner * 2.0 - 1.0) * (outsetPx / px);
    float4 worldPos = mul(float4(localPos, 0.0, 1.0), nodeWorld);
    o.Position = mul(worldPos, Projection);
    o.Half   = it.Bounds.zw * 0.5 * px;
    o.Local  = (corner - 0.5) * it.Bounds.zw * px + (corner * 2.0 - 1.0) * outsetPx;
    o.Radii = it.Radii * iso;
    o.InstId = instanceId;
    o.Scale  = iso;
    o.ClipBox   = ClipShapeBox(it.Clip.x);
    o.ClipRadii = ClipShapeRadii(it.Clip.x);
    int fracFadeSlot = int(it.Clip.y);   // int test + branch, not lerp/step - see the pattern VS
    o.Fade = fracFadeSlot < 0 ? 1.0 : nodes[(uint)fracFadeSlot].Params.x;
    return o;
}

// Newton fractal for z^3 - 1: iterate z -= (z^3-1)/(3z^2) and color by which of the 3 cube roots of unity it converges to
// (a different look from escape-time: smooth color basins with a fractal border). The two brush colors take two roots,
// their blend the third; converging in more steps (near a border) darkens, so the boundary detail shows. Animate flows it.
float4 NewtonColor(float2 z, int maxIt, bool animate, float4 c1, float4 c2)
{
    float2 r0 = float2(1.0, 0.0);
    float2 r1 = float2(-0.5, 0.8660254);
    float2 r2 = float2(-0.5, -0.8660254);
    int hit = -1;
    int i = 0;
    for (i = 0; i < maxIt; i++)
    {
        float2 z2 = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y);              // z², проверка UTF-8
        float2 z3 = float2(z2.x * z.x - z2.y * z.y, z2.x * z.y + z2.y * z.x);    // z^3
        float2 num = float2(z3.x - 1.0, z3.y);                                  // z^3 - 1
        float2 den = float2(3.0 * z2.x, 3.0 * z2.y);                            // 3z^2
        float dd = dot(den, den);
        if (dd < 1e-12) break;                                                  // derivative ~0: stationary
        z -= float2(num.x * den.x + num.y * den.y, num.y * den.x - num.x * den.y) / dd;   // z -= num/den (complex divide)
        if (dot(z - r0, z - r0) < 1e-4) { hit = 0; break; }
        if (dot(z - r1, z - r1) < 1e-4) { hit = 1; break; }
        if (dot(z - r2, z - r2) < 1e-4) { hit = 2; break; }
    }

    float shade = 1.0 - float(i) / float(maxIt);
    if (animate) shade = frac(shade + Time * 0.05);

    float4 baseCol;
    if (hit == 0) baseCol = c1;
    else if (hit == 1) baseCol = c2;
    else if (hit == 2) baseCol = lerp(c1, c2, 0.5);
    else baseCol = float4(0.0, 0.0, 0.0, c1.w);   // never converged
    return float4(baseCol.rgb * shade, baseCol.w);
}

// One shading body called by a pass per formula with compile-time literals, so each pass keeps only its own arithmetic
// and the formula test leaves the hot loop.
float4 FractalShade(FractalPSInput input, int formula, bool deep)
{
    FractalRectData* items = FractalRects;
    FractalRectData it = items[input.InstId];

    float lim = min(input.Half.x, input.Half.y);
    float4 r4 = min(input.Radii, float4(lim, lim, lim, lim));
    int joinType = int(fmod(floor(it.Stroke1.w / 4096.0), 8.0));
    float d = SdRoundRectJoin(input.Local, input.Half, r4, joinType);

    // Fragment -> complex plane, aspect-correct: the smaller axis spans 3/zoom around the center, so pixels stay square.
    float minHalf = max(min(input.Half.x, input.Half.y), 1e-4);
    float2 cp = it.Geom.xy + (input.Local / minHalf) * (1.5 / max(it.Geom.z, 1e-4));

    bool animate = it.Julia.z > 0.5;
    int maxIt = min(int(it.Params.w), 2000);   // mirrors FractalRectCollector.MaxIterations - a deep zoom spends its detail here

    float4 fill;
    if (formula == 5)   // Newton: convergence basins, not escape-time (C-mode does not apply, so map the raw fragment)
    {
        fill = NewtonColor(cp, maxIt, animate,
                           float4(it.Color1) * (1.0 / 255.0), float4(it.Color2) * (1.0 / 255.0));
    }
    else
    {
        bool mandelbrot = int(it.Params.y) == 1;   // C-mode: c is the fragment (else c is the Julia constant)
        float zoomAmp = 1.0 / max(it.Geom.z, 1.0);   // shrink morph amplitude as we zoom past 1x, so on-screen morph speed stays constant
        float expo = it.Geom.w;                       // Multibrot exponent d
        if (formula == 4 && animate)
        {
            expo = max(1.1, it.Geom.w + 2.0 * zoomAmp * sin(Time * 0.6));   // breathe the petals open/closed instead of drifting C
        }

        // PERTURBATION deep-zoom path (armed only for Quadratic z2+c past the deep threshold): iterate the SMALL delta
        // from a high-precision reference orbit (Z_n from Orbits) so the whole shader stays float32 - no fp64, no wall.
        // NOTE: the .fx parser does NOT accept unary '!', so bail flags are tested as (escaped) / (escaped == false).
        if (deep)   // the DEEP pass; the collector batches those instances apart, so Ref carries a reference orbit here
        {
            // One orbit step is a HI/LO pair: .xy the float that holds most of Z_n, .zw the residue it could not. A single
            // float carries an O(1) reference to ~1e-7 ABSOLUTE, and at zoom 1e8 the whole view is 1.5e-8 across - the
            // reference missed by more than the picture. The pair carries ~1e-14.
            float4* orbit = Orbits;
            uint ofs = (uint)it.Ref.x;
            int rlen = (int)it.Ref.y;
            // pixel offset from the REFERENCE point: (pixel - view center) + (view center - C_ref). The second term
            // (Ref.zw) lets the CPU pick a reference OFF the view center (a longer-living orbit) without moving the view.
            float2 delta = (input.Local / minHalf) * (1.5 / max(it.Geom.z, 1e-4)) + float2(it.Ref.z, it.Ref.w);
            // Segmented rebasing (Zhuoran): the orbit is indexed by the inner loop counter, and a rebase restarts the outer
            // loop at j=0. Ref0 keeps its lo residue, or each rebase reinjects ~1e-7 error.
            float4 Ref0p = orbit[ofs];
            float2 Ref0 = Ref0p.xy;
            float2 Ref0l = Ref0p.zw;
            float2 dz = mandelbrot ? float2(0.0, 0.0) : delta;   // Delta = z - Ref[j]. Julia: z0 offset. Mandelbrot: 0.
            float2 dc = mandelbrot ? delta : float2(0.0, 0.0);   // per-iteration additive. Mandelbrot: dc. Julia: 0.
            int pi = 0;                 // true iteration count (drives the smooth color)
            float2 pz = float2(0.0, 0.0);
            bool escaped = false;
            bool done = false;
            for (int seg = 0; seg < maxIt; seg++)   // one segment per rebase
            {
                bool rebased = false;
                int j = 0;
                for (j = 0; j + 1 < rlen; j++)
                {
                    float4 Zp = orbit[ofs + (uint)j];                 // MONOTONIC reference index
                    float2 Z = Zp.xy;
                    float2 Zl = Zp.zw;
                    // (Z + dz) + Zl, in that order: near a rebase Z and dz nearly cancel, and adding two close floats is
                    // EXACT - so the residue survives instead of being rounded away by an O(1) sum.
                    pz = (Z + dz) + Zl;                               // full z at this true iteration
                    if (dot(pz, pz) > 256.0) { escaped = true; break; }
                    if (pi + 1 >= maxIt) { done = true; break; }
                    // Z*dz with the residue kept as its own product. Each product needs only RELATIVE precision, which a
                    // float has; what a single float could not do is REPRESENT Z closely enough in the first place.
                    float2 zdz = float2(Z.x * dz.x - Z.y * dz.y, Z.x * dz.y + Z.y * dz.x)
                               + float2(Zl.x * dz.x - Zl.y * dz.y, Zl.x * dz.y + Zl.y * dz.x);
                    float2 dz2 = float2(dz.x * dz.x - dz.y * dz.y, 2.0 * dz.x * dz.y);
                    dz = 2.0 * zdz + dz2 + dc;                        // advance the perturbation
                    pi = pi + 1;
                    float4 Znp = orbit[ofs + (uint)(j + 1)];          // MONOTONIC (j+1)
                    pz = (Znp.xy + dz) + Znp.zw;                      // full z after the advance
                    // Rebase when the delta from Ref0 is smaller, not |pz|; they differ for Julia, whose reference starts at z0.
                    float2 rel = (pz - Ref0) - Ref0l;
                    if (dot(rel, rel) < dot(dz, dz)) { dz = rel; rebased = true; break; }
                }
                if (escaped) break;
                if (done) break;
                if (rebased == false) dz = (pz - Ref0) - Ref0l;   // inner ran out of reference -> rebase, restart at j=0
            }
            if (escaped)
            {
                float sm = float(pi) + 1.0 - log2(max(0.5 * log2(dot(pz, pz)), 1.0));   // smooth continuous escape count
                float ramp = sqrt(saturate(sm / float(maxIt)));
                if (animate && mandelbrot) ramp = frac(ramp + Time * 0.06);   // Mandelbrot-mode: flow the color ramp
                fill = lerp(float4(it.Color1), float4(it.Color2), ramp) * (1.0 / 255.0);
            }
            else
            {
                fill = float4(0.0, 0.0, 0.0, float(it.Color1.w) * (1.0 / 255.0));   // inside the set / glitch / ran out of reference: black
            }
        }
        else
        {

        // Julia: z0 = fragment, c = constant (drifting when animate). Mandelbrot: z0 = 0, c = fragment.
        float2 z;
        float2 cc;
        if (mandelbrot)
        {
            z = float2(0.0, 0.0);
            cc = cp;
        }
        else
        {
            z = cp;
            cc = it.Julia.xy;
            if (animate && formula != 4)   // drift the Julia constant; amplitude ~ 1/Zoom so the on-screen morph speed is zoom-independent
            {
                float amp = 0.18 * zoomAmp;
                cc += float2(amp * cos(Time), amp * sin(Time * 0.86));
            }

        }

        int i = 0;
        for (i = 0; i < maxIt; i++)
        {
            if (formula == 1)         // Burning Ship: (|Re z| + i|Im z|)^2 + c
            {
                float2 za = float2(abs(z.x), abs(z.y));
                z = float2(za.x * za.x - za.y * za.y, 2.0 * za.x * za.y) + cc;
            }
            else if (formula == 2)    // Tricorn / Mandelbar: conj(z)^2 + c
            {
                z = float2(z.x * z.x - z.y * z.y, -2.0 * z.x * z.y) + cc;
            }
            else if (formula == 3)    // Celtic: |Re(z^2)| + i*Im(z^2) + c
            {
                float2 z2 = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y);
                z = float2(abs(z2.x), z2.y) + cc;
            }
            else if (formula == 4)    // Multibrot: z^d + c (polar power, so d may be fractional / animated)
            {
                float rr = length(z);
                float th = atan2(z.y, z.x);
                z = pow(rr, expo) * float2(cos(expo * th), sin(expo * th)) + cc;
            }
            else                      // Quadratic: z^2 + c
            {
                z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + cc;
            }
            if (dot(z, z) > 256.0) break;   // large bail radius -> smoother continuous coloring
        }

        if (i >= maxIt)
        {
            fill = float4(0.0, 0.0, 0.0, float(it.Color1.w) * (1.0 / 255.0));   // inside the set: black (keep the fill alpha)
        }
        else
        {
            float sm = float(i) + 1.0 - log2(max(0.5 * log2(dot(z, z)), 1.0));   // smooth (continuous) escape count
            float ramp = sqrt(saturate(sm / float(maxIt)));
            if (animate && mandelbrot && formula != 4)   // Mandelbrot-mode: no C to morph, so flow the color ramp instead
            {
                ramp = frac(ramp + Time * 0.06);
            }
            fill = lerp(float4(it.Color1), float4(it.Color2), ramp) * (1.0 / 255.0);
        }
        }   // end float-path else (perturbation deep path handled above)
    }

    // Stroke lengths: slot units -> device pixels, to match the SDF above (align/trim/flags are unitless).
    float sc = input.Scale;
    float widthPx = it.Stroke0.x * sc;
    float mask = 1.0;
    if (it.Stroke0.z > 0.0 || it.Stroke1.y > 0.0 || it.Stroke1.z < 1.0)
    {
        float halfW = widthPx * 0.5;
        float perim;
        float s = RoundRectArc(input.Local, input.Half, r4, perim);
        float dPerp = d - it.Stroke0.y * halfW;
        float capScl = ArcCapScale(RoundRectCurvRadius(input.Local, input.Half, r4), dPerp);
        mask = DashTrimMask(s, s, perim, it.Stroke0.z * sc, it.Stroke0.w * sc, it.Stroke1.x * sc, it.Stroke1.y,
                            it.Stroke1.z, dPerp * capScl, halfW * capScl, it.Stroke1.w, it.Dash * sc);
    }
    float4 fracOut = CompositeFillStroke(d, input.Local, RectDistanceRate(joinType), fill, float4(it.StrokeColor) * (1.0 / 255.0),
                                         widthPx, it.Stroke0.y, mask, 0.0);
    return float4(fracOut.rgb, fracOut.a * input.Fade * ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii));
}

// The literals are what makes the split real: each of these specializes the body above down to one formula. Keep them
// literal - reading the selector back out of the record here would put every formula into every pass again.
[shader("fragment")]
float4 FractalQuadraticPS(FractalPSInput input) : SV_Target { return FractalShade(input, 0, false); }

[shader("fragment")]
float4 FractalBurningShipPS(FractalPSInput input) : SV_Target { return FractalShade(input, 1, false); }

[shader("fragment")]
float4 FractalTricornPS(FractalPSInput input) : SV_Target { return FractalShade(input, 2, false); }

[shader("fragment")]
float4 FractalCelticPS(FractalPSInput input) : SV_Target { return FractalShade(input, 3, false); }

[shader("fragment")]
float4 FractalMultibrotPS(FractalPSInput input) : SV_Target { return FractalShade(input, 4, false); }

[shader("fragment")]
float4 FractalNewtonPS(FractalPSInput input) : SV_Target { return FractalShade(input, 5, false); }

// Deep zoom is Quadratic only - the perturbation reference orbit is iterated as z2+c on the CPU side too.
[shader("fragment")]
float4 FractalDeepPS(FractalPSInput input) : SV_Target { return FractalShade(input, 0, true); }



// =====================================================================================================================
// TECHNIQUES - one per brush family. Passes: Sdf (rounded rect, ellipse or polygon by record flag), Mesh (a shared
// tessellated mesh), Curve (a quad over the fill's curves), Fringe (the AA ring). Accessor: technique Gradient, pass Sdf
// -> Effect.GradientSdfPass.
// =====================================================================================================================

// LINEAR / RADIAL gradients, up to 8 stops, interpolated perceptually in OKLab. The gradient is evaluated PER FRAGMENT,
// so a stop never bands across a large fill the way a per-vertex ramp does.
technique Gradient
{
    pass Sdf
    {
        VertexShader = GradientRectInstancedVS;
        PixelShader = GradientPS;
    }

    pass Mesh
    {
        VertexShader = GradientFillVS;
        PixelShader = GradientFillPS;
    }

    pass Curve
    {
        VertexShader = GradientCurveVS;
        PixelShader = GradientCurvePS;
    }

    // Its OWN pixel stage, unlike the other two families: the ring is colored by the gradient per fragment, so it
    // cannot share the flat-color fringe.
    pass Fringe
    {
        VertexShader = InstancedGradientFringeVS;
        PixelShader = InstancedGradientFringePS;
    }
}

// Procedural two-color fills, one pass per kind and carrier (CheckerboardSdf, CheckerboardMesh). Their fringe is
// BatchEffect's PatternFringe, since one shader may belong to only one effect.
technique Pattern
{
    pass CheckerboardSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = PatternCheckerboardSdfPS;
    }

    pass StripesSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = PatternStripesSdfPS;
    }

    pass DotsSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = PatternDotsSdfPS;
    }

    pass GridSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = PatternGridSdfPS;
    }

    pass HexagonSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = PatternHexagonSdfPS;
    }

    pass HatchSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = PatternHatchSdfPS;
    }

    pass WeaveSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = PatternWeaveSdfPS;
    }

    pass CheckerboardMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = PatternCheckerboardMeshPS;
    }

    pass StripesMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = PatternStripesMeshPS;
    }

    pass DotsMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = PatternDotsMeshPS;
    }

    pass GridMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = PatternGridMeshPS;
    }

    pass HexagonMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = PatternHexagonMeshPS;
    }

    pass HatchMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = PatternHatchMeshPS;
    }

    pass WeaveMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = PatternWeaveMeshPS;
    }

    pass CheckerboardCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = PatternCheckerboardCurvePS;
    }

    pass StripesCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = PatternStripesCurvePS;
    }

    pass DotsCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = PatternDotsCurvePS;
    }

    pass GridCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = PatternGridCurvePS;
    }

    pass HexagonCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = PatternHexagonCurvePS;
    }

    pass HatchCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = PatternHatchCurvePS;
    }

    pass WeaveCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = PatternWeaveCurvePS;
    }
}

// NOISE FIELDS. Same shape as Pattern above - these are a separate technique because they are a separate FAMILY of
// brush (NoiseBrush, not PatternBrush), even though both bake into the same record and the same vertex stage.
technique Noise
{
    pass SimplexSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = NoiseSimplexSdfPS;
    }

    pass PerlinSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = NoisePerlinSdfPS;
    }

    pass ValueSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = NoiseValueSdfPS;
    }

    pass WorleySdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = NoiseWorleySdfPS;
    }

    pass RidgedSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = NoiseRidgedSdfPS;
    }

    pass TurbulenceSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = NoiseTurbulenceSdfPS;
    }

    pass VoronoiSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = NoiseVoronoiSdfPS;
    }

    pass CombustibleSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = NoiseCombustibleSdfPS;
    }

    pass CircuitSdf
    {
        VertexShader = PatternRectInstancedVS;
        PixelShader = NoiseCircuitSdfPS;
    }

    pass SimplexMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = NoiseSimplexMeshPS;
    }

    pass PerlinMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = NoisePerlinMeshPS;
    }

    pass ValueMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = NoiseValueMeshPS;
    }

    pass WorleyMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = NoiseWorleyMeshPS;
    }

    pass RidgedMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = NoiseRidgedMeshPS;
    }

    pass TurbulenceMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = NoiseTurbulenceMeshPS;
    }

    pass VoronoiMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = NoiseVoronoiMeshPS;
    }

    pass CombustibleMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = NoiseCombustibleMeshPS;
    }

    pass CircuitMesh
    {
        VertexShader = PatternFillVS;
        PixelShader = NoiseCircuitMeshPS;
    }

    pass SimplexCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = NoiseSimplexCurvePS;
    }

    pass PerlinCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = NoisePerlinCurvePS;
    }

    pass ValueCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = NoiseValueCurvePS;
    }

    pass WorleyCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = NoiseWorleyCurvePS;
    }

    pass RidgedCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = NoiseRidgedCurvePS;
    }

    pass TurbulenceCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = NoiseTurbulenceCurvePS;
    }

    pass VoronoiCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = NoiseVoronoiCurvePS;
    }

    pass CombustibleCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = NoiseCombustibleCurvePS;
    }

    pass CircuitCurve
    {
        VertexShader = PatternCurveVS;
        PixelShader = NoiseCircuitCurvePS;
    }
}

// SAMPLED fills - an ImageBrush is one instance, a NineSliceBrush is nine, so a whole skinned frame is still one draw.
// ONE texture per segment (see SourceTexture).
technique Texture
{
    pass Sdf
    {
        VertexShader = TexRectInstancedVS;
        PixelShader = TexRectPS;
    }

    pass SdfArray
    {
        VertexShader = TexRectInstancedVS;
        PixelShader = TexRectArrayPS;
    }

    pass Mesh
    {
        VertexShader = TexFillVS;
        PixelShader = TexFillPS;
    }

    pass Curve
    {
        VertexShader = TexCurveVS;
        PixelShader = TexCurvePS;
    }

    pass Fringe
    {
        VertexShader = InstancedTexFringeVS;
        PixelShader = TexFringePS;
    }
}

// The backdrop materials live in MaterialEffect.fx.

// Escape-time fractals: z = z^2 + c per fragment, colored by the smooth escape count, with a perturbation path for
// deep zoom (see Orbits). No Fill/Fringe - a fractal fills a rect, and its edge is the rect's own SDF.
technique Fractal
{
    pass QuadraticSdf
    {
        VertexShader = FractalRectInstancedVS;
        PixelShader = FractalQuadraticPS;
    }

    pass BurningShipSdf
    {
        VertexShader = FractalRectInstancedVS;
        PixelShader = FractalBurningShipPS;
    }

    pass TricornSdf
    {
        VertexShader = FractalRectInstancedVS;
        PixelShader = FractalTricornPS;
    }

    pass CelticSdf
    {
        VertexShader = FractalRectInstancedVS;
        PixelShader = FractalCelticPS;
    }

    pass MultibrotSdf
    {
        VertexShader = FractalRectInstancedVS;
        PixelShader = FractalMultibrotPS;
    }

    pass NewtonSdf
    {
        VertexShader = FractalRectInstancedVS;
        PixelShader = FractalNewtonPS;
    }

    pass DeepSdf
    {
        VertexShader = FractalRectInstancedVS;
        PixelShader = FractalDeepPS;
    }
}
