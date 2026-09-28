using Adamantium.Core.TypeParsing;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Core.TypeParsers;

public class ImageSourceParser : ITypeParser<BitmapSource>
{
    public BitmapSource Parse(string value)
    {
        // Resolve relative paths against the application directory, not the working directory, which depends on how the
        // app was launched.
        var full = Path.IsPathRooted(value) ? value : Path.Combine(AppContext.BaseDirectory, value);
        // Through the cache: the same file shown twice is one decode and one texture, not two (see BitmapImageCache).
        return BitmapImageCache.GetOrCreate(full);
    }
}
