using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Writing;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Pdf;

/// <summary>Bitmaps turned into PDF images, each once: by the pixel buffer it holds and how it is sampled, so a picture
/// shown on every page of a document is one image, and a picture given new pixels is a new one. The pixels are taken as
/// they are the first time they are drawn, as <see cref="PicturePixels"/> takes them.</summary>
internal sealed class PdfPictures
{
    private readonly Dictionary<byte[], PdfStream> _smooth = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<byte[], PdfStream> _pixelated = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Recolored, PdfStream> _recolored = [];

    /// <summary>A picture's pixels with their colors changed - by a tile brush's tint and adjustment.</summary>
    private readonly record struct Recolored(byte[] Pixels, bool Smooth, Color Tint, Vector4<float> Adjust);

    /// <summary>The image of <paramref name="source"/> - of its <paramref name="frame"/>, for an animation; null when
    /// its pixels cannot be had. <paramref name="adjust"/> (brightness, contrast - 1, saturation - 1) then
    /// <paramref name="tint"/> change every pixel first, as the renderer's <c>AdjustColor</c> and tint do.</summary>
    public PdfStream Of(ImageSource source, int frame, Color tint, Vector4<float> adjust)
    {
        if (tint == Colors.White && adjust == default)
        {
            return Of(source, frame);
        }

        if (PicturePixels.BitmapOf(source, -1) is not { } bitmap)
        {
            return null;
        }

        var smooth = source.Sampling != ImageSampling.Pixelated;
        var key = new Recolored(bitmap.PixelBytes, smooth, tint, adjust);
        if (_recolored.TryGetValue(key, out var image))
        {
            return image;
        }

        var rgba = PicturePixels.RgbaOf(bitmap);
        if (rgba == null)
        {
            return null;
        }

        PicturePixels.Recolor(rgba, tint, adjust);
        image = PdfImages.FromRgba((int)bitmap.PixelWidth, (int)bitmap.PixelHeight, rgba, smooth);
        _recolored[key] = image;
        return image;
    }

    /// <summary>The image of <paramref name="source"/> - of its <paramref name="frame"/>, for an animation; null when
    /// its pixels cannot be had.</summary>
    public PdfStream Of(ImageSource source, int frame)
    {
        if (PicturePixels.BitmapOf(source, frame) is not { } bitmap)
        {
            return null;
        }

        var smooth = source.Sampling != ImageSampling.Pixelated;
        var images = smooth ? _smooth : _pixelated;
        if (images.TryGetValue(bitmap.PixelBytes, out var image))
        {
            return image;
        }

        var rgba = PicturePixels.RgbaOf(bitmap);
        if (rgba == null)
        {
            return null;
        }

        image = PdfImages.FromRgba((int)bitmap.PixelWidth, (int)bitmap.PixelHeight, rgba, smooth);
        images[bitmap.PixelBytes] = image;
        return image;
    }
}
