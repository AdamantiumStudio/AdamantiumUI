using System.Collections.Generic;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Tests;

/// <summary>A platform's file dialog a test answers for: the file it was made with, and what it was offered.</summary>
internal sealed class ChoosingFileDialog : IFileDialogPlatform
{
    private readonly string chosen;

    public ChoosingFileDialog(string chosen)
    {
        this.chosen = chosen;
    }

    /// <summary>The kinds of file the last request offered.</summary>
    public IReadOnlyList<FileType> Offered { get; private set; }

    public string Save(SaveFileRequest request)
    {
        Offered = request.FileTypes;
        return chosen;
    }

    public string Open(OpenFileRequest request)
    {
        Offered = request.FileTypes;
        return chosen;
    }

    public string OpenFolder(OpenFolderRequest request) => chosen;
}
