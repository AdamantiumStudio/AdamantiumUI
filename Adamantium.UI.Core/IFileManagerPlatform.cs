namespace Adamantium.UI.Core;

/// <summary>A platform's way to <see cref="FileManager"/>: the desktop's file manager.</summary>
public interface IFileManagerPlatform
{
    /// <summary>Shows the file or folder at <paramref name="path"/> in the file manager; false when it could not.</summary>
    bool ShowItem(string path);
}
