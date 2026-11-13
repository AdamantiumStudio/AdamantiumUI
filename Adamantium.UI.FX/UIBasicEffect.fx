#include "Includes/CommonData.fxh"
#include "Includes/ColorMath.fxh"
#include "Includes/TileMath.fxh"
#include "Includes/GradientMath.fxh"
#include "Includes/GradientUniforms.fxh"
#include "Includes/CurveMath.fxh"
#include "Includes/CurveTiles.fxh"

float4x4 wvp;

float4 fillColor;
SamplerState sampleType;
Texture2D shaderTexture;

// An animation's frames are layers of one texture; the layer to show arrives as an effect constant, one frame per draw.
Texture2DArray shaderTextureArray;
float textureLayer;

// A PALETTED animation: each layer holds one byte per pixel, an index into a 256x1 palette shared by every frame.
Texture2DArray paletteIndices;
Texture2D palette;

float opacity = 1;

// A TEXTURED fill on ARBITRARY tessellated geometry (Polygon/Path). The rounded rect and the ellipse sample a texture in
// their SDF batches, but a tessellated shape has neither an SDF nor a usable uv0 - the mesh carries the outline, not a
// mapping - so the picture is mapped across the shape's own LOCAL bounding box here instead. Same tiling arithmetic as
// the batch (ImageTiling): WHICH part of the source one copy takes and HOW MANY copies fit.
float4 fillBounds;    // the shape's local box (x, y, w, h) the picture is mapped across
float4 texTile;       // tile grid over that box: tiles per axis (.xy), grid origin in tiles (.zw)
float4 texRotation;   // 2x2 mapping a fragment back into the unturned grid, row-major (identity = 1,0,0,1)
float4 texDrawn;      // the content's rect inside ONE tile, as (offsetX, offsetY, scaleX, scaleY) in 0..1 of it
float4 texUvRect;     // the sub-rectangle of the source one copy samples
float4 texTint = float4(1, 1, 1, 1);
float4 texAdjust;     // the picture's colors, before the tint - see AdjustColor
float texRepeat;      // 1 = the tile repeats; 0 = a single copy, which must never wrap
float texMirror;      // mirror flags: 1 = X, 2 = Y, 3 = both

// A COLORED MESH: triangles with a color at each corner, the corners read by address (ColoredMeshVertexRecord.cs) - no
// vertex format of their own - and the color interpolated across each triangle.
struct ColoredMeshVertex
{
    float2 Position;
    float4 Color;
};

ColoredMeshVertex* MeshVertices;

// 1: the mesh is cut to the outline at CurveData by its curve coverage; 0: the triangles' own edges.
uint MeshOutlined;

// How many triangles the mesh has: a later one is drawn nearer, so where they overlap the depth keeps the last.
uint MeshTriangleCount;

struct COLORED_OUTPUT
{
    float4 position : SV_POSITION;
    float4 color : COLOR0;
    float2 local : TEXCOORD0;
};

[shader("vertex")]
COLORED_OUTPUT ColoredMeshVS(uint vertexId : SV_VertexID)
{
    ColoredMeshVertex* vertices = MeshVertices;
    ColoredMeshVertex corner = vertices[vertexId];
    COLORED_OUTPUT output;
    output.position = mul(float4(corner.Position, 0, 1), wvp);
    float order = float(vertexId / 3u) + 1.0;
    output.position.z = (1.0 - order / (float(MeshTriangleCount) + 2.0)) * output.position.w;
    output.color = corner.Color;
    output.local = corner.Position;
    return output;
}

[shader("fragment")]
float4 ColoredMesh_PS(COLORED_OUTPUT input) : SV_TARGET
{
    float4 color = input.color;
    color.a *= opacity;
    if (MeshOutlined != 0u)
    {
        color.a *= CurveCoverage(input.local);
    }

    return color;
}

[shader("vertex")]
VERTEX_OUTPUT UIVertexShader(UI_VERTEX input)
{
    VERTEX_OUTPUT output;
    output.position = mul(float4(input.position, 1), wvp);
    output.uv0 = float2(0, 0);
    return output;
}

// A textured draw: the vertex says where in its texture it samples.
[shader("vertex")]
VERTEX_OUTPUT TexturedVS(UI_TEXTURED_VERTEX input)
{
    VERTEX_OUTPUT output;
    output.position = mul(float4(input.position, 1), wvp);
    output.uv0 = input.uv0;
    return output;
}

[shader("fragment")]
float4 SolidColor_PS(VERTEX_OUTPUT input) : SV_TARGET
{
	float4 result = fillColor;
    result.a *= opacity;   // combine the color's own alpha with the element/brush opacity (don't drop fillColor.a)
    return result;
}

[shader("fragment")]
float4 Textured_PS(VERTEX_OUTPUT input) : SV_TARGET
{
   //float4 color = shaderTexture.Sample(sampleType, input.uv0) * fillColor;
   float4 color = shaderTexture.Sample(sampleType, input.uv0);

   // Apply the control's opacity so a textured element (image / RenderTargetPanel) honors Opacity, like SolidColor_PS.
   color.a *= opacity;

   return color;
}

// The shape's own box, not the mesh's uv0: a tessellated outline has no mapping baked into it.
[shader("vertex")]
VERTEX_OUTPUT TexturedFillVS(UI_VERTEX input)
{
    VERTEX_OUTPUT output;
    output.position = mul(float4(input.position, 1), wvp);
    output.uv0 = (input.position.xy - fillBounds.xy) / max(fillBounds.zw, float2(1e-4, 1e-4));
    return output;
}

// A GRADIENT fill drawn by its unit - one the batched records cannot carry (a radial between two circles): the gradient
// of GradientUniforms over the shape's local box, as the batches would paint it.
[shader("fragment")]
float4 GradientFill_PS(VERTEX_OUTPUT input) : SV_TARGET
{
    float4 color = GradientColorIn(input.uv0, float4(0.0, 0.0, 1.0, 1.0));
    color.a *= opacity;
    return color;
}

[shader("fragment")]
float4 TexturedFill_PS(VERTEX_OUTPUT input) : SV_TARGET
{
    float inside;
    float2 uv = TileSourceUv(input.uv0, texRotation, texTile, texDrawn, texUvRect, float2(texRepeat, texRepeat),
        texMirror, inside);

    // SampleLevel, not Sample: frac() makes uv discontinuous at every tile seam, and the derivative Sample picks its mip
    // by spikes there - one column of pixels drawn from the smallest mip, i.e. a thin line down each seam.
    float4 color = shaderTexture.SampleLevel(sampleType, uv, 0.0);
    color.rgb = AdjustColor(color.rgb, texAdjust);
    color *= texTint;
    color.a *= inside;
    color.a *= opacity;

    return color;
}

[shader("fragment")]
float4 TexturedArray_PS(VERTEX_OUTPUT input) : SV_TARGET
{
   float4 color = shaderTextureArray.Sample(sampleType, float3(input.uv0, textureLayer));

   color.a *= opacity;

   return color;
}

float4 PaletteTexel(int2 p, int2 size, int layer)
{
   p = clamp(p, int2(0, 0), size - int2(1, 1));
   float index = paletteIndices.Load(int4(p, layer, 0)).r;
   return palette.Load(int3((int)round(index * 255.0), 0, 0));
}

// Indices cannot be filtered - a blend of two palette numbers is a third, unrelated color - so the four neighbors are
// looked up first and their COLORS blended, which is what a linear sample of the full-color frame gives.
[shader("fragment")]
float4 PalettedArray_PS(VERTEX_OUTPUT input) : SV_TARGET
{
   uint width, height, layers;
   paletteIndices.GetDimensions(width, height, layers);
   int2 size = int2(width, height);
   int layer = (int)textureLayer;

   float2 texel = input.uv0 * float2(size) - 0.5;
   int2 p = (int2)floor(texel);
   float2 f = texel - floor(texel);

   float4 top = lerp(PaletteTexel(p, size, layer), PaletteTexel(p + int2(1, 0), size, layer), f.x);
   float4 bottom = lerp(PaletteTexel(p + int2(0, 1), size, layer), PaletteTexel(p + int2(1, 1), size, layer), f.x);
   float4 color = lerp(top, bottom, f.y);

   color.a *= opacity;

   return color;
}

technique Basic
{
	pass SolidColor
	{
		VertexShader = UIVertexShader;
		PixelShader = SolidColor_PS;
	}

	pass Textured
	{
		VertexShader = TexturedVS;
		PixelShader = Textured_PS;
	}

	pass TexturedFill
	{
		VertexShader = TexturedFillVS;
		PixelShader = TexturedFill_PS;
	}

	pass GradientFill
	{
		VertexShader = TexturedFillVS;
		PixelShader = GradientFill_PS;
	}

	pass ColoredMesh
	{
		VertexShader = ColoredMeshVS;
		PixelShader = ColoredMesh_PS;
	}

	pass TexturedArray
	{
		VertexShader = TexturedVS;
		PixelShader = TexturedArray_PS;
	}

	pass PalettedArray
	{
		VertexShader = TexturedVS;
		PixelShader = PalettedArray_PS;
	}
}