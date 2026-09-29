namespace Adamantium.UI.Core.Media;

/// <summary>How a <see cref="GradientBrush"/> interpolates BETWEEN its stops.</summary>
public enum ColorInterpolationMode
{
    /// <summary>Interpolate in sRGB (WPF's behavior). Simple, but muddies midpoints (a gray dead-zone between
    /// complementary colors) and can band on large fills.</summary>
    Srgb,

    /// <summary>Interpolate in OKLab (perceptually uniform). Smooth, even-brightness midpoints and no muddy grays - the
    /// modern look. Costs a per-fragment color-space convert.</summary>
    Oklab
}
