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

    private const string ShellFolders = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    private const string DownloadsId = "{374DE290-123F-4565-9164-39C4925E467B}";

    /// <summary>The Downloads folder as the shell records it for the user - moved, if they moved it - under its known
    /// folder id in User Shell Folders, its variables expanded once.</summary>
    public string DownloadsFolder
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                using var folders = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ShellFolders);
                return folders?.GetValue(DownloadsId, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames)
                    is string folder
                    ? Environment.ExpandEnvironmentVariables(folder)
                    : null;
            }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

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
