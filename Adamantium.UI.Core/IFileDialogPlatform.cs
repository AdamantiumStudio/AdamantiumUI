namespace Adamantium.UI.Core;

/// <summary>What a platform implements to answer <see cref="FileDialog"/>.</summary>
public interface IFileDialogPlatform
{
    /// <summary>Shows the platform's save dialog and returns the chosen path, or null when the user canceled. Modal to
    /// the window the user pressed from, and called on the UI thread.</summary>
    string Save(SaveFileRequest request);

    /// <summary>The same for opening: the path of an EXISTING file, or null when the user canceled.</summary>
    string Open(OpenFileRequest request);

    /// <summary>The same, letting several files be chosen at once: their paths, none when the user canceled. A platform
    /// that does not say otherwise asks for one.</summary>
    IReadOnlyList<string> OpenMany(OpenFileRequest request) => Open(request) is { } path ? [path] : [];

    /// <summary>The same for choosing a folder: the path of an EXISTING folder, or null when the user canceled.</summary>
    string OpenFolder(OpenFolderRequest request);
}
