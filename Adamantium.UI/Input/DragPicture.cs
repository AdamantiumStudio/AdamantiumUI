using System.IO;
using Adamantium.Imaging;

namespace Adamantium.UI.Input;

// Identifies a dragged picture's real encoding and converts it for other applications; shared by all platform bridges.
internal static class DragPicture
{
    /// <summary>The encodings the world at large opens without a second thought. Anything outside this list is converted
    /// rather than handed over - a .tga or a .dds saved to disk is a file most people cannot use.</summary>
    public static bool IsWidelyReadable(byte[] picture) => Extension(picture) != null;

    /// <summary>The file extension the bytes deserve, or null when nothing common matches (a TGA, a DDS, an ICO...).</summary>
    public static string Extension(byte[] picture) => picture switch
    {
        [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => ".png",
        [0xFF, 0xD8, ..] => ".jpg",
        [(byte)'G', (byte)'I', (byte)'F', ..] => ".gif",
        [(byte)'B', (byte)'M', ..] => ".bmp",
        [0x49, 0x49, 0x2A, 0x00, ..] or [0x4D, 0x4D, 0x00, 0x2A, ..] => ".tif",
        _ => null,
    };

    /// <summary>Is this already the neutral encoding? Asked before offering anything as PNG, because MAKING one is the
    /// single most expensive thing in this whole subsystem - see <see cref="Convert"/>.</summary>
    public static bool IsPng(byte[] picture) => picture is [0x89, (byte)'P', (byte)'N', (byte)'G', ..];

    // Kept off the drag's hot path: PNG encoding takes seconds (~17 s at 960x540), so BMP is the default.
    public static byte[] Convert(byte[] encoded, ImageFileType format = ImageFileType.Bmp)
    {
        try
        {
            var bitmap = BitmapLoader.Load(new MemoryStream(encoded));
            if (bitmap == null) return null;
            var output = new MemoryStream();
            BitmapLoader.Save(bitmap, output, format);
            return output.Length > 0 ? output.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }
}
