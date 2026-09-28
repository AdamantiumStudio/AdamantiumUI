// SHAPE MATHS - the SDFs every instanced fill is cut from, plus slot pixel scale, arc length, curvature and AA-ring
// expansion. Include after CommonData.fxh and before StrokeMath.fxh.

// ---- Shared SDF fill+stroke compositing --------------------------------------------------------------------------
// stroke0 = (width_px, align[-1 inside/0 center/+1 outside], dashOn, dashGap); stroke1 = (dashOffset, trimStart, trimEnd, flags).

// Device pixels per unit of a slot's space, per axis. A slot may carry a non-uniform scale, so callers convert SDF inputs
// with this to keep fwidth(d) ~1 on both axes.
float2 SlotPixelScale(float4x4 nodeWorld)
{
    float4x4 m = mul(nodeWorld, Projection);
    float2 halfVp = ViewportSize * 0.5;
    float2 ax = mul(float4(1.0, 0.0, 0.0, 0.0), m).xy * halfVp;
    float2 ay = mul(float4(0.0, 1.0, 0.0, 0.0), m).xy * halfVp;
    float2 scale = max(float2(length(ax), length(ay)), float2(1e-4, 1e-4));
    return ViewportSize.x < 1.0 ? float2(1.0, 1.0) : scale;   // no viewport supplied: leave the bake untouched
}

// Approximate SIGNED DISTANCE (device px) to an ellipse boundary: the implicit F = length(p/half) - 1 normalised by the
// length of its gradient (first-order/Taylor distance). Exact for a circle (rx==ry); for rx!=ry it's the correct shape
// with sub-pixel-accurate distance near the boundary - which is exactly where fill AA and the stroke ring live.
float SdEllipse(float2 p, float2 half)
{
    float2 h = max(half, float2(1e-6, 1e-6));
    float2 nq = p / h;
    float L = max(length(nq), 1e-6);
    float2 grad = float2(nq.x / h.x, nq.y / h.y) / L;   // d(F)/d(p)
    return (L - 1.0) / max(length(grad), 1e-6);
}

// Signed distance to a regular n-gon inscribed in the `half` box, scaled by the smaller half-axis like SdEllipse. The
// first vertex is on +x, matching the tessellated Shapes.Polygon.
float SdRegularPolygon(float2 p, float2 half, float n, float startAngle)
{
    float2 h = max(half, float2(1e-6, 1e-6));
    float2 q = p / h;                       // normalised: the shape is the unit circumradius polygon

    // Turn the shape by rolling the SAMPLE the other way - and do it HERE, in normalised space, where the corners sit on
    // a unit circle. Rotating the fragment before the divide would rotate the box too, so a squashed hexagon would swing
    // out of the slot it is inscribed in; rotating after it moves the corners along the ellipse the box inscribes, which
    // is exactly what Shapes.Polygon does with the same angle (radii * cos/sin of start + 2*pi*i/N).
    float ca = cos(startAngle);
    float sa = sin(startAngle);
    q = float2(q.x * ca + q.y * sa, q.y * ca - q.x * sa);

    float an = 3.14159265 / n;              // half of one sector

    // Fold into a single half-sector, measured from the +x axis so that vertex 0 lands on it. What is left is a point
    // whose x runs along the apothem and whose y is its (positive) offset along the edge.
    float a = atan2(q.y, q.x);
    float wrapped = a - 2.0 * an * floor(a / (2.0 * an) + 0.5);   // into [-an, an], centred on a VERTEX
    float2 folded = length(q) * float2(cos(wrapped), abs(sin(wrapped)));

    // Distance to the edge running from that vertex to the next: a segment, so a point past the vertex measures to the
    // vertex itself rather than to the edge's infinite line.
    float2 v0 = float2(1.0, 0.0);
    float2 v1 = float2(cos(2.0 * an), sin(2.0 * an));
    float2 e = v1 - v0;
    float2 w = folded - v0;
    float2 d = w - e * saturate(dot(w, e) / max(dot(e, e), 1e-9));

    // Inside is the side the centre is on. cross(e, w) changes sign exactly across the edge's line.
    float side = (e.x * w.y - e.y * w.x) > 0.0 ? -1.0 : 1.0;
    return length(d) * side * min(h.x, h.y);
}

// Arc-length `s` (device px) of the point on the ROUNDED-RECT contour nearest `p`, and the perimeter. Exact/closed-form.
// Traversal CCW from the start of the top-right arc: TR arc, top edge, TL arc, left edge, BL arc, bottom edge, BR arc,
// right edge. (Start point is arbitrary for dashes; dashOffset shifts the phase.)
float RoundRectArc(float2 p, float2 b, float4 radii, out float perimeter)
{
    // Centerline arc length; in corners by angle, uniform across the width. Accumulated per corner (BR arc, bottom,
    // BL arc, left, TL arc, top, TR arc, right) since radii differ.
    float rTL = radii.x, rTR = radii.y, rBR = radii.z, rBL = radii.w;
    const float HALF_PI = 1.5707963268;
    float aBR = HALF_PI * rBR, aBL = HALF_PI * rBL, aTL = HALF_PI * rTL, aTR = HALF_PI * rTR;
    float eBottom = 2.0 * b.x - rBR - rBL;
    float eLeft   = 2.0 * b.y - rBL - rTL;
    float eTop    = 2.0 * b.x - rTL - rTR;
    float eRight  = 2.0 * b.y - rTR - rBR;
    perimeter = aBR + eBottom + aBL + eLeft + aTL + eTop + aTR + eRight;

    float sBottom = aBR;                      // where each segment STARTS along the traversal
    float sBL     = sBottom + eBottom;
    float sLeft   = sBL + aBL;
    float sTL     = sLeft + eLeft;
    float sTop    = sTL + aTL;
    float sTR     = sTop + eTop;
    float sRight  = sTR + aTR;

    float r = CornerRadiusAt(p, radii);
    float bx = b.x - r, by = b.y - r;
    float ax = abs(p.x), ay = abs(p.y);
    float cx = ax - bx, cy = ay - by;

    if (cx > 0.0 && cy > 0.0)                                   // corner -> arc-length by angle (phi), uniform + continuous
    {
        float phi = atan2(cy, cx);
        float s;
        if      (p.x >= 0.0 && p.y >= 0.0) s = phi * rBR;
        else if (p.x <  0.0 && p.y >= 0.0) s = sBL + (HALF_PI - phi) * rBL;
        else if (p.x <  0.0 && p.y <  0.0) s = sTL + phi * rTL;
        else                               s = sTR + (HALF_PI - phi) * rTR;
        return s;
    }
    // Classify by the nearest edge, since a stroke thicker than r reaches inside the inner box; an edge is anchored at
    // the corner it starts from.
    bool horizontal = (cx <= 0.0) && (cy > 0.0 || (b.y - ay) < (b.x - ax));
    float s;
    if (horizontal) s = (p.y >= 0.0) ? sBottom + ((b.x - rBR) - p.x) : sTop + (p.x + (b.x - rTL));
    else            s = (p.x >= 0.0) ? sRight + (p.y + (b.y - rTR)) : sLeft + ((b.y - rBL) - p.y);
    return s;
}

// Arc-length `s` (device px) along an ELLIPSE contour to the fragment's radial projection, and the perimeter. Perimeter
// is Ramanujan's closed form; the partial length is a short trapezoidal integral of ds/dt = sqrt(rx^2 sin^2 t + ry^2
// cos^2 t) - exact for a circle, sub-pixel for real ellipses. The loop runs only for the thin stroke ring's fragments.
float EllipseArc(float2 p, float2 h, out float perimeter)
{
    float a = max(h.x, h.y), b = min(h.x, h.y);
    float hh = ((a - b) * (a - b)) / max((a + b) * (a + b), 1e-6);
    perimeter = 3.14159265 * (a + b) * (1.0 + 3.0 * hh / (10.0 + sqrt(4.0 - 3.0 * hh)));

    float t = atan2(p.y * h.x, p.x * h.y);        // parametric angle of the radial projection
    if (t < 0.0) t += 6.28318530718;

    const int N = 16;
    float dt = t / float(N);
    float s = 0.0;
    float prev = h.y;                             // ds/dt at t=0 = ry
    for (int i = 1; i <= N; i++)
    {
        float u = dt * float(i);
        float su = sin(u), cu = cos(u);
        float cur = sqrt(h.x * h.x * su * su + h.y * h.y * cu * cu);
        s += 0.5 * (prev + cur) * dt;
        prev = cur;
    }
    return s;
}

// Same for a rounded rect: the corner arcs bend with the corner radius, the four edges are straight.
float RoundRectCurvRadius(float2 p, float2 b, float4 radii)
{
    float r = CornerRadiusAt(p, radii);
    float2 q = abs(p) - (b - r);
    return (q.x > 0.0 && q.y > 0.0 && r > 0.5) ? r : 1e9;
}

// Radius of curvature of the ellipse at the fragment's parametric angle: |r'|^3 / (rx*ry) for x = rx cos t, y = ry sin t.
float EllipseCurvRadius(float2 p, float2 h)
{
    float t = atan2(p.y * h.x, p.x * h.y);
    float st = sin(t), ct = cos(t);
    float e = sqrt(h.x * h.x * st * st + h.y * h.y * ct * ct);
    return (e * e * e) / max(h.x * h.y, 1e-6);
}

// The ring's vertex, expanded. Shared by every fringe pass (solid / pattern / gradient): they differ only in WHICH
// record supplies the matrix and the colour, and the expansion itself must stay one definition - it is the thing that
// makes the ring exactly one device pixel wide. `coverage` comes out 1 on the contour and 0 on the outer edge.
float4 ExpandFringe(FringeVertex v, float4x4 m, out float coverage)
{
    float4 clip = mul(float4(v.Position, 0.0, 1.0), m);
    float outer = dot(v.Dir0, v.Dir0) + dot(v.Dir1, v.Dir1);
    if (outer > 0.0)
    {
        // Edge directions -> PIXEL space (w = 0 drops the translation), so the miter is perpendicular to the edge as the
        // rasterizer sees it - correct under anisotropic scale, skew and rotation alike.
        float2 halfVp = max(ViewportSize, float2(1.0, 1.0)) * 0.5;
        float w = max(clip.w, 1e-6);
        float2 e0 = mul(float4(v.Dir0, 0.0, 0.0), m).xy / w * halfVp;
        float2 e1 = mul(float4(v.Dir1, 0.0, 0.0), m).xy / w * halfVp;
        e0 = length(e0) > 1e-9 ? normalize(e0) : float2(0.0, 0.0);
        e1 = length(e1) > 1e-9 ? normalize(e1) : float2(0.0, 0.0);
        float2 n0 = float2(-e0.y, e0.x);
        float2 n1 = float2(-e1.y, e1.x);
        float2 sum = n0 + n1;
        float2 miter = length(sum) > 1e-4 ? normalize(sum) : n0;   // a 180-degree reversal has no bisector: use one normal
        float denom = max(dot(miter, n0), 0.25);                   // clamp the corner spike to <= 4x
        clip.xy += miter * (FringePixels / denom) / halfVp * w;
    }
    coverage = outer > 0.0 ? 0.0 : 1.0;
    return clip;
}

