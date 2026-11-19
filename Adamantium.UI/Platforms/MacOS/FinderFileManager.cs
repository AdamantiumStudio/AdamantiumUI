using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Adamantium.Core;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>The Finder, revealing the file in its folder or opening it, through <c>open</c>; a file from outside is
/// quarantined first.</summary>
internal sealed class FinderFileManager : IFileManagerPlatform
{
    private const string QuarantineAttribute = "com.apple.quarantine";
    private const int NoFollow = 0x0001;

    /// <summary>Downloads in the home folder: macOS keeps it there, named in English whatever the language the Finder
    /// shows it in.</summary>
    public string DownloadsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public bool ShowItem(string path) => Open(["-R", Path.GetFullPath(path)]);

    // A full path begins with '/', never read as an option.
    public bool OpenItem(string path) => Open([Path.GetFullPath(path)]);

    // macOS answers no such question; FileManager's shared list is all there is.
    public bool IsDangerousToOpen(string fileName) => false;

    // Quarantined, the file is checked by Gatekeeper and its warning shown when `open` hands it on.
    public bool OpenUntrustedItem(string path, IntPtr ownerWindow) =>
        !UnixUntrustedFiles.IsAProgramByContent(path) && MarkUntrusted(path) && OpenItem(path);

    // The quarantine mark browsers write: flags, the time in hex seconds, the application that saved it.
    public unsafe bool MarkUntrusted(string path)
    {
        if (!UnixUntrustedFiles.TakeAwayExecution(path))
        {
            return false;
        }

        var mark = Encoding.UTF8.GetBytes($"0081;{DateTimeOffset.UtcNow.ToUnixTimeSeconds():x8};Adamantium;");
        fixed (byte* value = mark)
        {
            return PosixInterop.SetMacExtendedAttribute(path, QuarantineAttribute, value, (nuint)mark.Length, 0,
                NoFollow) == 0;
        }
    }

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
