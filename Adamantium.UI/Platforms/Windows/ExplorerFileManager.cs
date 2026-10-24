using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Windows;

/// <summary>Explorer, asked to select the file in its folder.</summary>
internal sealed class ExplorerFileManager : IFileManagerPlatform
{
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
