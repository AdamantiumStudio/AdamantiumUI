namespace Adamantium.UI.Core.Media.Imaging;

/// <summary>How a picture's pixels are read where it is drawn larger or smaller than they are.</summary>
public enum ImageSampling
{
    /// <summary>Blended between neighbors: a smooth picture at any size.</summary>
    Smooth,

    /// <summary>Enlarged in hard squares, each the nearest pixel - pixel art, a chart's cells, a PDF image that asks
    /// not to be interpolated - and reduced smoothly, so it does not shimmer.</summary>
    Pixelated,
}
