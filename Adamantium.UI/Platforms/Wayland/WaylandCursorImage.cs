using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Imaging;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>A cursor picture from a file: a Windows .cur (or .ico), an Xcursor file of a cursor theme or a PNG held at
/// its top-left corner, as premultiplied BGRA with its hot spot.</summary>
internal sealed class WaylandCursorImage
{
    private const uint XcursorImageChunk = 0xFFFD0002;

    /// <summary>The logical size a cursor is shown at, as Windows does with every cursor file.</summary>
    public const int LogicalSize = 32;

    private WaylandCursorImage(byte[] pixels, int width, int height, int hotX, int hotY, int nominalSize = 0)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        HotX = hotX;
        HotY = hotY;
        NominalSize = nominalSize > 0 ? nominalSize : width;
    }

    public byte[] Pixels { get; }

    /// <summary>The size the picture stands for: an Xcursor picture may be larger than its nominal size, which is
    /// what is scaled to the size shown; any other picture's is its width.</summary>
    public int NominalSize { get; }

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
            return bytes switch
            {
                [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => FromPng(bytes, 0, 0),
                [(byte)'X', (byte)'c', (byte)'u', (byte)'r', ..] => FromXcursor(bytes, wantedSize),
                _ => FromCur(bytes, wantedSize)
            };
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

    // An Xcursor file: a header ("Xcur", its size, a version, the entry count), a table of entries (type, nominal size,
    // position), and at each image entry its own header (size, type, nominal size, version, width, height, hot spot,
    // delay) and the pixels as premultiplied ARGB words, little-endian. The first image of the nominal size nearest the
    // wanted one is taken: an animated cursor shows its first frame.
    private static WaylandCursorImage FromXcursor(byte[] file, int wantedSize)
    {
        var data = file.AsSpan();
        if (data.Length < 16)
        {
            return null;
        }

        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        var best = -1;
        var bestSize = 0;
        for (var i = 0; i < count && headerSize + (i + 1) * 12 <= data.Length; i++)
        {
            var entry = data.Slice(headerSize + i * 12, 12);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != XcursorImageChunk)
            {
                continue;
            }

            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var better = best < 0
                         || (size >= wantedSize ? bestSize < wantedSize || size < bestSize : bestSize < wantedSize && size > bestSize);
            if (better)
            {
                best = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                bestSize = size;
            }
        }

        if (best < 0 || best + 36 > data.Length)
        {
            return null;
        }

        var chunk = data[best..];
        var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[16..]);
        var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[20..]);
        var hotX = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[24..]);
        var hotY = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[28..]);
        var chunkHeader = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunk);
        if (width is <= 0 or > 1024 || height is <= 0 or > 1024 || chunkHeader + width * height * 4 > chunk.Length)
        {
            return null;
        }

        // Little-endian ARGB words are B, G, R, A bytes, already premultiplied: as they are.
        var pixels = chunk.Slice(chunkHeader, width * height * 4).ToArray();
        return new WaylandCursorImage(pixels, width, height, hotX, hotY, bestSize);
    }

    private static WaylandCursorImage FromPng(byte[] png, int hotX, int hotY)
    {
        var bitmap = BitmapLoader.Load(new MemoryStream(png));
        if (bitmap?.GetRgba8Pixels() is not { } rgba)
        {
            return null;
        }

        var width = (int)bitmap.Width;
        var height = (int)bitmap.Height;
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
