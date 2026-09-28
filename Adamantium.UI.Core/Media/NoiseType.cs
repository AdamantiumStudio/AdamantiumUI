namespace Adamantium.UI.Core.Media;

/// <summary>The noise a <see cref="NoiseBrush"/> produces. VoronoiBorders follows iq's Xd23Dh and CombustibleVoronoi
/// Shane's 4tlSzl; CombustibleVoronoi uses its own fire palette, taking only Color1's alpha.</summary>
public enum NoiseType
{
    Simplex,
    Perlin,
    Value,
    Worley,
    Ridged,
    Turbulence,
    VoronoiBorders,
    CombustibleVoronoi,
    Circuit
}
