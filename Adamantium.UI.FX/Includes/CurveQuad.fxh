// THE QUAD A CURVE FILL DRAWS - one per instance over the fill's box (CurveMath.fxh gives each fragment its coverage).
// Include after ShapeMath.fxh and CurveMath.fxh.

// One corner of the quad an instance draws, in the fill's local space - the space its mesh's vertices are in: the
// pieces' box, a pixel past it on every side so its edge's soft half lands inside. toSlot takes that space to the slot's.
float2 CurveQuadCorner(uint vertexId, float4x4 toSlot)
{
    float2 pixelsPerUnit = SlotPixelScale(toSlot);
    float2 corner = float2(vertexId & 1u, (vertexId >> 1u) & 1u);
    return CurvePoint(0u) + corner * CurvePoint(2u) + (corner * 2.0 - 1.0) / pixelsPerUnit;
}
