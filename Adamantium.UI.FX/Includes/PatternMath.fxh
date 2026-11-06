// PatternBrush / NoiseBrush color at a point: every pattern and noise kind, shared by the brush batch's carriers and the
// stroke. Include NoiseMath.fxh before it, and declare `float Time` (the shared flow clock animated noise reads).

// --- Alternative base noise for NoiseBrush.NoiseType: texture-free, returning ~[-1,1] like SimplexNoise. The hashes
// live in NoiseMath.fxh. ---

// Value noise: bilinearly interpolate a random value per lattice point (smoothstep fade). Blockier than gradient noise.
float ValueNoise(float2 v)
{
    float2 i = floor(v);
    float2 f = frac(v);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = Hash21(i);
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));
    return (lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y)) * 2.0 - 1.0;
}

// Classic Perlin gradient noise: a random unit gradient per lattice point (angle from the now well-distributed Hash21, so
// no column seam), dotted with the offset and interpolated. Smooth like simplex. The angle stays in [0,2pi], so no sin of
// large arguments.
float PerlinNoise(float2 v)
{
    float2 i = floor(v);
    float2 f = frac(v);
    float2 u = f * f * (3.0 - 2.0 * f);
    float g0 = Hash21(i) * 6.2831853;
    float g1 = Hash21(i + float2(1.0, 0.0)) * 6.2831853;
    float g2 = Hash21(i + float2(0.0, 1.0)) * 6.2831853;
    float g3 = Hash21(i + float2(1.0, 1.0)) * 6.2831853;
    float a = dot(float2(cos(g0), sin(g0)), f - float2(0.0, 0.0));
    float b = dot(float2(cos(g1), sin(g1)), f - float2(1.0, 0.0));
    float c = dot(float2(cos(g2), sin(g2)), f - float2(0.0, 1.0));
    float d = dot(float2(cos(g3), sin(g3)), f - float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 1.4;
}

// Worley (cellular / Voronoi): squared distance to the nearest of one feature point per cell over the 3x3 neighborhood,
// inverted so cell centers are bright. `phase` orbits each cell's feature point on a per-cell Lissajous so the cells FLOW in
// place when animated (phase=0 -> a fixed per-cell point, i.e. a static Voronoi).
float WorleyNoise(float2 v, float phase)
{
    float2 i = floor(v);
    float2 f = frac(v);
    float md = 1.5;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 nb = float2(x, y);
            float2 h = Hash22(i + nb);
            float2 pt = 0.5 + 0.35 * sin(phase + 6.2831853 * h);   // feature point orbits over time -> flowing cells
            float2 diff = nb + pt - f;
            md = min(md, dot(diff, diff));
        }
    }
    return (1.0 - sqrt(md)) * 2.0 - 1.0;
}

// iq's Voronoi distance (shadertoy Xd23Dh): distance to the nearest cell BORDER (the Voronoi edge network), NOT the nearest
// point - thin glowing cell walls / cracks instead of Worley's filled cells. Pass 1 finds the nearest feature point (mr) and
// its cell (mb); pass 2 takes the min distance to the perpendicular bisectors with the neighbors of mb. Feature points orbit
// by `phase` so the whole network morphs. Guards normalize(0) at the nearest cell itself.
float VoronoiEdge(float2 v, float phase)
{
    float2 n = floor(v);
    float2 f = frac(v);
    float2 mr = float2(0.0, 0.0);
    float2 mb = float2(0.0, 0.0);
    float md = 8.0;
    for (int j = -1; j <= 1; j++)
    {
        for (int i = -1; i <= 1; i++)
        {
            float2 b = float2(i, j);
            float2 h = Hash22(n + b);
            float2 o = 0.5 + 0.35 * sin(phase + 6.2831853 * h);   // animated feature point
            float2 r = b + o - f;
            float d = dot(r, r);
            if (d < md)
            {
                md = d;
                mr = r;
                mb = b;
            }
        }
    }
    md = 8.0;
    for (int j = -1; j <= 1; j++)
    {
        for (int i = -1; i <= 1; i++)
        {
            float2 b = mb + float2(i, j);
            float2 h = Hash22(n + b);
            float2 o = 0.5 + 0.35 * sin(phase + 6.2831853 * h);
            float2 r = b + o - f;
            float2 diff = r - mr;
            if (dot(diff, diff) > 1e-5)                            // skip the nearest cell itself -> no normalize(0)
            {
                md = min(md, dot(0.5 * (mr + r), normalize(diff)));
            }
        }
    }
    return md;   // 0 at borders, growing inside cells
}

// ---- Circuit board. The plane is cut into tiles, and a tile edge either carries a bus of four parallel tracks, at the same
// places on both sides (0.1 of a tile apart, round the edge's middle), or nothing - so tracks run on from tile to tile
// instead of closing into rings, and end in chips rather than in thin air. Current runs one way along each column and each
// row of tracks, chosen by the column or row itself.

// Which way current runs along a column (vertical tracks) or a row (horizontal tracks) of the board.
float BoardFlow(float k, float salt)
{
    return (Hash21(float2(k, salt)) < 0.5) ? -1.0 : 1.0;
}

// The period of the waves running along the tracks, in tiles. A turn can only hand a wave on without a jump if it can
// stretch or squeeze by half of this, so it has to stay shorter than the shortest turn.
static const float BoardWave = 0.72;

// How far along its track a point lies, in tiles, measured the way the current runs. Every column and row of tracks is
// shifted by its own share of a wave, so the board does not pulse in step, and each track of a bus by a little more, so
// a wave crosses the bus with a slightly ragged front. `alongLocal` is the tile-local coordinate along the track, `o` the
// track's offset across it; vertical tracks are handled with x and y swapped.
float BoardAlong(float2 tile, float alongLocal, float o, bool vertical, float flow)
{
    float2 base = vertical ? tile.yx : tile;
    float lane = base.y + 0.5 + o;
    float2 busKey = vertical ? float2(base.y * 3.1, 1.0) : float2(2.0, base.y * 3.1);
    float2 laneKey = vertical ? float2(lane * 7.13, 5.1) : float2(3.7, lane * 7.13);
    return flow * (base.x + 0.5 + alongLocal) + BoardWave * Hash21(busKey) + 0.08 * Hash21(laneKey);
}

// A bus straight across the tile.
void BoardStraight(float2 p, float2 tile, bool vertical, float flow, inout float best, inout float along)
{
    float2 q = vertical ? p.yx : p;          // q.x along the tracks, q.y across them
    float o = -0.15 + 0.1 * clamp(round((q.y + 0.15) / 0.1), 0.0, 3.0);
    float d = abs(q.y - o);
    if (d < best)
    {
        best = d;
        along = BoardAlong(tile, q.x, o, vertical, flow);
    }
}

// Whether a tile edge carries a bus. Keyed by the edge itself, so both tiles that share it agree.
bool BoardEdge(float2 id)
{
    return Hash21(id * 1.7 + 0.23) < 0.65;
}

float BoardBox(float2 p, float2 center, float2 halfSize)
{
    float2 d = abs(p - center) - halfSize;
    return length(max(d, float2(0.0, 0.0))) + min(max(d.x, d.y), 0.0);
}

// A bus coming in over one edge (`side` +1 = the edge at +0.5) and running to the pins of a chip whose near side lies
// `stop` from the tile's middle.
void BoardToChip(float2 p, float2 tile, bool vertical, float side, float flow, float stop, inout float best, inout float along)
{
    float2 q = vertical ? p.yx : p;
    float o = -0.15 + 0.1 * clamp(round((q.y + 0.15) / 0.1), 0.0, 3.0);
    float onTrack = (side * q.x >= stop) ? abs(q.y - o) : 8.0;
    float pin = BoardBox(q, float2(side * (stop + 0.015), o), float2(0.045, 0.022));
    float d = min(onTrack, pin);
    if (d < best)
    {
        best = d;
        along = BoardAlong(tile, q.x, o, vertical, flow);
    }
}

// A bus turned round the tile corner `c` (components +-0.5): straight, a 45-degree chamfer, straight - the level sets of
// an octagon. The current's position is blended between the values at the two ports, rounded to the bunch pattern's
// period at the far one, so what comes in on one side leaves on the other without a jump.
void BoardCorner(float2 p, float2 tile, float2 c, float dv, float dh, inout float best, inout float along)
{
    float2 v = abs(p - c);
    float n = max(max(v.x, v.y), (v.x + v.y) * 0.70710678);
    float i = clamp(round((n - 0.35) / 0.1), 0.0, 3.0);
    float r = 0.35 + 0.1 * i;
    float d = abs(n - r);
    if (d < best)
    {
        best = d;
        float ov = -0.15 + 0.1 * ((c.x > 0.0) ? 3.0 - i : i);   // the track's offset where it crosses the x edge...
        float oh = -0.15 + 0.1 * ((c.y > 0.0) ? 3.0 - i : i);   // ...and where it crosses the y edge
        float atV = BoardAlong(tile, c.y, ov, true, dv);
        float atH = BoardAlong(tile, c.x, oh, false, dh);
        bool verticalIn = dv * c.y < 0.0;                        // the current arrives up or down the vertical port
        float from = verticalIn ? atV : atH;
        float to = verticalIn ? atH : atV;
        float len = 1.6568542 * r;                               // the octagon's arc between the two ports
        float miss = (to - from - len) / BoardWave;
        float span = len + (miss - round(miss)) * BoardWave;
        float a = v.y / max(v.x + v.y, 1e-5);                    // 0 at the vertical port .. 1 at the horizontal one
        along = from + (verticalIn ? a : 1.0 - a) * span;
    }
}

// One circuit tile laid out from which edges carry a bus; leftover space holds a chip. Returns distances in tiles: .x to a
// track or pin, .y along it, .z to a chip body, .w to its pin-one mark.
float4 BoardTile(float2 g)
{
    float2 tile = floor(g);
    float2 p = frac(g) - 0.5;
    float dv = BoardFlow(tile.x, 17.3);
    float dh = BoardFlow(tile.y, 43.1);
    bool n = BoardEdge(float2(tile.x, tile.y + 0.5));
    bool s = BoardEdge(float2(tile.x, tile.y - 0.5));
    bool e = BoardEdge(float2(tile.x + 0.5, tile.y));
    bool w = BoardEdge(float2(tile.x - 0.5, tile.y));
    int count = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);
    float pick = Hash21(tile + 0.37);

    float2 c = (dv * dh > 0.0) ? float2(0.5, -0.5) : float2(-0.5, -0.5);   // current may turn at c and at -c
    bool turnC = ((c.y > 0.0) ? n : s) && ((c.x > 0.0) ? e : w);
    bool turnK = ((c.y > 0.0) ? s : n) && ((c.x > 0.0) ? w : e);

    float best = 8.0;
    float along = 0.0;
    float body = 8.0;
    float mark = 8.0;
    if (count == 4 && pick < 0.45)
    {
        BoardCorner(p, tile, c, dv, dh, best, along);
        BoardCorner(p, tile, -c, dv, dh, best, along);
    }
    else if (count == 2 && turnC)
    {
        BoardCorner(p, tile, c, dv, dh, best, along);
    }
    else if (count == 2 && turnK)
    {
        BoardCorner(p, tile, -c, dv, dh, best, along);
    }
    else if (n && s && (count < 4 || pick < 0.75))
    {
        BoardStraight(p, tile, true, dv, best, along);
        if (e)
        {
            BoardToChip(p, tile, false, 1.0, dh, 0.4, best, along);
            body = min(body, BoardBox(p, float2(0.32, 0.0), float2(0.08, 0.2)));
        }
        if (w)
        {
            BoardToChip(p, tile, false, -1.0, dh, 0.4, best, along);
            body = min(body, BoardBox(p, float2(-0.32, 0.0), float2(0.08, 0.2)));
        }
    }
    else if (e && w)
    {
        BoardStraight(p, tile, false, dh, best, along);
        if (n)
        {
            BoardToChip(p, tile, true, 1.0, dv, 0.4, best, along);
            body = min(body, BoardBox(p, float2(0.0, 0.32), float2(0.2, 0.08)));
        }
        if (s)
        {
            BoardToChip(p, tile, true, -1.0, dv, 0.4, best, along);
            body = min(body, BoardBox(p, float2(0.0, -0.32), float2(0.2, 0.08)));
        }
    }
    else if (count > 0)
    {
        if (n)
        {
            BoardToChip(p, tile, true, 1.0, dv, 0.2, best, along);
        }
        if (s)
        {
            BoardToChip(p, tile, true, -1.0, dv, 0.2, best, along);
        }
        if (e)
        {
            BoardToChip(p, tile, false, 1.0, dh, 0.2, best, along);
        }
        if (w)
        {
            BoardToChip(p, tile, false, -1.0, dh, 0.2, best, along);
        }
        body = BoardBox(p, float2(0.0, 0.0), float2(0.2, 0.2));
        mark = length(p - float2(-0.13, 0.13)) - 0.025;
    }
    return float4(best, along, body, mark);
}

// The board, one layer per octave, with waves of current running along its tracks. A layer past the first whose tracks
// would crowd closer than a few pixels fades out.
float CircuitField(float2 g, int oct, float lacunarity, float gain, float phase)
{
    float foot = max(max(fwidth(g.x), fwidth(g.y)), 1e-5);   // one pixel, in tiles of the first layer
    float amp = 1.0;
    float freq = 1.0;
    float sum = 0.0;
    float norm = 0.0;
    for (int o = 0; o < 8; o++)
    {
        if (o >= oct)
        {
            break;
        }
        float4 b = BoardTile(g * freq + float2(3.7, 1.9) * float(o));
        float tilePx = 1.0 / (foot * freq);
        float wire = 1.0 - smoothstep(0.7, 1.7, b.x * tilePx);            // ~1.4 px core, a pixel of smoothing
        float resolved = (o == 0) ? 1.0 : smoothstep(3.0, 5.0, 0.1 * tilePx);

        // A wave: a long tail rising to the crest, a steep front, then quiet until the next. Each keeps its own strength
        // as it travels - some bright, some faint, some missing - and lights a few pixels round the track as it passes.
        float u = (b.y - phase * 0.9) / BoardWave;
        float x = frac(u);
        float wave = x * x * x * (1.0 - smoothstep(0.92, 1.0, x));
        float strength = smoothstep(0.3, 0.8, Hash21(float2(floor(u), 7.7)));
        float surge = wave * strength;
        float halo = (1.0 - smoothstep(1.0, 6.0, b.x * tilePx)) * surge * 0.5;
        float copper = wire * (0.45 + 1.4 * surge) + halo;

        float bodyPx = b.z * tilePx;
        float chip = max((1.0 - smoothstep(-0.5, 0.5, bodyPx)) * 0.16,          // the body, a shade off the board
                         (1.0 - smoothstep(0.4, 1.4, abs(bodyPx))) * 0.45);     // and its outline
        float mark = (1.0 - smoothstep(-0.5, 0.5, b.w * tilePx)) * 0.55;
        float v = resolved * max(copper, max(chip, mark));
        sum += amp * v;
        norm += amp;
        freq *= lacunarity;
        amp *= gain;
    }
    float flicker = 0.9 + 0.1 * ValueNoise(float2(phase * 3.0, 0.0));
    return (norm > 1e-5) ? saturate(sum / norm * 1.6 * flicker) : 0.0;
}

// Pick the base noise by basis index (0 simplex / 1 perlin / 2 value / 3 WorleyNoise). `phase` drives the Worley flow (others
// ignore it). Scalar branches only - no vector ternary.
float BaseNoise(float2 p, int basis, float phase)
{
    if (basis == 1) return PerlinNoise(p);
    if (basis == 2) return ValueNoise(p);
    if (basis == 3) return WorleyNoise(p, phase);
    return SimplexNoise(p);
}

// Fractional Brownian motion: sum `oct` octaves of the chosen base noise, each octave freq*lacunarity and amp*gain.
// Normalized to ~[-1,1]. The 8-iteration loop with an early break caps the cost while honoring the per-instance octave count.
float Fbm(float2 p, int oct, float lacunarity, float gain, int basis, float phase)
{
    float amp = 0.5;
    float freq = 1.0;
    float sum = 0.0;
    float norm = 0.0;
    float dmask = 1.0;
    if (basis == 3) dmask = 0.0;   // Worley animates via its own feature-point orbit, not a domain drift
    for (int o = 0; o < 8; o++)
    {
        if (o >= oct)
        {
            break;
        }
        // Per-octave domain warp: each octave drifts on its own phase-shifted orbit so the layers churn over each other
        // (the field evolves in place instead of rigidly scrolling). phase=0 when not animating -> a static field.
        float2 dv = dmask * 0.4 * float2(sin(phase * 0.6 + 1.7 * float(o)), cos(phase * 0.5 + 1.3 * float(o)));
        sum += amp * BaseNoise(p * freq + dv, basis, phase);
        norm += amp;
        freq *= lacunarity;
        amp *= gain;
    }
    return (norm > 1e-5) ? sum / norm : 0.0;
}

// Ridged / turbulence FBM folds over simplex. Turbulence (mode 0) sums |noise| -> billowy/smoky; ridged (mode 1) sums
// (1-|noise|)^2 -> sharp ridges / marble veins. Returns ~[0,1] (already non-negative from the abs), so the caller maps it
// straight to the color ramp rather than the signed *0.5+0.5 of the plain FBM types.
float FbmFold(float2 p, int oct, float lacunarity, float gain, int mode, float phase)
{
    float amp = 0.5;
    float freq = 1.0;
    float sum = 0.0;
    float norm = 0.0;
    for (int o = 0; o < 8; o++)
    {
        if (o >= oct)
        {
            break;
        }
        float2 dv = 0.4 * float2(sin(phase * 0.6 + 1.7 * float(o)), cos(phase * 0.5 + 1.3 * float(o)));   // per-octave churn
        float v = abs(SimplexNoise(p * freq + dv));
        if (mode == 1)
        {
            v = 1.0 - v;
            v = v * v;
        }
        sum += amp * v;
        norm += amp;
        freq *= lacunarity;
        amp *= gain;
    }
    return (norm > 1e-5) ? sum / norm : 0.0;
}

// This instance's phase on the shared clock: animating rides Time minus its offset (anim.x), paused holds anim.y.
float NoisePhase(float octavesSigned, float2 anim)
{
    return lerp(anim.y, Time - anim.x, step(octavesSigned, -0.0001));
}

// Pattern mix at `p` (px from the rect's top-left): 0 = Color1, 1 = Color2, antialiased by fwidth (the checker via iq's
// filtered checker). Type 4 is continuous FBM noise.
float PatternMix(int type, float2 p, float cell, float4 noise, float2 anim)
{
    cell = max(cell, 1.0);
    float2 g = p / cell;

    if (type == 1)   // vertical stripes (the x-component of the checker: Color2 every other cell)
    {
        float w = fwidth(g.x) + 1e-4;
        float i = 2.0 * (abs(frac((g.x - 0.5 * w) * 0.5) - 0.5) - abs(frac((g.x + 0.5 * w) * 0.5) - 0.5)) / w;
        return saturate(0.5 - 0.5 * i);
    }
    if (type == 2)   // dots: a Color2 disc centered in each cell
    {
        float2 f = frac(g) - 0.5;
        float d = length(f) - 0.34;                // radius 0.34 cells
        float aa = fwidth(d) + 1e-4;
        return 1.0 - smoothstep(-aa, aa, d);       // 1 inside the dot
    }
    if (type == 3)   // grid: thin Color2 lines at cell boundaries
    {
        float2 dl = (0.5 - abs(frac(g) - 0.5)) * cell;   // px distance to the nearest line, per axis
        float dmin = min(dl.x, dl.y);
        float aa = fwidth(dmin) + 1e-4;
        return 1.0 - smoothstep(0.5, 0.5 + aa + 1.0, dmin);   // ~1px line
    }

    // NOISE lives in its own hundred (PatternBrushRecord.NoiseBase): 100 simplex, 101 perlin, 102 value, 103 worley,
    // 104 ridged, 105 turbulence, 106 voronoi borders, 107 combustible, 108 circuit. Patterns keep 0..N. The two families share this
    // one field because they share one record and one collector, and separating them by RANGE is what keeps either
    // enum free to grow without renumbering the other.
    if (type >= 100 && type <= 103)   // FBM noise: simplex / perlin / value / worley
    {
        int basis = type - 100;                                          // 100->0 simplex, 101->1 perlin, ...
        int oct = int(abs(noise.x));                                     // octaves is sign-encoded: negative = animate
        float phase = NoisePhase(noise.x, anim);
        float2 np = g + noise.y;                                         // base noise domain + seed offset
        float n = Fbm(np, oct, max(noise.z, 1.0), noise.w, basis, phase);   // Color1 (low) -> Color2 (high); phase drives flow
        return saturate(n * 0.5 + 0.5);
    }
    if (type == 104 || type == 105)   // ridged (104) / turbulence (105): FBM folds over simplex, already ~[0,1]
    {
        int mode = 0;                              // turbulence
        if (type == 104) mode = 1;                 // ridged
        int oct = int(abs(noise.x));
        float phase = NoisePhase(noise.x, anim);
        float2 np = g + noise.y;
        float n = FbmFold(np, oct, max(noise.z, 1.0), noise.w, mode, phase);
        if (type == 105) n = n * 1.6;               // turbulence is dimmer (averaged |noise|) - lift it for contrast
        return saturate(n);
    }
    if (type == 106)   // Voronoi BORDER network (iq Xd23Dh): thin bright cell walls, morphing under Animate
    {
        float ph = NoisePhase(noise.x, anim);
        float dd = VoronoiEdge(g + noise.y, ph);
        float aa = fwidth(dd) + 1e-4;
        return 1.0 - smoothstep(0.0, 0.06 + aa, dd);   // Color2 on the borders, Color1 inside the cells
    }
    if (type == 108)   // circuit board: buses of tracks with 45-degree turns and pads, current running along them
    {
        int oct = max(int(abs(noise.x)), 1);
        float phase = NoisePhase(noise.x, anim);
        return CircuitField(g + noise.y, oct, max(noise.z, 1.0), noise.w, phase);
    }
    if (type == 4)   // hexagonal grid (honeycomb) lines
    {
        float2 grid = float2(1.0, 1.7320508);
        float2 hh = grid * 0.5;
        float2 a = frac(g / grid) * grid - hh;
        float2 b = frac((g + hh) / grid) * grid - hh;
        float2 gv = dot(a, a) < dot(b, b) ? a : b;             // fragment within the nearest hex, centered
        float2 ag = abs(gv);
        float hd = max(ag.x * 0.8660254 + ag.y * 0.5, ag.y);   // 0 at center .. 0.5 at the hex edge
        float dpx = (0.5 - hd) * cell;                         // px to the nearest honeycomb edge
        float aa = fwidth(dpx) + 1e-4;
        return 1.0 - smoothstep(0.5, 0.5 + aa + 1.0, dpx);     // ~1px hex lines
    }
    if (type == 5)   // hatch lines; noise.xy = the unit line normal (cos/sin baked on the CPU - NO trig here, so the
    {                //                 already-maxed pattern PS doesn't grow: dot replaces the old p.x+p.y)
        float t = dot(p, float2(noise.x, noise.y)) / cell;
        float dpx = (0.5 - abs(frac(t) - 0.5)) * cell;       // px to the nearest line (cell = perpendicular spacing)
        float aa = fwidth(dpx) + 1e-4;
        return 1.0 - smoothstep(0.5, 0.5 + aa + 1.0, dpx);
    }
    if (type == 6)   // WEAVE (carbon fiber): two ribbons cross in every cell, and which one lies ON TOP alternates
    {                 // like a checkerboard - that alternation IS the weave; without it this is just a grid.
        float2 f = frac(g);
        float2 id = floor(g);
        float over = fmod(id.x + id.y, 2.0);                 // 0 = the horizontal ribbon is on top here
        float dh = abs(f.y - 0.5);                           // distance across the horizontal ribbon
        float dv = abs(f.x - 0.5);
        float halfW = 0.30;                                  // ribbon half-width, in cells (leaves a gap between tows)
        float mh = 1.0 - smoothstep(halfW - fwidth(dh) - 1e-4, halfW + fwidth(dh) + 1e-4, dh);
        float mv = 1.0 - smoothstep(halfW - fwidth(dv) - 1e-4, halfW + fwidth(dv) + 1e-4, dv);
        // Shading ACROSS each ribbon: bright along its middle, falling off to the edges. This is what makes the
        // crossing read as depth rather than as a flat plaid.
        float bh = mh * (0.55 + 0.45 * saturate(1.0 - dh / halfW));
        float bv = mv * (0.55 + 0.45 * saturate(1.0 - dv / halfW));
        float onTop = step(over, 0.5);
        float top = lerp(bv, bh, onTop);
        float under = lerp(bh, bv, onTop);
        return saturate(max(top, under * 0.45));             // the one underneath reads darker where it passes below
    }

    // checkerboard (type 0): iq's analytically-filtered checker (period 2 in g -> cell-sized squares)
    float2 w2 = fwidth(g) + 1e-4;
    float2 i2 = 2.0 * (abs(frac((g - 0.5 * w2) * 0.5) - 0.5) - abs(frac((g + 0.5 * w2) * 0.5) - 0.5)) / w2;
    return saturate(0.5 - 0.5 * i2.x * i2.y);
}

// --- Combustible Voronoi (Shane, shadertoy 4tlSzl): 3D Voronoi fBm colored by a blackbody FIRE palette. Its own color
// path (the palette returns RGB, not a 2-color lerp), so PatternPS handles type 13 specially. fBm layers x a 3x3x3
// cell search - the heaviest pattern branch. ---
float3 Hash33(float3 p)
{
    float n = sin(dot(p, float3(7.0, 157.0, 113.0)));
    return frac(float3(2097152.0, 262144.0, 32768.0) * n);
}

// 3D Voronoi (Shane's rehash of iq): squared distance to the nearest 3D feature point over the 3x3 cell block, the z loop
// unrolled (GPUs dislike deep nesting). Range [0,1].
float Voronoi3(float3 p)
{
    float3 g = floor(p);
    p = frac(p);
    float3 b;
    float3 r;
    float d = 1.0;
    for (int j = -1; j <= 1; j++)
    {
        for (int i = -1; i <= 1; i++)
        {
            b = float3(float(i), float(j), -1.0);
            r = b - p + Hash33(g + b);
            d = min(d, dot(r, r));
            b.z = 0.0;
            r = b - p + Hash33(g + b);
            d = min(d, dot(r, r));
            b.z = 1.0;
            r = b - p + Hash33(g + b);
            d = min(d, dot(r, r));
        }
    }
    return d;
}

// fBm of the 3D Voronoi with time dilation on the z axis (position and time frequencies advance at different rates -> a
// parallax "combustible" flow). 5 layers. Range [0,1].
float NoiseLayers(float3 p, float time)
{
    float3 t = float3(0.0, 0.0, p.z + time * 1.5);
    float tot = 0.0;
    float sum = 0.0;
    float amp = 1.0;
    for (int i = 0; i < 3; i++)
    {
        tot += Voronoi3(p + t) * amp;
        p *= 2.0;
        t *= 1.5;
        sum += amp;
        amp *= 0.5;
    }
    return tot / sum;
}

// Shane's favorite fire palette: blackbody radiation across a 1400..2700K temperature range (Planck-ish per wavelength).
float3 FirePalette(float i)
{
    float T = 1400.0 + 1300.0 * i;
    float3 L = float3(7.4, 5.6, 4.4);
    L = pow(L, float3(5.0, 5.0, 5.0)) * (exp(143876.719683 / (T * L)) - 1.0);
    return 1.0 - exp(-5e8 / L);
}

// What evaluating a pattern needs, shared by the SDF and mesh pattern passes so both color identically. `pTopLeft` is
// the pattern origin; `centerRel`/`halfY` serve the combustible fireball.
struct PatternFill
{
    float4 Params;
    float4 Color1;
    float4 Color2;
    float4 Color3;
    float4 Noise;
    float4 Anim;
};

float4 PatternFillColor(PatternFill it, int ptype, float2 pTopLeft, float2 centerRel, float halfY)
{
    // ptype comes in as a compile-time constant from the pass entry point, so the optimizer drops every branch but one
    // and each pass gets a small pixel shader.
    float4 fill;
    if (ptype == 107)   // Combustible Voronoi: its own 3D-ray + fire-palette color path (ignores Color1/Color2 as a lerp)
    {
        float time = NoisePhase(it.Noise.x, it.Anim.xy);
        float2 uv = centerRel / max(halfY, 1.0);   // centered, normalized by half height
        float cs = cos(time * 0.25);
        float si = sin(time * 0.25);
        float3 rd = normalize(float3(uv.x, uv.y, 0.3926991));   // ~PI/8 ray, gives the central fireball
        rd.xy = float2(rd.x * cs - rd.y * si, rd.x * si + rd.y * cs);   // rolling camera
        float c = NoiseLayers(rd * 2.0, time);
        c = max(c + dot(Hash33(rd) * 2.0 - 1.0, float3(0.015, 0.015, 0.015)), 0.0);   // subtle dust
        c *= sqrt(c) * 1.5;                                  // contrast
        // Palette. noise.w = flag (>=0.5 built-in blackbody fire; <0.5 the brush's own Color1->MidColor->Color2 ramp). Both
        // are computed and selected by step().
        float3 fireCol = sqrt(saturate(pow(FirePalette(c), float3(1.25, 1.25, 1.25))));
        float cc = saturate(c);
        float3 duo3 = lerp(it.Color1.xyz, it.Color2.xyz, cc);
        float3 triLo3 = lerp(it.Color1.xyz, it.Color3.xyz, saturate(cc * 2.0));
        float3 triHi3 = lerp(it.Color3.xyz, it.Color2.xyz, saturate(cc * 2.0 - 1.0));
        float3 tri3 = lerp(triLo3, triHi3, step(0.5, cc));
        float3 userCol = lerp(duo3, tri3, step(0.001, it.Color3.w));   // Color3.w==0 -> 2-color ramp
        float3 col = lerp(userCol, fireCol, step(0.5, it.Noise.w));
        fill = float4(col, it.Color1.w);                            // alpha carries brush opacity
    }
    else
    {
        float k = PatternMix(ptype, pTopLeft, it.Params.z, it.Noise, it.Anim.xy);
        // TRITONE gradient-map (Color1 -> Color3 mid -> Color2), selected by step(). Color3.w==0 (no mid color) blends
        // back to the plain two-color duotone.
        float4 duo = lerp(it.Color1, it.Color2, k);
        float4 triLo = lerp(it.Color1, it.Color3, saturate(k * 2.0));
        float4 triHi = lerp(it.Color3, it.Color2, saturate(k * 2.0 - 1.0));
        float4 tri = lerp(triLo, triHi, step(0.5, k));
        fill = lerp(duo, tri, step(0.001, it.Color3.w));
    }
    return fill;
}
