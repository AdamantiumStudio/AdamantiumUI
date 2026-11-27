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

    public IReadOnlyList<string> OpenMany(OpenFileRequest request) => Ask(() =>
    {
        var panel = NSSavePanel.CreateOpen(folders: false);
        NSSavePanel.SetAllowsMany(panel, true);
        return Run(panel, request?.Title, request?.Key, request?.FileTypes, NSSavePanel.RunForMany);
    }) ?? [];

    public string OpenFolder(OpenFolderRequest request) => Ask(() =>
        Run(NSSavePanel.CreateOpen(folders: true), request?.Title, request?.Key, null));

    private static string Run(IntPtr panel, string title, string key, IReadOnlyList<FileType> fileTypes) =>
        Run(panel, title, key, fileTypes, NSSavePanel.Run);

    private static T Run<T>(IntPtr panel, string title, string key, IReadOnlyList<FileType> fileTypes,
        Func<IntPtr, T> run)
    {
        if (!string.IsNullOrEmpty(title))
            NSSavePanel.SetMessage(panel, title);
        if (!string.IsNullOrEmpty(key))
            NSSavePanel.SetIdentifier(panel, IdentifierPrefix + key);
        using var types = new MacOSFileTypeList(panel, fileTypes);
        return run(panel);
    }

    private T Ask<T>(Func<T> ask)
    {
        T chosen = default;
        platform.RunOnMainThreadAndWait(() => chosen = ask());
        return chosen;
    }
}
