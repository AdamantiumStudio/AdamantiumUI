using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Adamantium.Imaging;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The application's icon as its assembly carries it - the build embeds AdamantiumIcon, or else
/// ApplicationIcon - as square RGBA pictures, the largest first.</summary>
internal sealed class ApplicationIcon
{
    /// <summary>The name the build gives the embedded icon.</summary>
    public const string ResourceName = "Adamantium.ApplicationIcon";

    private ApplicationIcon(IReadOnlyList<Picture> pictures)
    {
        Pictures = pictures;
    }

    public IReadOnlyList<Picture> Pictures { get; }

    /// <summary>The icon the entry assembly carries; null where it carries none or it cannot be read.</summary>
    public static ApplicationIcon Load() => Load(Assembly.GetEntryAssembly());

    /// <summary>The icon <paramref name="assembly"/> carries; null where it carries none or it cannot be read.</summary>
    public static ApplicationIcon Load(Assembly assembly)
    {
        using var stream = assembly?.GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            return null;
        }

        try
        {
            if (BitmapLoader.Load(stream) is { } bitmap && From(bitmap) is { } icon)
            {
                return icon;
            }

            Serilog.Log.Logger.Warning("The application's icon is in no format it is read in: a PNG or an ICO is");
            return null;
        }
        catch (Exception e)
        {
            Serilog.Log.Logger.Warning(e, "The application's icon could not be read");
            return null;
        }
    }

    /// <summary>The square pictures of <paramref name="bitmap"/>, every level of an ICO; null where none is square.
    /// </summary>
    public static ApplicationIcon From(IRawBitmap bitmap)
    {
        List<Picture> pictures = [];
        var levels = Math.Max(1u, bitmap.MipLevelsCount);
        for (uint level = 0; level < levels; level++)
        {
            var frame = bitmap.MipLevelsCount > 1 ? bitmap.GetMipLevelData(level) : bitmap.GetFrameData(0);
            var (width, height) = ((int)frame.Description.Width, (int)frame.Description.Height);
            var levelPixels = bitmap.MipLevelsCount > 1 ? Rgba(frame.RawPixels, width * height) : bitmap.GetRgba8Pixels();
            if (width != height || width == 0 || pictures.Exists(picture => picture.Size == width) ||
                levelPixels is not { } rgba)
            {
                continue;
            }

            pictures.Add(new Picture(width, rgba));
        }

        pictures.Sort((a, b) => b.Size.CompareTo(a.Size));
        return pictures.Count > 0 ? new ApplicationIcon(pictures) : null;
    }

    private static byte[] Rgba(byte[] pixels, int count)
    {
        var channels = pixels.Length / count;
        if (channels is not (1 or 3 or 4) || pixels.Length != channels * count)
        {
            return null;
        }

        if (channels == 4)
        {
            return pixels;
        }

        var rgba = new byte[count * 4];
        for (var pixel = 0; pixel < count; pixel++)
        {
            var from = pixel * channels;
            rgba[pixel * 4] = pixels[from];
            rgba[pixel * 4 + 1] = pixels[channels == 1 ? from : from + 1];
            rgba[pixel * 4 + 2] = pixels[channels == 1 ? from : from + 2];
            rgba[pixel * 4 + 3] = byte.MaxValue;
        }

        return rgba;
    }

    /// <summary>One picture of the icon: <see cref="Size"/> pixels a side, RGBA rows top-down.</summary>
    internal sealed class Picture(int size, byte[] rgba)
    {
        private byte[] _premultipliedBgra;

        public int Size { get; } = size;

        public byte[] Rgba { get; } = rgba;

        /// <summary>The pixels as a Wayland ARGB8888 buffer takes them: blue, green, red, alpha, the colors multiplied by
        /// the alpha.</summary>
        public byte[] PremultipliedBgra
        {
            get
            {
                if (_premultipliedBgra != null)
                {
                    return _premultipliedBgra;
                }

                var bgra = new byte[Rgba.Length];
                for (var i = 0; i < bgra.Length; i += 4)
                {
                    var alpha = Rgba[i + 3];
                    bgra[i] = (byte)((Rgba[i + 2] * alpha + 127) / 255);
                    bgra[i + 1] = (byte)((Rgba[i + 1] * alpha + 127) / 255);
                    bgra[i + 2] = (byte)((Rgba[i] * alpha + 127) / 255);
                    bgra[i + 3] = alpha;
                }

                return _premultipliedBgra = bgra;
            }
        }
    }
}
