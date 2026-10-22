using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Imaging;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>A cursor picture from a file: a Windows .cur (or .ico) or a PNG held at its top-left corner, as premultiplied
/// BGRA with its hot spot.</summary>
internal sealed class WaylandCursorImage
{
    /// <summary>The logical size a cursor is shown at, as Windows does with every cursor file.</summary>
    public const int LogicalSize = 32;

    private WaylandCursorImage(byte[] pixels, int width, int height, int hotX, int hotY)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        HotX = hotX;
        HotY = hotY;
    }

    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    public int HotX { get; }

    public int HotY { get; }

    /// <summary>The picture nearest <paramref name="wantedSize"/> pixels across; null when the file is none of these.</summary>
    public static WaylandCursorImage Load(string path, int wantedSize)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return bytes is [0x89, (byte)'P', (byte)'N', (byte)'G', ..] ? FromPng(bytes, 0, 0) : FromCur(bytes, wantedSize);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static WaylandCursorImage FromCur(byte[] file, int wantedSize)
    {
        var data = file.AsSpan();
        if (data.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0)
        {
            return null;
        }

        var isCursor = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) == 2;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        var best = -1;
        var bestSize = 0;
        for (var i = 0; i < count; i++)
        {
            var size = data[6 + i * 16] == 0 ? 256 : data[6 + i * 16];
            var better = best < 0
                         || (size >= wantedSize ? bestSize < wantedSize || size < bestSize : bestSize < wantedSize && size > bestSize);
            if (better)
            {
                best = i;
                bestSize = size;
            }
        }

        if (best < 0)
        {
            return null;
        }

        var entry = data.Slice(6 + best * 16, 16);
        var hotX = isCursor ? BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]) : 0;
        var hotY = isCursor ? BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]) : 0;
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
        var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
        var image = data.Slice(offset, length);
        return image is [0x89, (byte)'P', (byte)'N', (byte)'G', ..]
            ? FromPng(image.ToArray(), hotX, hotY)
            : FromDib(image, hotX, hotY);
    }

    private static WaylandCursorImage FromPng(byte[] png, int hotX, int hotY)
    {
        var bitmap = BitmapLoader.Load(new MemoryStream(png));
        if (bitmap == null || bitmap.PixelFormat.SizeInBytes != 4)
        {
            return null;
        }

        var width = (int)bitmap.Width;
        var height = (int)bitmap.Height;
        var rgba = bitmap.GetRawPixels(0);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            Store(pixels, i, rgba[i + 2], rgba[i + 1], rgba[i], rgba[i + 3]);
        }

        return new WaylandCursorImage(pixels, width, height, hotX, hotY);
    }

    private static WaylandCursorImage FromDib(ReadOnlySpan<byte> dib, int hotX, int hotY)
    {
        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(dib);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]) / 2;
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var used = (int)BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        if (width is <= 0 or > 256 || height is <= 0 or > 256 || bits is not (1 or 4 or 8 or 24 or 32))
        {
            return null;
        }

        var paletteSize = bits <= 8 ? (used > 0 ? used : 1 << bits) : 0;
        var palette = dib.Slice(headerSize, paletteSize * 4);
        var colorStride = (width * bits + 31) / 32 * 4;
        var colors = dib[(headerSize + paletteSize * 4)..];
        var maskStride = (width + 31) / 32 * 4;
        var mask = colors[(colorStride * height)..];
        var hasMask = mask.Length >= maskStride * height;

        var pixels = new byte[width * height * 4];
        var anyAlpha = false;
        for (var y = 0; y < height; y++)
        {
            var row = colors.Slice((height - 1 - y) * colorStride, colorStride);
            for (var x = 0; x < width; x++)
            {
                byte b, g, r, a = 255;
                if (bits >= 24)
                {
                    var at = x * (bits / 8);
                    (b, g, r) = (row[at], row[at + 1], row[at + 2]);
                    if (bits == 32)
                    {
                        a = row[at + 3];
                        anyAlpha |= a != 0;
                    }
                }
                else
                {
                    var index = (row[x * bits / 8] >> (8 - bits - x * bits % 8)) & ((1 << bits) - 1);
                    (b, g, r) = (palette[index * 4], palette[index * 4 + 1], palette[index * 4 + 2]);
                }

                var to = (y * width + x) * 4;
                (pixels[to], pixels[to + 1], pixels[to + 2], pixels[to + 3]) = (b, g, r, a);
            }
        }

        if ((bits < 32 || !anyAlpha) && hasMask)
        {
            for (var y = 0; y < height; y++)
            {
                var row = mask.Slice((height - 1 - y) * maskStride, maskStride);
                for (var x = 0; x < width; x++)
                {
                    var transparent = (row[x / 8] & (0x80 >> (x % 8))) != 0;
                    var at = (y * width + x) * 4;
                    if (transparent)
                    {
                        pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = 0;
                    }
                    else
                    {
                        pixels[at + 3] = 255;
                    }
                }
            }
        }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            Store(pixels, i, pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
        }

        return new WaylandCursorImage(pixels, width, height, hotX, hotY);
    }

    private static void Store(byte[] pixels, int at, byte b, byte g, byte r, byte a)
    {
        pixels[at] = (byte)(b * a / 255);
        pixels[at + 1] = (byte)(g * a / 255);
        pixels[at + 2] = (byte)(r * a / 255);
        pixels[at + 3] = a;
    }
}
