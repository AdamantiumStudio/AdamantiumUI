using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Writing;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Pdf;

/// <summary>Bitmaps turned into PDF images, each once: by the pixel buffer it holds and how it is sampled, so a picture
/// shown on every page of a document is one image, and a picture given new pixels is a new one. The pixels are taken as
/// they are the first time they are drawn. Pixels of 8 or 16 bits a channel, gray to RGBA in either order, are taken; a
/// picture whose pixels are not on the CPU, or of another layout, is not.</summary>
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

        var bitmap = source as BitmapSource;
        if (bitmap is not { PixelBytes: { } pixels } || bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
        {
            return null;
        }

        var smooth = source.Sampling != ImageSampling.Pixelated;
        var key = new Recolored(pixels, smooth, tint, adjust);
        if (_recolored.TryGetValue(key, out var image))
        {
            return image;
        }

        var rgba = Rgba(pixels, (int)bitmap.PixelWidth, (int)bitmap.PixelHeight, (Format)bitmap.SurfaceLayout);
        if (rgba == null)
        {
            return null;
        }

        Recolor(rgba, tint, adjust);
        image = PdfImages.FromRgba((int)bitmap.PixelWidth, (int)bitmap.PixelHeight, rgba, smooth);
        _recolored[key] = image;
        return image;
    }

    /// <summary>Brightness, contrast and saturation (the shader's <c>AdjustColor</c>), then the tint multiplied in.
    /// </summary>
    internal static void Recolor(byte[] rgba, Color tint, Vector4<float> adjust)
    {
        for (var index = 0; index + 3 < rgba.Length; index += 4)
        {
            var red = rgba[index] / 255.0;
            var green = rgba[index + 1] / 255.0;
            var blue = rgba[index + 2] / 255.0;
            if (adjust != default)
            {
                var gray = 0.2126 * red + 0.7152 * green + 0.0722 * blue;
                var saturation = 1.0 + adjust.Z;
                var contrast = 1.0 + adjust.Y;
                red = Math.Clamp(((gray + (red - gray) * saturation) - 0.5) * contrast + 0.5 + adjust.X, 0, 1);
                green = Math.Clamp(((gray + (green - gray) * saturation) - 0.5) * contrast + 0.5 + adjust.X, 0, 1);
                blue = Math.Clamp(((gray + (blue - gray) * saturation) - 0.5) * contrast + 0.5 + adjust.X, 0, 1);
            }

            rgba[index] = (byte)Math.Round(red * tint.R);
            rgba[index + 1] = (byte)Math.Round(green * tint.G);
            rgba[index + 2] = (byte)Math.Round(blue * tint.B);
            rgba[index + 3] = (byte)Math.Round(rgba[index + 3] * tint.A / 255.0);
        }
    }

    /// <summary>The image of <paramref name="source"/> - of its <paramref name="frame"/>, for an animation; null when
    /// its pixels cannot be had.</summary>
    public PdfStream Of(ImageSource source, int frame)
    {
        var bitmap = frame >= 0 && source is BitmapImage animated && frame < animated.FrameCount
            ? animated.GetFrame((uint)frame)
            : source as BitmapSource;
        if (bitmap is not { PixelBytes: { } pixels } || bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
        {
            return null;
        }

        var smooth = source.Sampling != ImageSampling.Pixelated;
        var images = smooth ? _smooth : _pixelated;
        if (images.TryGetValue(pixels, out var image))
        {
            return image;
        }

        var rgba = Rgba(pixels, (int)bitmap.PixelWidth, (int)bitmap.PixelHeight, (Format)bitmap.SurfaceLayout);
        if (rgba == null)
        {
            return null;
        }

        image = PdfImages.FromRgba((int)bitmap.PixelWidth, (int)bitmap.PixelHeight, rgba, smooth);
        images[pixels] = image;
        return image;
    }

    /// <summary>The pixels as RGBA, 8 bits a channel; null for a layout not taken.</summary>
    internal static byte[] Rgba(byte[] pixels, int width, int height, Format format)
    {
        int channels;
        int bytes;
        var swapped = false;
        switch (format)
        {
            case Format.R8_UNORM or Format.R8_SRGB:
                (channels, bytes) = (1, 1);
                break;
            case Format.R8G8B8_UNORM or Format.R8G8B8_SRGB:
                (channels, bytes) = (3, 1);
                break;
            case Format.B8G8R8_UNORM or Format.B8G8R8_SRGB:
                (channels, bytes, swapped) = (3, 1, true);
                break;
            case Format.R8G8B8A8_UNORM or Format.R8G8B8A8_SRGB:
                (channels, bytes) = (4, 1);
                break;
            case Format.B8G8R8A8_UNORM or Format.B8G8R8A8_SRGB:
                (channels, bytes, swapped) = (4, 1, true);
                break;
            case Format.R16_UNORM:
                (channels, bytes) = (1, 2);
                break;
            case Format.R16G16B16_UNORM:
                (channels, bytes) = (3, 2);
                break;
            case Format.R16G16B16A16_UNORM:
                (channels, bytes) = (4, 2);
                break;
            default:
                return null;
        }

        var count = (long)width * height;
        if (pixels.LongLength < count * channels * bytes)
        {
            return null;
        }

        var rgba = new byte[count * 4];
        // A 16-bit channel is little-endian: its high byte is the second.
        var high = bytes - 1;
        for (long index = 0; index < count; index++)
        {
            var from = index * channels * bytes;
            var to = index * 4;
            byte Channel(int channel) => pixels[from + channel * bytes + high];
            if (channels == 1)
            {
                var gray = Channel(0);
                rgba[to] = rgba[to + 1] = rgba[to + 2] = gray;
                rgba[to + 3] = 255;
                continue;
            }

            rgba[to] = Channel(swapped ? 2 : 0);
            rgba[to + 1] = Channel(1);
            rgba[to + 2] = Channel(swapped ? 0 : 2);
            rgba[to + 3] = channels == 4 ? Channel(3) : (byte)255;
        }

        return rgba;
    }
}
