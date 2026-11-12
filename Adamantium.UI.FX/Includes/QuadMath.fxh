// QUADRATIC BEZIER MATHS - a piece from s through control c to e: where it is, which way it runs, how long it is and
// which of its points is nearest a given one. B(t) = s + 2t(c - s) + t^2(s - 2c + e).

float2 QuadAt(float2 s, float2 c, float2 e, float t)
{
    float rest = 1.0 - t;
    return s * (rest * rest) + c * (2.0 * rest * t) + e * (t * t);
}

// Its velocity at t, halved: (s - 2c + e) t + (c - s).
float2 QuadHalfVelocity(float2 s, float2 c, float2 e, float t)
{
    return (s - 2.0 * c + e) * t + (c - s);
}

// Which way it runs at t; where it stands still - a control on an end - the way from the start to the end.
float2 QuadDirection(float2 s, float2 c, float2 e, float t)
{
    float2 v = QuadHalfVelocity(s, c, e, t);
    float l = length(v);
    if (l > 1e-12)
    {
        return v / l;
    }

    float2 chord = e - s;
    float lc = length(chord);
    return lc > 1e-12 ? chord / lc : float2(1.0, 0.0);
}

// Its length from the start to t: five-point Gauss-Legendre over its speed - the rule the CPU measures the stroke's
// arcs by (QuadraticCurve.LengthTo), so the two agree where a piece ends.
float QuadLength(float2 s, float2 c, float2 e, float t)
{
    const float nodes[5] = { -0.9061798459386640, -0.5384693101056831, 0.0, 0.5384693101056831, 0.9061798459386640 };
    const float weights[5] = { 0.2369268850561891, 0.4786286704993665, 0.5688888888888889, 0.4786286704993665,
                               0.2369268850561891 };
    float sum = 0.0;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float u = t * 0.5 * (1.0 + nodes[i]);
        sum += weights[i] * 2.0 * length(QuadHalfVelocity(s, c, e, u));
    }

    return sum * t * 0.5;
}

// Where along it its length from the start reaches `along` - its whole length known as `total` - by Newton's steps from
// the even guess. Inside 0..1; the caller extends past the ends.
float QuadParameterAt(float2 s, float2 c, float2 e, float along, float total)
{
    float t = total > 1e-12 ? saturate(along / total) : 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float speed = 2.0 * length(QuadHalfVelocity(s, c, e, t));
        if (!(speed > 1e-12))
        {
            break;
        }

        t = saturate(t - (QuadLength(s, c, e, t) - along) / speed);
    }

    return t;
}

// The real roots of t^3 + a t^2 + b t + d in 0..1 - one or three - in roots, how many in the count returned. Cardano for
// one real root, the trigonometric form for three.
uint CubicRootsInUnit(float a, float b, float d, out float roots[3])
{
    roots[0] = 0.0;
    roots[1] = 0.0;
    roots[2] = 0.0;
    float shift = a / 3.0;
    float p = b - a * shift;
    float q = 2.0 * shift * shift * shift - shift * b + d;
    float h = q * q * 0.25 + p * p * p / 27.0;
    uint count = 0u;
    if (h >= 0.0)
    {
        float root = sqrt(h);
        float u = -q * 0.5 + root;
        float v = -q * 0.5 - root;
        float x = sign(u) * pow(abs(u), 1.0 / 3.0) + sign(v) * pow(abs(v), 1.0 / 3.0);
        roots[0] = saturate(x - shift);
        return 1u;
    }

    float m = 2.0 * sqrt(-p / 3.0);
    float angle = acos(clamp(3.0 * q / (p * m), -1.0, 1.0)) / 3.0;
    [unroll]
    for (int k = 0; k < 3; k++)
    {
        roots[k] = saturate(m * cos(angle - 2.0943951023931957 * float(k)) - shift);
    }

    return 3u;
}

// The piece's parameter nearest p: where (B(t) - p) is square to the piece's velocity, or an end.
float QuadNearest(float2 s, float2 c, float2 e, float2 p)
{
    float2 bend = s - 2.0 * c + e;
    float2 lean = c - s;
    float2 off = s - p;
    // (bend t^2 + 2 lean t + off) . (bend t + lean) = 0
    float k3 = dot(bend, bend);
    float k2 = 3.0 * dot(bend, lean);
    float k1 = 2.0 * dot(lean, lean) + dot(bend, off);
    float k0 = dot(lean, off);

    float best = 0.0;
    float bestDistance = dot(off, off);
    float2 toEnd = e - p;
    if (dot(toEnd, toEnd) < bestDistance)
    {
        best = 1.0;
        bestDistance = dot(toEnd, toEnd);
    }

    float roots[3];
    uint count;
    if (k3 > 1e-6 * max(dot(lean, lean), 1e-30))
    {
        count = CubicRootsInUnit(k2 / k3, k1 / k3, k0 / k3, roots);
    }
    else
    {
        // Near enough straight for the cubic to lose its digits: the straight piece's answer, refined below.
        roots[0] = abs(k1) > 1e-30 ? saturate(-k0 / k1) : 0.0;
        roots[1] = 0.0;
        roots[2] = 0.0;
        count = 1u;
    }

    for (uint i = 0u; i < count; i++)
    {
        // Two Newton steps on (B(t) - p) . B'(t) / 2, whose derivative is 2|B'/2|^2 + (B(t) - p) . bend.
        float t = roots[i];
        [unroll]
        for (int n = 0; n < 2; n++)
        {
            float2 v = bend * t + lean;
            float2 r = QuadAt(s, c, e, t) - p;
            float slope = 2.0 * dot(v, v) + dot(r, bend);
            if (abs(slope) > 1e-30)
            {
                t = saturate(t - dot(r, v) / slope);
            }
        }

        float2 r = QuadAt(s, c, e, t) - p;
        float distance = dot(r, r);
        if (distance < bestDistance)
        {
            best = t;
            bestDistance = distance;
        }
    }

    return best;
}

// How far across the piece p is - signed, positive on the left of its way, (-dir.y, dir.x) - and how far along it from
// its start, `start` added; past an end both are read off the line its end runs along, as a straight piece's are.
float2 QuadAcrossAlong(float2 p, float2 s, float2 c, float2 e, float start)
{
    float t = QuadNearest(s, c, e, p);
    float2 at = QuadAt(s, c, e, t);
    float2 dir = QuadDirection(s, c, e, t);
    float2 r = p - at;
    float across = dot(r, float2(-dir.y, dir.x));
    float along = start + QuadLength(s, c, e, t);
    // At an end the point may lie beyond it: count how far, along the end's way.
    if (t <= 0.0 || t >= 1.0)
    {
        along += dot(r, dir);
    }

    return float2(across, along);
}
