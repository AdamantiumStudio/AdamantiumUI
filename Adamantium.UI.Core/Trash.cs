namespace Adamantium.UI.Core;

/// <summary>Moves files and folders to the desktop's trash, where the user can take them back from, as deleting in a
/// file manager does.</summary>
public static class Trash
{
    /// <summary>The platform that does it, registered once at startup; null where none is written.</summary>
    public static ITrashPlatform Platform { get; set; }

    /// <summary>Whether this platform has a trash at all - worth asking before offering to delete into it.</summary>
    public static bool IsAvailable => Platform != null;

    /// <summary>Moves the file or folder at <paramref name="path"/> to the trash; false when it is not there or has no
    /// trash to go to - it is left where it is then.</summary>
    public static bool MoveToTrash(string path) =>
        Platform != null && !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)) &&
        Platform.MoveToTrash(Path.GetFullPath(path));
}
