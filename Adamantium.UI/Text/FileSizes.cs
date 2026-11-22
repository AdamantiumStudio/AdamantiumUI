using Adamantium.Localization;
using Adamantium.UI.Themes.Localization;

namespace Adamantium.UI.Text;

/// <summary>A file's size as a person reads it, in the language's units.</summary>
public static class FileSizes
{
    /// <summary>"2.4 MB", "42 KB", "100 B"; empty for a size below zero.</summary>
    public static string Format(long bytes)
    {
        var culture = Languages.Culture;
        return bytes switch
        {
            < 0 => string.Empty,
            >= 1L << 30 => $"{(bytes / (double)(1L << 30)).ToString("0.0", culture)} {FileSizeStrings.Gigabytes}",
            >= 1L << 20 => $"{(bytes / (double)(1L << 20)).ToString("0.0", culture)} {FileSizeStrings.Megabytes}",
            >= 1L << 10 => $"{(bytes / (double)(1L << 10)).ToString("0", culture)} {FileSizeStrings.Kilobytes}",
            _ => $"{bytes.ToString(culture)} {FileSizeStrings.Bytes}"
        };
    }
}
