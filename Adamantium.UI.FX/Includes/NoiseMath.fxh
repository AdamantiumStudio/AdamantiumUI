// Randomness and the base noise field, shared because they are not only the brushes' business: the backdrop materials
// take their film grain from the hash, the living halo wanders its reach with the simplex noise the noise brushes are
// built on. Everything derived from the field - fbm, worley, voronoi, the pattern mixer - lives with the brushes.

// Spreads every bit of h over the whole word, so neighbouring inputs give unrelated outputs (the 32-bit finalizer of
// MurmurHash3).
uint MixBits(uint h)
{
    h ^= h >> 16;
    h *= 0x85ebca6bu;
    h ^= h >> 13;
    h *= 0xc2b2ae35u;
    h ^= h >> 16;
    return h;
}

// The bits of a point - a lattice cell or a pixel - made into one random word.
uint HashBits(float2 p)
{
    return MixBits(asuint(p.x) ^ MixBits(asuint(p.y) + 0x9e3779b9u));
}

// A random number in [0,1) for a point: its top 24 bits, which a float holds exactly.
float Hash21(float2 p)
{
    return (HashBits(p) >> 8) * (1.0 / 16777216.0);
}

// Two independent random numbers in [0,1) for a point.
float2 Hash22(float2 p)
{
    uint h = HashBits(p);
    return float2(h >> 8, MixBits(h) >> 8) * (1.0 / 16777216.0);
}

static const float2 SimplexGradients[8] =
{
    float2(1.0, 0.0), float2(0.70710678, 0.70710678), float2(0.0, 1.0), float2(-0.70710678, 0.70710678),
    float2(-1.0, 0.0), float2(-0.70710678, -0.70710678), float2(0.0, -1.0), float2(0.70710678, -0.70710678)
};

// A corner's share of the simplex noise: its gradient - one of eight directions, by the corner's hash - dotted with the
// offset from it, falling to nothing at a distance of sqrt(0.5) as (0.5 - r^2)^4.
float SimplexCorner(float2 offset, float2 cell)
{
    float falloff = max(0.5 - dot(offset, offset), 0.0);
    falloff *= falloff;
    return falloff * falloff * dot(SimplexGradients[HashBits(cell) & 7u], offset);
}

// 2D simplex noise in about [-1,1]. The plane is skewed so its triangles become the halves of unit squares, the point's
// triangle found there, and the shares of its three corners summed.
float SimplexNoise(float2 v)
{
    const float Skew = 0.36602540;   // (sqrt(3) - 1) / 2
    const float Unskew = 0.21132487; // (3 - sqrt(3)) / 6

    float2 cell = floor(v + (v.x + v.y) * Skew);
    float2 first = v - cell + (cell.x + cell.y) * Unskew;
    float2 step = first.x > first.y ? float2(1.0, 0.0) : float2(0.0, 1.0);
    float2 second = first - step + Unskew;
    float2 third = first - 1.0 + 2.0 * Unskew;

    // The largest sum the three shares reach, 1/99.2, scaled to one
    return 99.2 * (SimplexCorner(first, cell) + SimplexCorner(second, cell + step) + SimplexCorner(third, cell + 1.0));
}
