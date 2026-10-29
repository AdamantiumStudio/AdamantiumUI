using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The Trash, by asking the Finder to delete the item, as dragging it to the Trash does - the Finder can put
/// it back. The first time macOS asks the user whether the application may tell the Finder what to do.</summary>
internal sealed class FinderTrash : ITrashPlatform
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public bool MoveToTrash(string path)
    {
        try
        {
            using var script = Process.Start(new ProcessStartInfo("osascript")
            {
                ArgumentList =
                {
                    "-e", "on run arguments",
                    "-e", "tell application \"Finder\" to delete (POSIX file (item 1 of arguments) as alias)",
                    "-e", "end run",
                    path
                },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (script == null)
            {
                return false;
            }

            if (!script.WaitForExit(Patience))
            {
                script.Kill();
                return false;
            }

            return script.ExitCode == 0 && !File.Exists(path) && !Directory.Exists(path);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or ArgumentException)
        {
            return false;
        }
    }
}
