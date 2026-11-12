// QUADRATIC BEZIER MATHS - a piece from start through control to end: where it is, which way it runs, how long it is
// and which of its points is nearest a given one. A point on it at parameter t, 0 at the start and 1 at the end, is
// start + 2t (control - start) + t^2 (start - 2 control + end).

float2 QuadAt(float2 start, float2 control, float2 end, float parameter)
{
    float rest = 1.0 - parameter;
    return start * (rest * rest) + control * (2.0 * rest * parameter) + end * (parameter * parameter);
}

// Its velocity at parameter, halved: (start - 2 control + end) t + (control - start).
float2 QuadHalfVelocity(float2 start, float2 control, float2 end, float parameter)
{
    return (start - 2.0 * control + end) * parameter + (control - start);
}

// Which way it runs at parameter; where it stands still - a control on an end - the way from the start to the end.
float2 QuadDirection(float2 start, float2 control, float2 end, float parameter)
{
    float2 velocity = QuadHalfVelocity(start, control, end, parameter);
    float speed = length(velocity);
    if (speed > 1e-12)
    {
        return velocity / speed;
    }

    float2 chord = end - start;
    float chordLength = length(chord);
    return chordLength > 1e-12 ? chord / chordLength : float2(1.0, 0.0);
}

// Its length from the start to parameter: five-point Gauss-Legendre over its speed - the rule the CPU measures the
// stroke's arcs by (QuadraticCurve.LengthTo), so the two agree where a piece ends.
float QuadLength(float2 start, float2 control, float2 end, float parameter)
{
    const float nodes[5] = { -0.9061798459386640, -0.5384693101056831, 0.0, 0.5384693101056831, 0.9061798459386640 };
    const float weights[5] = { 0.2369268850561891, 0.4786286704993665, 0.5688888888888889, 0.4786286704993665,
                               0.2369268850561891 };
    float sum = 0.0;
    [unroll]
    for (int node = 0; node < 5; node++)
    {
        float atNode = parameter * 0.5 * (1.0 + nodes[node]);
        sum += weights[node] * 2.0 * length(QuadHalfVelocity(start, control, end, atNode));
    }

    return sum * parameter * 0.5;
}

// Where along it its length from the start reaches distance - its whole length known as total - by Newton's steps from
// the even guess. Inside 0..1; the caller extends past the ends.
float QuadParameterAt(float2 start, float2 control, float2 end, float distance, float total)
{
    float parameter = total > 1e-12 ? saturate(distance / total) : 0.0;
    [unroll]
    for (int iteration = 0; iteration < 4; iteration++)
    {
        float speed = 2.0 * length(QuadHalfVelocity(start, control, end, parameter));
        if (!(speed > 1e-12))
        {
            break;
        }

        parameter = saturate(parameter - (QuadLength(start, control, end, parameter) - distance) / speed);
    }

    return parameter;
}

// The real roots of t^3 + squared t^2 + linear t + constant in 0..1 - one or three - in roots, how many in the count
// returned: Cardano's formula for one real root, the trigonometric form for three, on the cubic shifted to lose its
// square term.
uint CubicRootsInUnit(float squaredTerm, float linearTerm, float constantTerm, out float roots[3])
{
    roots[0] = 0.0;
    roots[1] = 0.0;
    roots[2] = 0.0;
    float shift = squaredTerm / 3.0;
    float shiftedLinear = linearTerm - squaredTerm * shift;
    float shiftedConstant = 2.0 * shift * shift * shift - shift * linearTerm + constantTerm;
    float discriminant = shiftedConstant * shiftedConstant * 0.25 + shiftedLinear * shiftedLinear * shiftedLinear / 27.0;
    if (discriminant >= 0.0)
    {
        float root = sqrt(discriminant);
        float upper = -shiftedConstant * 0.5 + root;
        float lower = -shiftedConstant * 0.5 - root;
        float shiftedRoot = sign(upper) * pow(abs(upper), 1.0 / 3.0) + sign(lower) * pow(abs(lower), 1.0 / 3.0);
        roots[0] = saturate(shiftedRoot - shift);
        return 1u;
    }

    float amplitude = 2.0 * sqrt(-shiftedLinear / 3.0);
    float angle = acos(clamp(3.0 * shiftedConstant / (shiftedLinear * amplitude), -1.0, 1.0)) / 3.0;
    [unroll]
    for (int root = 0; root < 3; root++)
    {
        roots[root] = saturate(amplitude * cos(angle - 2.0943951023931957 * float(root)) - shift);
    }

    return 3u;
}

// The piece's parameter nearest target: where the way from target to the piece is square to the piece's velocity, or
// an end. That is (bend t^2 + 2 lean t + offset) . (bend t + lean) = 0, a cubic in t; near enough straight for the cubic
// to lose its digits, the straight piece's answer. Each root is refined by two Newton steps on
// (B(t) - target) . B'(t) / 2, whose derivative is 2 |B'/2|^2 + (B(t) - target) . bend.
float QuadNearest(float2 start, float2 control, float2 end, float2 target)
{
    float2 bend = start - 2.0 * control + end;
    float2 lean = control - start;
    float2 offset = start - target;
    float cubicTerm = dot(bend, bend);
    float squaredTerm = 3.0 * dot(bend, lean);
    float linearTerm = 2.0 * dot(lean, lean) + dot(bend, offset);
    float constantTerm = dot(lean, offset);

    float best = 0.0;
    float bestDistance = dot(offset, offset);
    float2 toEnd = end - target;
    if (dot(toEnd, toEnd) < bestDistance)
    {
        best = 1.0;
        bestDistance = dot(toEnd, toEnd);
    }

    float roots[3];
    uint count;
    if (cubicTerm > 1e-6 * max(dot(lean, lean), 1e-30))
    {
        count = CubicRootsInUnit(squaredTerm / cubicTerm, linearTerm / cubicTerm, constantTerm / cubicTerm, roots);
    }
    else
    {
        roots[0] = abs(linearTerm) > 1e-30 ? saturate(-constantTerm / linearTerm) : 0.0;
        roots[1] = 0.0;
        roots[2] = 0.0;
        count = 1u;
    }

    for (uint candidate = 0u; candidate < count; candidate++)
    {
        float parameter = roots[candidate];
        [unroll]
        for (int iteration = 0; iteration < 2; iteration++)
        {
            float2 velocity = bend * parameter + lean;
            float2 away = QuadAt(start, control, end, parameter) - target;
            float slope = 2.0 * dot(velocity, velocity) + dot(away, bend);
            if (abs(slope) > 1e-30)
            {
                parameter = saturate(parameter - dot(away, velocity) / slope);
            }
        }

        float2 away = QuadAt(start, control, end, parameter) - target;
        float distance = dot(away, away);
        if (distance < bestDistance)
        {
            best = parameter;
            bestDistance = distance;
        }
    }

    return best;
}

// How far across the piece target is - signed, positive on the left of its way, (-direction.y, direction.x) - and how
// far along it from its start, startDistance added. At an end the target may lie beyond it: both are then read off the
// line its end runs along, as a straight piece's are.
float2 QuadAcrossAlong(float2 target, float2 start, float2 control, float2 end, float startDistance)
{
    float parameter = QuadNearest(start, control, end, target);
    float2 nearest = QuadAt(start, control, end, parameter);
    float2 direction = QuadDirection(start, control, end, parameter);
    float2 away = target - nearest;
    float across = dot(away, float2(-direction.y, direction.x));
    float along = startDistance + QuadLength(start, control, end, parameter);
    if (parameter <= 0.0 || parameter >= 1.0)
    {
        along += dot(away, direction);
    }

    return float2(across, along);
}
