// CURVE FILL MATHS - a fill drawn from its curves rather than from a mesh. One quad per instance over the fill's box;
// each fragment counts how its curves wind round it, along a ray to +x and a ray to +y, each crossing softened over the
// pixel it falls in - exact at any zoom, with no mesh and no fringe. The curves are quadratic pieces running one way in
// x and in y. A fill takes its coverage from its own tiles (CurveTiles.fxh); the walk over every piece here is what the
// tiles are checked against. Every brush that fills a shape this way takes its coverage from these and its color from
// its own maths. Include after ShapeMath.fxh.

static const uint CurveHeaderWords = 8u;
static const uint CurvePieceWords = 6u;

// The packed curves of the fill the draw is of - see Retained/CurveFill.cs for the layout.
uint* CurveData;

float CurveWord(uint index)
{
    uint* words = CurveData;
    return asfloat(words[index]);
}

float2 CurvePoint(uint index)
{
    return float2(CurveWord(index), CurveWord(index + 1u));
}

// One corner of the quad an instance draws, in the fill's local space - the space its mesh's vertices are in: the
// pieces' box, a pixel past it on every side so its edge's soft half lands inside. toSlot takes that space to the slot's.
float2 CurveQuadCorner(uint vertexId, float4x4 toSlot)
{
    float2 pixelsPerUnit = SlotPixelScale(toSlot);
    float2 corner = float2(vertexId & 1u, (vertexId >> 1u) & 1u);
    return CurvePoint(0u) + corner * CurvePoint(2u) + (corner * 2.0 - 1.0) / pixelsPerUnit;
}

// Where along a piece running one way its coordinate - given by its start, control and end - reaches value.
float CurveCrossing(float start, float control, float end, float value)
{
    float quadraticTerm = start - 2.0 * control + end;
    float linearTerm = 2.0 * (control - start);
    float constantTerm = start - value;
    float along;
    if (abs(quadraticTerm) < 1e-6 * (abs(start) + abs(control) + abs(end) + 1.0))
    {
        along = abs(linearTerm) > 1e-20 ? -constantTerm / linearTerm : 0.0;
    }
    else
    {
        float root = sqrt(max(linearTerm * linearTerm - 4.0 * quadraticTerm * constantTerm, 0.0));
        float first = (-linearTerm + root) / (2.0 * quadraticTerm);
        float second = (-linearTerm - root) / (2.0 * quadraticTerm);
        along = (first >= -1e-4 && first <= 1.0001) ? first : second;
    }
    return saturate(along);
}

// One coordinate of a piece - given by its start, control and end - at along, from 0 at the start to 1 at the end.
float CurveAt(float start, float control, float end, float along)
{
    float rest = 1.0 - along;
    return start * rest * rest + control * 2.0 * rest * along + end * along * along;
}

// The winding along one ray from position - relative to the pieces' box corner - each crossing counted by how much of
// the pixel lies before it, and how near the nearest crossing is in pixels. A piece ending half a pixel short of the
// ray's start does not count. axis 0: the ray runs to +x, where a piece going down winds one way; axis 1: to +y, where a
// piece going left winds the same way round.
float2 CurveRay(float2 position, float pixelsPerUnit, uint axis)
{
    uint* words = CurveData;
    uint pieceCount = words[4];
    float alongRay = axis == 0u ? position.x : position.y;
    float acrossRay = axis == 0u ? position.y : position.x;

    float winding = 0.0;
    float nearest = 1e20;
    for (uint piece = 0u; piece < pieceCount; piece++)
    {
        uint at = CurveHeaderWords + piece * CurvePieceWords;
        float2 start = CurvePoint(at);
        float2 control = CurvePoint(at + 2u);
        float2 end = CurvePoint(at + 4u);
        float startAlong = axis == 0u ? start.x : start.y;
        float controlAlong = axis == 0u ? control.x : control.y;
        float endAlong = axis == 0u ? end.x : end.y;
        if ((max(startAlong, endAlong) - alongRay) * pixelsPerUnit < -0.5)
        {
            continue;
        }

        float startAcross = axis == 0u ? start.y : start.x;
        float controlAcross = axis == 0u ? control.y : control.x;
        float endAcross = axis == 0u ? end.y : end.x;
        if (acrossRay < min(startAcross, endAcross) || acrossRay >= max(startAcross, endAcross))
        {
            continue;
        }

        float along = CurveCrossing(startAcross, controlAcross, endAcross, acrossRay);
        float hit = CurveAt(startAlong, controlAlong, endAlong, along);
        float distance = (hit - alongRay) * pixelsPerUnit;
        float turn = axis == 0u ? (endAcross > startAcross ? 1.0 : -1.0) : (endAcross < startAcross ? 1.0 : -1.0);
        winding += turn * saturate(distance + 0.5);
        nearest = min(nearest, abs(distance));
    }

    return float2(winding, nearest);
}

float CurveWindingCoverage(float winding, uint evenOdd)
{
    float turns = abs(winding);
    return evenOdd != 0u ? 1.0 - abs(1.0 - fmod(turns, 2.0)) : saturate(turns);
}

// How much of the pixel at local - in the fill's local space, as CurveQuadCorner gives it - the fill covers, 0..1.
// Takes derivatives, so it is called where every fragment of the quad still runs: before any discard. Each ray softens
// only the edges it crosses; the one with a crossing nearer the pixel speaks for it, and far from every edge both agree.
float CurveFillCoverage(float2 local)
{
    uint* words = CurveData;
    float2 position = local - CurvePoint(0u);
    float2 acrossScreenX = ddx(position);
    float2 acrossScreenY = ddy(position);
    float pixelsPerUnitX = 1.0 / max(length(float2(acrossScreenX.x, acrossScreenY.x)), 1e-20);
    float pixelsPerUnitY = 1.0 / max(length(float2(acrossScreenX.y, acrossScreenY.y)), 1e-20);

    float2 rayX = CurveRay(position, pixelsPerUnitX, 0u);
    float2 rayY = CurveRay(position, pixelsPerUnitY, 1u);
    uint evenOdd = words[5];
    float coverX = CurveWindingCoverage(rayX.x, evenOdd);
    float coverY = CurveWindingCoverage(rayY.x, evenOdd);
    float weightX = saturate(1.0 - rayX.y);
    float weightY = saturate(1.0 - rayY.y);
    float weights = weightX + weightY;
    return weights > 1e-4 ? (coverX * weightX + coverY * weightY) / weights : coverX;
}
