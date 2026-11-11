using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>Explorer, asked to select the file in its folder; the shell, to open it.</summary>
internal sealed class ExplorerFileManager : IFileManagerPlatform
{
    private const int NoAssociation = 1155;

    public bool OpenItem(string path)
    {
        try
        {
            using var opened = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == NoAssociation)
        {
            // Nothing opens its kind: the shell asks which application should, as a double click does.
            return OpenWith(path);
        }
        catch (Exception e) when (e is Win32Exception or IOException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool OpenWith(string path)
    {
        try
        {
            using var asking = Process.Start(new ProcessStartInfo("rundll32.exe")
            {
                ArgumentList = { "shell32.dll,OpenAs_RunDLL", path },
                UseShellExecute = false
            });
            return asking != null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or ArgumentException)
        {
            return false;
        }
    }

    public bool ShowItem(string path)
    {
        try
        {
            using var explorer = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"")
            {
                UseShellExecute = false
            });
            return explorer != null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or ArgumentException)
        {
            return false;
        }
    }
}
