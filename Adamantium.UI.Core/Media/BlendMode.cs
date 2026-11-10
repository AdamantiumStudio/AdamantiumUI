namespace Adamantium.UI.Core.Media;

/// <summary>How a group's colors combine with what lies under it - the blend modes of PDF (ISO 32000-2, 11.3.5), in its
/// order. <see cref="Normal"/> lays the group over; the rest mix each color with the one beneath.</summary>
public enum BlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
}
