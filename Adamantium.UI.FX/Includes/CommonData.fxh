// The contract shared by BatchEffect.fx and BrushEffect.fx: vertex layouts, globals both need, and common maths. A global
// only one effect reads belongs in that effect.
struct UI_VERTEX
{
    float4 position : POSITION;
    float3 normal: NORMAL;
    float2 uv0: TEXCOORD0;
    float2 uv1: TEXCOORD1;
};

struct VERTEX_OUTPUT
{
    float4 position : SV_POSITION;
    float3 normal: NORMAL;
    float2 uv0: TEXCOORD0;
    float2 uv1: TEXCOORD1;
};

// ---- Globals both effects declare ------------------------------------------------------------------------------
// ---- Globals both effects declare -----------------------------------------------------------------------------
float4x4 Projection;

// Global frame time in seconds, advanced by the render loop each present. Only the fractal's auto-morph reads it; every
// other pass ignores it. Unset (0) = no drift, so a static fractal renders fine before the loop starts feeding it.
float Time;

// Per-instance data by BUFFER DEVICE ADDRESS (BDA), not a descriptor-heap StructuredBuffer: the SV_InstanceID-indexed
// StructuredBuffer form did not bind/read on this device (the fill rasterized nothing - World came out garbage), while
// BDA is the engine's proven GPU-storage pattern (see StrokeEffect/FillFringeEffect: uint64_t address + (T*)addr).
uint64_t InstancesAddress;

float2 ViewportSize;      // render target size in DEVICE pixels - the NDC <-> pixel basis for the fringe offset

float FringePixels;       // fringe width in DEVICE pixels

// The transform table (Rendering/TransformTable.cs): one world matrix per motion node, by instance slot; slot 0 is
// identity for world-baked instances.
uint64_t TransformsAddress;

// One table entry: the node's matrix and opacity. Params.x alpha (1 opaque), .y parent opacity slot (-1 none, composed
// on the CPU), .zw reserved; 16-byte aligned.
struct NodeSlot
{
    float4x4 World;
    float4   Params;
};

// ---- Bound resources and the records both effects read ----------------------------------------------------------
// One bound texture per segment, used by brushes and by the halo's baked distance field (HaloFieldDistance). At t2/s2
// because FontEffect's glyph atlas occupies t1.
Texture2D SourceTexture : register(t2);
SamplerState SourceSampler : register(s2);

// One PROCEDURAL fill instance on arbitrary geometry. Shared because its FILL is a brush (BrushEffect evaluates the
// pattern from it) while its FRINGE is not: a one-pixel ring does not evaluate a pattern, it just takes the brush's low
// color, so the ring is the same flat pass the solid fills use and lives with them in BatchEffect. Both read this
// record, so it belongs to neither.
struct PatternGeomData
{
    float4x4 Local;      // element local -> SLOT space (the slot's matrix is applied on top, from the transform table)
    float4 Params;       // .y pattern type, .z cell (LOCAL units), .w transform-table slot. .x = opacity slot
    float4 LocalBounds;  // shape local bounds: minXY, sizeXY
    float4 Color1;
    float4 Color2;
    float4 Color3;
    float4 Noise;        // x octaves (sign=animate), y seed, z lacunarity, w gain (or combustible fire-palette flag)
    float4 Anim;         // .x = offset subtracted from the clock while animating, .y = the phase held while paused,
                         // .w = the ROUNDED CLIP's slot (-1 = none)
};

// ---- InstancedFringe pass: the analytic-AA fringe of the SAME instances, as one instanced draw --------------------
// Every instance shares one scale-free ring (FringeGeometry.cs) and reads the body's GeometryInstance buffer; width is
// applied here in device pixels.
struct FringeVertex
{
    float2 Position : POSITION;
    float2 Dir0     : TEXCOORD0;   // incoming edge direction, Winding folded into its sign; zero on the contour itself
    float2 Dir1     : TEXCOORD1;   // outgoing edge direction
};

struct FringePSInput
{
    float4 Position : SV_Position;
    float4 Color    : COLOR0;
    float  Coverage : TEXCOORD0;
    // The rounded ancestor clip's SHAPE, fetched in the vertex stage (see ClipShapeBox): rect in device px + radii.
    nointerpolation float4 ClipBox   : TEXCOORD1;
    nointerpolation float4 ClipRadii : TEXCOORD2;
};

