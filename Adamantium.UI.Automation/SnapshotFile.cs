using System.Runtime.InteropServices;
using Adamantium.Imaging;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Automation;

internal static class SnapshotFile
{
    public static void Save(BitmapSource bitmap, string path)
    {
        var pixels = (byte[])bitmap.PixelBytes.Clone();
        if (bitmap.SurfaceLayout == SurfaceFormat.B8G8R8A8.UNorm)
        {
            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }
        }

        using var image = Image.New2D(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.SurfaceLayout);
        Marshal.Copy(pixels, 0, image.DataPointer, pixels.Length);
        image.Save(path, ImageFileType.Png);
    }
}
