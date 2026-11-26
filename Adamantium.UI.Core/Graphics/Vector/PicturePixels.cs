using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Core.Graphics.Vector;

/// <summary>A picture's pixels as a vector writer embeds them: RGBA, 8 bits a channel, with a tile brush's adjustment
/// and tint already in, as the renderer applies them to every pixel. Pixels of 8 or 16 bits a channel, gray to RGBA in
/// either order, are taken; a picture whose pixels are not on the CPU, or of another layout, is not.</summary>
internal static class PicturePixels
{
    /// <summary>The bitmap drawn for <paramref name="source"/> - its <paramref name="frame"/>, for an animation, -1 for
    /// the picture as it is; null when it has no pixels on the CPU.</summary>
    public static BitmapSource BitmapOf(ImageSource source, int frame)
    {
        var bitmap = frame >= 0 && source is BitmapImage animated && frame < animated.FrameCount
            ? animated.GetFrame((uint)frame)
            : source as BitmapSource;
        return bitmap is { PixelBytes: not null, PixelWidth: > 0, PixelHeight: > 0 } ? bitmap : null;
    }

    /// <summary>Whether a vector writer can put <paramref name="source"/> down: a drawing, or a bitmap with its pixels on
    /// the CPU - not a GPU surface, a live 3D view's or a game's.</summary>
    public static bool IsPrintable(ImageSource source, int frame) =>
        source is DrawingImage { Drawing: not null } || BitmapOf(source, frame) != null;

    /// <summary>The bitmap's pixels as RGBA; null for a layout not taken.</summary>
    public static byte[] RgbaOf(BitmapSource bitmap) =>
        Rgba(bitmap.PixelBytes, (int)bitmap.PixelWidth, (int)bitmap.PixelHeight, (Format)bitmap.SurfaceLayout);

    /// <summary>The pixels as RGBA, 8 bits a channel; null for a layout not taken.</summary>
    public static byte[] Rgba(byte[] pixels, int width, int height, Format format)
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

    /// <summary>Brightness, contrast and saturation (the shader's <c>AdjustColor</c>: brightness, contrast - 1,
    /// saturation - 1), then the tint multiplied in.</summary>
    public static void Recolor(byte[] rgba, Color tint, Vector4<float> adjust)
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
}
