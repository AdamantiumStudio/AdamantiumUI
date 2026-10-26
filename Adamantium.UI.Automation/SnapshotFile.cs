using System.Runtime.InteropServices;
using Adamantium.Imaging;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Automation;

internal static class SnapshotFile
{
    public static void Save(BitmapSource bitmap, string path)
    {
        // The pixels said to be in their own layout: the encoders read B8G8R8A8 as such.
        var pixels = bitmap.PixelBytes;
        using var image = Image.New2D(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.SurfaceLayout);
        Marshal.Copy(pixels, 0, image.DataPointer, pixels.Length);
        image.Save(path, ImageFileType.Png);
    }
}
