using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The Finder, revealing the file in its folder or opening it, through <c>open</c>.</summary>
internal sealed class FinderFileManager : IFileManagerPlatform
{
    public bool ShowItem(string path) => Open(["-R", Path.GetFullPath(path)]);

    // A full path begins with '/', never read as an option.
    public bool OpenItem(string path) => Open([Path.GetFullPath(path)]);

    private static bool Open(string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo("open") { UseShellExecute = false };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var open = Process.Start(start);
            return open != null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or ArgumentException)
        {
            return false;
        }
    }
}
