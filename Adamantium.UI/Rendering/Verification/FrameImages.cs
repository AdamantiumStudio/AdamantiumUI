using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;

namespace Adamantium.UI.Rendering.Verification;

internal static class FrameImages
{
    private const int ZoomPad = 16;
    private const int ZoomTarget = 640;
    private const int Gap = 4;

    /// <summary>Saves a frame as PNG. The PNG writer takes the bytes as RGBA whatever the format says, so a blue-first frame
    /// is turned round first.</summary>
    public static void Save(byte[] pixels, int width, int height, bool blueFirst, string path)
    {
        var rgba = pixels;
        if (blueFirst)
        {
            rgba = (byte[])pixels.Clone();
            for (var i = 0; i + 3 < rgba.Length; i += 4)
            {
                (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
            }
        }

        using var image = Image.New2D((uint)width, (uint)height, 1, SurfaceFormat.R8G8B8A8.UNorm);
        Marshal.Copy(rgba, 0, image.DataPointer, Math.Min(rgba.Length, (int)image.TotalSizeInBytes));
        image.Save(path, ImageFileType.Png);
    }

    /// <summary>The live frame dimmed, every differing pixel red, the box round them yellow.</summary>
    public static byte[] Diff(byte[] live, FrameComparison comparison, bool blueFirst)
    {
        var width = comparison.Width;
        var height = comparison.Height;
        var result = new byte[width * height * 4];

        for (var i = 0; i < width * height; i++)
        {
            var at = i * 4;
            if (comparison.DifferentAt[i])
            {
                Put(result, at, 255, 0, 0, blueFirst);
                continue;
            }

            result[at] = (byte)(live[at] / 4);
            result[at + 1] = (byte)(live[at + 1] / 4);
            result[at + 2] = (byte)(live[at + 2] / 4);
            result[at + 3] = 255;
        }

        if (!comparison.HasDifference)
        {
            return result;
        }

        for (var x = comparison.Left; x <= comparison.Right; x++)
        {
            Put(result, (comparison.Top * width + x) * 4, 255, 220, 0, blueFirst);
            Put(result, (comparison.Bottom * width + x) * 4, 255, 220, 0, blueFirst);
        }

        for (var y = comparison.Top; y <= comparison.Bottom; y++)
        {
            Put(result, (y * width + comparison.Left) * 4, 255, 220, 0, blueFirst);
            Put(result, (y * width + comparison.Right) * 4, 255, 220, 0, blueFirst);
        }

        return result;
    }

    /// <summary>Live | walk | difference, cut round the difference and blown up so a few pixels can be seen.</summary>
    public static byte[] Zoom(byte[] live, byte[] walk, byte[] diff, FrameComparison comparison, out int zoomWidth, out int zoomHeight)
    {
        var left = Math.Max(0, comparison.Left - ZoomPad);
        var top = Math.Max(0, comparison.Top - ZoomPad);
        var right = Math.Min(comparison.Width - 1, comparison.Right + ZoomPad);
        var bottom = Math.Min(comparison.Height - 1, comparison.Bottom + ZoomPad);
        var cutWidth = right - left + 1;
        var cutHeight = bottom - top + 1;
        var scale = Math.Clamp(ZoomTarget / Math.Max(cutWidth, cutHeight), 1, 8);

        var panelWidth = cutWidth * scale;
        zoomWidth = panelWidth * 3 + Gap * 2;
        zoomHeight = cutHeight * scale;
        var result = new byte[zoomWidth * zoomHeight * 4];

        for (var i = 0; i < result.Length; i += 4)
        {
            result[i] = 64;
            result[i + 1] = 64;
            result[i + 2] = 64;
            result[i + 3] = 255;
        }

        Blit(live, comparison.Width, left, top, cutWidth, cutHeight, scale, result, zoomWidth, 0);
        Blit(walk, comparison.Width, left, top, cutWidth, cutHeight, scale, result, zoomWidth, panelWidth + Gap);
        Blit(diff, comparison.Width, left, top, cutWidth, cutHeight, scale, result, zoomWidth, (panelWidth + Gap) * 2);
        return result;
    }

    private static void Blit(byte[] source, int sourceWidth, int left, int top, int width, int height, int scale,
        byte[] target, int targetWidth, int offsetX)
    {
        for (var y = 0; y < height * scale; y++)
        {
            for (var x = 0; x < width * scale; x++)
            {
                var from = ((top + y / scale) * sourceWidth + left + x / scale) * 4;
                var to = (y * targetWidth + offsetX + x) * 4;
                if (from + 3 >= source.Length || to + 3 >= target.Length)
                {
                    continue;
                }

                target[to] = source[from];
                target[to + 1] = source[from + 1];
                target[to + 2] = source[from + 2];
                target[to + 3] = 255;
            }
        }
    }

    private static void Put(byte[] pixels, int at, byte r, byte g, byte b, bool blueFirst)
    {
        if (at < 0 || at + 3 >= pixels.Length)
        {
            return;
        }

        pixels[at] = blueFirst ? b : r;
        pixels[at + 1] = g;
        pixels[at + 2] = blueFirst ? r : b;
        pixels[at + 3] = 255;
    }
}
