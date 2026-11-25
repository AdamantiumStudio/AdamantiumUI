using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Imaging;
using Adamantium.Imaging.Png;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.Win32.Printing;
using Adamantium.Win32.Printing.Xps;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>Bitmaps turned into the PNG images of an XPS package, each once for the whole job: by the pixel buffer it
/// holds and the tint and adjustment its colors are changed by - a picture on every page is one part of the package.
/// Released with the job.</summary>
internal sealed class XpsPictures(IXpsOMObjectFactory factory) : IDisposable
{
    private readonly Dictionary<Key, Picture> _pictures = [];

    private readonly record struct Key(byte[] Pixels, Color Tint, Vector4<float> Adjust);

    /// <summary>An image in the package, and its size in pixels.</summary>
    public sealed record Picture(IXpsOMImageResource Resource, int Width, int Height);

    /// <summary>The image of <paramref name="source"/> - its <paramref name="frame"/>, -1 for the picture as it is -
    /// with <paramref name="adjust"/> then <paramref name="tint"/> in its pixels; null when its pixels cannot be had.
    /// </summary>
    public Picture Of(ImageSource source, int frame, Color tint, Vector4<float> adjust)
    {
        if (PicturePixels.BitmapOf(source, frame) is not { } bitmap)
        {
            return null;
        }

        var key = new Key(bitmap.PixelBytes, tint, adjust);
        if (_pictures.TryGetValue(key, out var picture))
        {
            return picture;
        }

        var rgba = PicturePixels.RgbaOf(bitmap);
        if (rgba == null)
        {
            return null;
        }

        if (tint != Colors.White || adjust != default)
        {
            PicturePixels.Recolor(rgba, tint, adjust);
        }

        var width = (int)bitmap.PixelWidth;
        var height = (int)bitmap.PixelHeight;
        var png = new MemoryStream();
        PngEncoder.Encode(new FrameBitmap((uint)width, (uint)height, SurfaceFormat.R8G8B8A8.UNorm, [rgba]), png);
        png.Position = 0;
        var name = "/Resources/Images/" + (_pictures.Count + 1).ToString(CultureInfo.InvariantCulture) + ".png";
        XpsGeometryPath.Check(factory.CreatePartUri(name, out var part));
        XpsGeometryPath.Check(factory.CreateImageResource(new ComStream(png), XpsImageType.Png, part,
            out var resource));
        picture = new Picture(resource, width, height);
        _pictures[key] = picture;
        return picture;
    }

    public void Dispose()
    {
        foreach (var picture in _pictures.Values)
        {
            Marshal.ReleaseComObject(picture.Resource);
        }

        _pictures.Clear();
    }
}
