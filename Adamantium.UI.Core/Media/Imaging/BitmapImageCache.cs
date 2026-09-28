using System.Collections.Generic;

namespace Adamantium.UI.Core.Media.Imaging;

/// <summary>One decoded image per file, shared by everything showing it. Entries are weak, so an image unused everywhere
/// is decoded again next time; playback state lives in the controls.</summary>
public static class BitmapImageCache
{
    private static readonly Dictionary<string, WeakReference<BitmapImage>> Entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Master switch - off makes every request build its own image, as before.</summary>
    public static bool Enabled { get; set; } = true;

    public static BitmapImage GetOrCreate(string fullPath)
    {
        if (!Enabled) return new BitmapImage(new Uri(fullPath));

        lock (Entries)
        {
            if (Entries.TryGetValue(fullPath, out var entry) && entry.TryGetTarget(out var cached) && !cached.IsDisposed)
            {
                return cached;
            }

            var image = new BitmapImage(new Uri(fullPath));
            Entries[fullPath] = new WeakReference<BitmapImage>(image);
            return image;
        }
    }
}
