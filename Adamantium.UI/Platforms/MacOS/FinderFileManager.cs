using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The Finder, revealing the file in its folder.</summary>
internal sealed class FinderFileManager : IFileManagerPlatform
{
    public bool ShowItem(string path)
    {
        try
        {
            using var open = Process.Start(new ProcessStartInfo("open")
            {
                ArgumentList = { "-R", Path.GetFullPath(path) },
                UseShellExecute = false
            });
            return open != null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or ArgumentException)
        {
            return false;
        }
    }
}
