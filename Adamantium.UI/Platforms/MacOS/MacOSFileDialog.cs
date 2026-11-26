using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.MacOS.AppKit;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The system's open and save dialogs. The calling thread waits for the answer, as it waits for a modal dialog
/// on Windows.</summary>
internal sealed class MacOSFileDialog(MacOSPlatform platform) : IFileDialogPlatform
{
    private const string IdentifierPrefix = "Adamantium.FileDialog.";

    public string Save(SaveFileRequest request)
    {
        var path = Ask(() =>
        {
            var panel = NSSavePanel.CreateSave();
            if (request?.FileName is { Length: > 0 } name)
                NSSavePanel.SetName(panel, name);
            if (Directory.Exists(request?.Folder))
                NSSavePanel.SetFolder(panel, Path.GetFullPath(request.Folder));
            return Run(panel, request?.Title, request?.Key, request?.FileTypes);
        });
        return path != null && request?.DefaultExtension is { Length: > 0 } extension && Path.GetExtension(path).Length == 0
            ? path + "." + extension
            : path;
    }

    public string Open(OpenFileRequest request) => Ask(() =>
        Run(NSSavePanel.CreateOpen(folders: false), request?.Title, request?.Key, request?.FileTypes));

    public string OpenFolder(OpenFolderRequest request) => Ask(() =>
        Run(NSSavePanel.CreateOpen(folders: true), request?.Title, request?.Key, null));

    private static string Run(IntPtr panel, string title, string key, IReadOnlyList<FileType> fileTypes)
    {
        if (!string.IsNullOrEmpty(title))
            NSSavePanel.SetMessage(panel, title);
        if (!string.IsNullOrEmpty(key))
            NSSavePanel.SetIdentifier(panel, IdentifierPrefix + key);
        using var types = new MacOSFileTypeList(panel, fileTypes);
        return NSSavePanel.Run(panel);
    }

    private string Ask(Func<string> ask)
    {
        string chosen = null;
        platform.RunOnMainThreadAndWait(() => chosen = ask());
        return chosen;
    }
}
