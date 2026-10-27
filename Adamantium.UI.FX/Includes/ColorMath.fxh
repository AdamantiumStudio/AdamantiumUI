// Brightness, contrast and saturation of a sampled color, zero for each leaving it as it is: .x brightness added, .y
// contrast - 1 around the middle gray, .z saturation - 1 away from the color's own gray. One statement for every pass
// that draws a picture, so a picture drawn by the batch and by the per-unit fallback look the same.
float3 AdjustColor(float3 color, float4 adjust)
{
    float gray = dot(color, float3(0.2126, 0.7152, 0.0722));
    float3 c = lerp(float3(gray, gray, gray), color, 1.0 + adjust.z);
    c = (c - 0.5) * (1.0 + adjust.y) + 0.5;
    return saturate(c + adjust.x);
}
