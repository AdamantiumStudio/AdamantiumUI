using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            Prepare(panel, request?.Title, request?.Key, request?.FileTypes);
            if (request?.FileName is { Length: > 0 } name)
                NSSavePanel.SetName(panel, name);
            if (Directory.Exists(request?.Folder))
                NSSavePanel.SetFolder(panel, Path.GetFullPath(request.Folder));
            return NSSavePanel.Run(panel);
        });
        return path != null && request?.DefaultExtension is { Length: > 0 } extension && Path.GetExtension(path).Length == 0
            ? path + "." + extension
            : path;
    }

    public string Open(OpenFileRequest request) => Ask(() =>
    {
        var panel = NSSavePanel.CreateOpen(folders: false);
        Prepare(panel, request?.Title, request?.Key, request?.FileTypes);
        return NSSavePanel.Run(panel);
    });

    public string OpenFolder(OpenFolderRequest request) => Ask(() =>
    {
        var panel = NSSavePanel.CreateOpen(folders: true);
        Prepare(panel, request?.Title, request?.Key, null);
        return NSSavePanel.Run(panel);
    });

    private static void Prepare(IntPtr panel, string title, string key, IReadOnlyList<FileType> fileTypes)
    {
        if (!string.IsNullOrEmpty(title))
            NSSavePanel.SetMessage(panel, title);
        if (!string.IsNullOrEmpty(key))
            NSSavePanel.SetIdentifier(panel, IdentifierPrefix + key);
        // All the kinds at once: the system's dialog has no list to choose one from.
        if (fileTypes is { Count: > 0 } && fileTypes.All(type => type.Extensions.Count > 0))
            NSSavePanel.SetExtensions(panel, fileTypes.SelectMany(type => type.Extensions).Distinct());
    }

    private string Ask(Func<string> ask)
    {
        string chosen = null;
        platform.RunOnMainThreadAndWait(() => chosen = ask());
        return chosen;
    }
}
