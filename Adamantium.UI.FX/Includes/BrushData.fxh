// Maths used by BrushEffect alone: per-instance shape distance, shape numbers to device pixels, the uncapped dash mask.
// Include last, after CommonData, ShapeMath and StrokeMath.

// Shape numbers to device pixels: a rect's four radii all scale; a polygon's field holds corner count, start angle and
// ring thickness, and only the ring scales.
float4 ScaleShapeNumbers(float4 radii, float iso, float isPolygon)
{
    return lerp(radii * iso, float4(radii.x, radii.y, radii.z * iso, radii.w), isPolygon);
}

// The shape a brush pass paints on, stated once for the gradient, pattern and texture passes: `shape` is 0 rect,
// 1 ellipse, 2 polygon.
float BrushShapeDistance(float2 p, float2 half, float4 radii, int joinType, float shape)
{
    // Branch-free: all three distances are computed and selected with step/lerp.
    float dPoly = SdRegularPolygon(p, half, max(radii.x, 3.0), radii.y);
    dPoly = lerp(dPoly, max(dPoly, -(dPoly + radii.z)), step(0.0001, radii.z));   // a RING, exactly as in pass Polygon

    float dRect = SdRoundRectJoin(p, half, radii, joinType);
    float dEllipse = SdEllipse(p, half);
    return lerp(lerp(dRect, dEllipse, step(0.5, shape)), dPoly, step(1.5, shape));
}

float DashTrimMask(float sTrim, float sDash, float perimeter, float dashOn, float dashGap, float dashOffset,
    float trimStart, float trimEnd, float dPerp, float halfW, float capFlags, float4 dashRest)
{
    int dashStartCap = int(fmod(capFlags, 8.0));
    int dashEndCap   = int(fmod(floor(capFlags / 8.0), 8.0));
    int startCap     = int(fmod(floor(capFlags / 64.0), 8.0));
    int endCap       = int(fmod(floor(capFlags / 512.0), 8.0));   // base-8 mask: else the JOIN above them leaks in

    // Distance to the trim window's two ends. Untrimmed = "nowhere near", so the dash edges are the only ends there is.
    float tS = 1e9;
    float tE = 1e9;
    if (trimStart > 0.0 || trimEnd < 1.0)
    {
        tS = sTrim - trimStart * perimeter;
        tE = trimEnd * perimeter - sTrim;
    }

    // Distance to the two ends of the dash run this fragment belongs to. Inside a run it is that run; inside a gap it is
    // whichever neighboring run is nearer, so a convex cap still bulges out into the gap it faces.
    float dS = 1e9;
    float dE = 1e9;
    int dashCount = int(floor(capFlags / 32768.0));   // how many runs the pattern has; 2 is the plain ON/GAP
    float period = DashPatternLength(dashOn, dashGap, dashRest, dashCount);
    if (dashOn > 0.0 && period > 0.0)
    {
        float ph = frac((sDash + dashOffset) / period) * period;   // 0..period
        float2 de = DashPiece(ph, dashOn, dashGap, dashRest, dashCount);
        dS = de.x;
        dE = de.y;
    }

    float sdStart = (dS < tS) ? dS + CapReach(dashStartCap, dPerp, halfW)
                              : tS + CapReach(startCap, dPerp, halfW);
    float sdEnd   = (dE < tE) ? dE + CapReach(dashEndCap, dPerp, halfW)
                              : tE + CapReach(endCap, dPerp, halfW);
    return saturate(min(sdStart, sdEnd) + 0.5);
}

