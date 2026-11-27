using Adamantium.UI.Core;

namespace Adamantium.UITests;

/// <summary>A platform's file dialog of an application's own that knows one file at a time: it says nothing of several.
/// </summary>
internal sealed class OneFileDialog(string chosen) : IFileDialogPlatform
{
    public string Save(SaveFileRequest request) => chosen;

    public string Open(OpenFileRequest request) => chosen;

    public string OpenFolder(OpenFolderRequest request) => chosen;
}
