// ---- ROUNDED CLIPPING -------------------------------------------------------------------------------------------
// The clip lives in a transform-table slot (row 0 rect in device px, row 1 radii), read in the vertex stage via the
// includer's NodeSlot/TransformsAddress and applied as antialiased coverage.

// The radius of the corner this point belongs to (x TL, y TR, z BR, w BL, as CornerRadius; y is down). Every rounded-rect
// helper uses it, keeping the corners independent.
float CornerRadiusAt(float2 p, float4 radii)
{
    return p.x < 0.0 ? (p.y < 0.0 ? radii.x : radii.w)
                     : (p.y < 0.0 ? radii.y : radii.z);
}

// Signed distance (device px) to a rounded rect with a selectable outer-corner JOIN: 0 = miter (sharp, Chebyshev outer
// corner), 1 = bevel (45-deg chamfer), 2 = round (Euclidean, the natural offset). Straight edges are identical across all
// three; only the corner (both q>0) differs. This is what gives stroke-join parity with the pen (PenLineJoin).
float SdRoundRectJoin(float2 p, float2 b, float4 radii, int joinType)
{
    float r = CornerRadiusAt(p, radii);
    float2 q = abs(p) - b + r;
    float inside = min(max(q.x, q.y), 0.0);
    float2 qp = max(q, float2(0.0, 0.0));
    // A ROUNDED geometry (r>0) already curves the corner - the join is moot and applying miter/bevel would (wrongly)
    // reshape the FILL, so only a (near-)sharp corner honors the join. Round join is always the plain Euclidean offset.
    float outside = (r > 0.5 || joinType == 2) ? length(qp)
                  : (joinType == 1) ? (qp.x + qp.y)                   // bevel: L1 - a 45-deg chamfer at the corner, but a
                                                                     // straight edge (one component 0) stays exact
                  : max(qp.x, qp.y);                                  // miter (sharp)
    return inside + outside - r;
}

// Box: xy = the clip rect's origin in DEVICE pixels, zw = its size. zw = 0 means THERE IS NO CLIP.
float4 ClipShapeBox(float slotIndex)
{
    if (slotIndex < 0.0) return float4(0.0, 0.0, 0.0, 0.0);

    NodeSlot* nodes = (NodeSlot*)TransformsAddress;
    NodeSlot clip = nodes[(uint)slotIndex];
    if (clip.Params.x < 0.5) return float4(0.0, 0.0, 0.0, 0.0);

    return clip.World[0];
}

/// The four corner radii of that same clip, in device pixels.
float4 ClipShapeRadii(float slotIndex)
{
    if (slotIndex < 0.0) return float4(0.0, 0.0, 0.0, 0.0);

    NodeSlot* nodes = (NodeSlot*)TransformsAddress;
    NodeSlot clip = nodes[(uint)slotIndex];
    if (clip.Params.x < 0.5) return float4(0.0, 0.0, 0.0, 0.0);

    return clip.World[1];
}

// Both halves of the clip shape in ONE table read, for a vertex stage that hands them to its pixel shader.
void ClipShapeBoxAndRadii(float slotIndex, out float4 box, out float4 radii)
{
    box = float4(0.0, 0.0, 0.0, 0.0);
    radii = float4(0.0, 0.0, 0.0, 0.0);
    if (slotIndex < 0.0) return;

    NodeSlot* nodes = (NodeSlot*)TransformsAddress;
    NodeSlot clip = nodes[(uint)slotIndex];
    if (clip.Params.x < 0.5) return;

    box = clip.World[0];
    radii = clip.World[1];
}

// Clip coverage from a single table read in the fragment stage, for passes that do not fetch the clip in their vertex
// stage (text).
float ClipCoverageBySlot(float2 fragment, float slotIndex)
{
    if (slotIndex < 0.0) return 1.0;

    NodeSlot* nodes = (NodeSlot*)TransformsAddress;
    NodeSlot clip = nodes[(uint)slotIndex];
    if (clip.Params.x < 0.5) return 1.0;

    float2 halfSize = max(clip.World[0].zw * 0.5, float2(1.0, 1.0));
    float2 local = fragment - (clip.World[0].xy + halfSize);
    float lim = min(halfSize.x, halfSize.y);
    float4 r4 = min(clip.World[1], float4(lim, lim, lim, lim));

    float d = SdRoundRectJoin(local, halfSize, r4, 2);
    float aa = fwidth(d) + 1e-4;
    return 1.0 - smoothstep(-aa, aa, d);
}

// The fragment's coverage under that shape - no buffer access, so any pass can call it.
float ClipCoverage(float2 fragment, float4 box, float4 radii)
{
    if (box.z <= 0.0) return 1.0;   // no clip

    float2 halfSize = max(box.zw * 0.5, float2(1.0, 1.0));
    float2 local = fragment - (box.xy + halfSize);
    float lim = min(halfSize.x, halfSize.y);
    float4 r4 = min(radii, float4(lim, lim, lim, lim));

    float d = SdRoundRectJoin(local, halfSize, r4, 2);
    float aa = fwidth(d) + 1e-4;
    return 1.0 - smoothstep(-aa, aa, d);
}
