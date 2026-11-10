using Adamantium.Graphics.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Rendering;

/// <summary>The sampler a picture is read with: its <see cref="ImageSource.Sampling"/>, clamped to its edge.</summary>
internal static class ImageSamplers
{
    public static SamplerState Of(ImageSource image) =>
        image?.Sampling == ImageSampling.Pixelated ? SamplerStates.PixelatedClampToEdge : SamplerStates.LinearClampToEdge;

    /// <summary>The sampler of a brush's picture - an image brush's source; smooth for anything else.</summary>
    public static SamplerState Of(Brush brush) => Of((brush as ImageBrush)?.Source);
}
