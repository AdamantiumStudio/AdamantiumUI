namespace Adamantium.UI.Core;

/// <summary>Shows a file in the desktop's own file manager: its folder open, the file picked out, as "Show in folder"
/// does.</summary>
public static class FileManager
{
    /// <summary>The platform that shows it, registered once at startup; null where none is written.</summary>
    public static IFileManagerPlatform Platform { get; set; }

    /// <summary>Whether this platform can show a file at all - worth asking before offering the action.</summary>
    public static bool IsAvailable => Platform != null;

    /// <summary>Shows the file or folder at <paramref name="path"/>; false when it is not there or nothing could show it.
    /// </summary>
    public static bool ShowItem(string path) =>
        Platform != null && !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)) &&
        Platform.ShowItem(path);
}
