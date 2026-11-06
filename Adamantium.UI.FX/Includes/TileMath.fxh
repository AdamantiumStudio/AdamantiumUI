// Where a picture is sampled, by ImageTiling's layout: shared by every pass that maps a TileBrush across a box.

// `t` is the point 0..1 across the box. Back into the UNTURNED grid (one 2x2, with the inverse, the aspect and the turn
// center already folded in) -> TILE space -> the content's rect inside one tile -> the source's sub-rectangle. `repeat`
// is per axis: 1 repeats the tile, 0 keeps a single copy, which must never wrap - frac() would send its far edge (n = 1)
// back to the opposite one. Mirror flags: 1 = X, 2 = Y, 3 = both - every other copy runs backwards, so a picture never
// drawn to tile still meets its own reflection at the seam. `inside` is 0 where the content does not reach: the gap a
// Uniform fit leaves round every tile, and all of the box a single copy does not cover.
float2 TileSourceUv(float2 t, float4 rotation, float4 tile, float4 drawn, float4 uvRect, float2 repeat, float mirror,
    out float inside)
{
    float2 g = float2(t.x * rotation.x + t.y * rotation.y, t.x * rotation.z + t.y * rotation.w);
    float2 nn = g * tile.xy - tile.zw;
    float2 tileLocal = lerp(nn, frac(nn), repeat);
    float2 mirrored = abs(frac(nn * 0.5) * 2.0 - 1.0);
    float2 pick = float2(step(0.5, fmod(mirror, 2.0)), step(0.5, floor(mirror * 0.5)));
    float2 inTile = lerp(tileLocal, mirrored, pick);
    float2 n = (inTile - drawn.xy) / max(drawn.zw, float2(1e-4, 1e-4));
    inside = step(0.0, n.x) * step(n.x, 1.0) * step(0.0, n.y) * step(n.y, 1.0);
    return uvRect.xy + saturate(n) * uvRect.zw;
}
