#include "Includes/CommonData.fxh"

float4x4 wvp;
float4x4 world;

float4 fillColor;
sampler sampleType;
Texture2D shaderTexture;

// An animation's frames are layers of one texture; the layer to show arrives as an effect constant, one frame per draw.
Texture2DArray shaderTextureArray;
float textureLayer;

// A PALETTED animation: each layer holds one byte per pixel, an index into a 256x1 palette shared by every frame.
Texture2DArray paletteIndices;
Texture2D palette;

float zNear;
float zFar;
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
float texRepeat;      // 1 = the tile repeats; 0 = a single copy, which must never wrap
float texMirror;      // mirror flags: 1 = X, 2 = Y, 3 = both

VERTEX_OUTPUT UIVertexShader(UI_VERTEX input)
{
    VERTEX_OUTPUT output;
    output.position = float4(input.position.xyz, 1);
    output.position = mul(output.position, wvp);
	output.normal = normalize(mul(float4(input.normal, 0.0), world).xyz);
	output.uv0 = input.uv0;
	output.uv1 = input.uv1;

	return output;
}

float4 SolidColor_PS(VERTEX_OUTPUT input) : SV_TARGET
{
	float4 result = fillColor;
    result.a *= opacity;   // combine the color's own alpha with the element/brush opacity (don't drop fillColor.a)
    return result;
}

float4 Textured_PS(VERTEX_OUTPUT input) : SV_TARGET
{
   //float4 color = shaderTexture.Sample(sampleType, input.uv0) * fillColor;
   float4 color = shaderTexture.Sample(sampleType, input.uv0);

   // Apply the control's opacity so a textured element (image / RenderTargetPanel) honors Opacity, like SolidColor_PS.
   color.a *= opacity;

   return color;
}

// The shape's own box, not the mesh's uv0: a tessellated outline has no mapping baked into it.
VERTEX_OUTPUT TexturedFillVS(UI_VERTEX input)
{
    VERTEX_OUTPUT output;
    output.position = mul(float4(input.position.xyz, 1), wvp);
    output.normal = normalize(mul(float4(input.normal, 0.0), world).xyz);
    output.uv0 = (input.position.xy - fillBounds.xy) / max(fillBounds.zw, float2(1e-4, 1e-4));
    output.uv1 = input.uv1;

    return output;
}

float4 TexturedFill_PS(VERTEX_OUTPUT input) : SV_TARGET
{
    // 0..1 across the shape's box -> TILE space -> the content's rect inside one tile -> the source's sub-rectangle.
    // Back into the UNTURNED grid: one 2x2, with the inverse, the aspect and the turn center already folded in.
    float2 g = float2(input.uv0.x * texRotation.x + input.uv0.y * texRotation.y,
                      input.uv0.x * texRotation.z + input.uv0.y * texRotation.w);
    float2 nn = g * texTile.xy - texTile.zw;
    // A SINGLE copy never wraps: frac() would send its far edge back to the opposite one.
    float2 tileLocal = lerp(nn, frac(nn), texRepeat);
    // MIRRORED repeat: every other copy runs backwards, so a picture never drawn to tile still meets its own reflection.
    float2 mirrored = abs(frac(nn * 0.5) * 2.0 - 1.0);
    float2 pick = float2(step(0.5, fmod(texMirror, 2.0)), step(0.5, floor(texMirror * 0.5)));
    float2 inTile = lerp(tileLocal, mirrored, pick);
    float2 n = (inTile - texDrawn.xy) / max(texDrawn.zw, float2(1e-4, 1e-4));
    float2 uv = texUvRect.xy + saturate(n) * texUvRect.zw;

    // SampleLevel, not Sample: frac() makes uv discontinuous at every tile seam, and the derivative Sample picks its mip
    // by spikes there - one column of pixels drawn from the smallest mip, i.e. a thin line down each seam.
    float4 color = shaderTexture.SampleLevel(sampleType, uv, 0.0) * texTint;

    // Outside the content's rect inside its tile there is nothing to paint - the gap a Uniform fit leaves.
    float inside = step(0.0, n.x) * step(n.x, 1.0) * step(0.0, n.y) * step(n.y, 1.0);
    color.a *= inside;
    color.a *= opacity;

    return color;
}

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
		VertexShader = UIVertexShader;
		PixelShader = Textured_PS;
	}

	pass TexturedFill
	{
		VertexShader = TexturedFillVS;
		PixelShader = TexturedFill_PS;
	}

	pass TexturedArray
	{
		VertexShader = UIVertexShader;
		PixelShader = TexturedArray_PS;
	}

	pass PalettedArray
	{
		VertexShader = UIVertexShader;
		PixelShader = PalettedArray_PS;
	}
}