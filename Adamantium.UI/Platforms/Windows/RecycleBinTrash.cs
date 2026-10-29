using System;
using System.IO;
using Adamantium.UI.Core;
using Adamantium.Win32;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>The Recycle Bin, through the shell's own delete: no question but the one the shell asks before deleting for
/// good what the Recycle Bin cannot take - a removable drive's file, one too big for it, one where it is turned off;
/// answered no, the item stays. A network folder has no Recycle Bin, so nothing there goes.</summary>
internal sealed class RecycleBinTrash : ITrashPlatform
{
    public bool MoveToTrash(string path)
    {
        try
        {
            if (IsOnNetwork(path))
            {
                return false;
            }

            var operation = new SHFILEOPSTRUCT
            {
                Function = ShellFileOperation.Delete,
                From = path + '\0' + '\0',
                Flags = ShellFileOperation.AllowUndo | ShellFileOperation.NoConfirmation | ShellFileOperation.Silent
                        | ShellFileOperation.WantNukeWarning
            };
            return Win32Interop.SHFileOperation(ref operation) == 0 && operation.AnyOperationsAborted == 0
                   && !File.Exists(path) && !Directory.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsOnNetwork(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(path)).DriveType == DriveType.Network;
}
