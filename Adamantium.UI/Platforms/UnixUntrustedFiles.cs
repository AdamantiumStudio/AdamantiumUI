using System;
using System.IO;

namespace Adamantium.UI.Platforms;

/// <summary>Files from outside on Linux and macOS: they never keep the right to run, and one that is a program by its
/// content is not opened, whatever its name.</summary>
internal static class UnixUntrustedFiles
{
    private const UnixFileMode Execution = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
    private const int ContentChecked = 1024;

    // Windows and Linux programs, scripts, Mach-O (both byte orders, 32 and 64 bits), fat binaries, Windows shortcuts,
    // launchers written as config files, Debian and RPM packages.
    private static readonly byte[][] ProgramStarts =
    [
        "MZ"u8.ToArray(), [0x7F, (byte)'E', (byte)'L', (byte)'F'], "#!"u8.ToArray(), [0xCA, 0xFE, 0xBA, 0xBE],
        [0xFE, 0xED, 0xFA, 0xCE], [0xFE, 0xED, 0xFA, 0xCF], [0xCE, 0xFA, 0xED, 0xFE], [0xCF, 0xFA, 0xED, 0xFE],
        [0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00], "# Config File"u8.ToArray(), "# KDE Config File"u8.ToArray(),
        "[Desktop Action"u8.ToArray(), "!<arch>\ndebian-binary"u8.ToArray(), [0xED, 0xAB, 0xEE, 0xDB]
    ];

    // Launchers, internet shortcuts and app references the desktop finds anywhere near the start.
    private static readonly byte[][] ProgramSections =
    [
        "[Desktop Entry]"u8.ToArray(), "[KDE Desktop Entry]"u8.ToArray(), "[InternetShortcut]"u8.ToArray(),
        "[Flatpak Ref]"u8.ToArray(), "[Flatpak Repo]"u8.ToArray()
    ];

    /// <summary>Opens a file from outside with <paramref name="open"/>: marked first by <paramref name="mark"/>, so a
    /// refused one is left marked too, and not when it is a program by its content.</summary>
    public static bool Open(string path, Func<string, bool> mark, Func<string, bool> open) =>
        mark(path) && !IsAProgramByContent(path) && open(path);

    /// <summary>Takes the right to run away from the file at <paramref name="path"/>; false when it could not.</summary>
    public static bool TakeAwayExecution(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & Execution) != 0)
            {
                File.SetUnixFileMode(path, mode & ~Execution);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether the file at <paramref name="path"/> starts as a program does - what the desktop goes by when the
    /// name says nothing it knows, handing a Windows program to Wine, which needs no right to run; true when unread.
    /// </summary>
    public static bool IsAProgramByContent(string path)
    {
        try
        {
            Span<byte> start = stackalloc byte[ContentChecked];
            using var file = File.OpenRead(path);
            start = start[..file.ReadAtLeast(start, start.Length, throwOnEndOfStream: false)];
            foreach (var magic in ProgramStarts)
            {
                if (start.StartsWith(magic))
                {
                    return true;
                }
            }

            foreach (var section in ProgramSections)
            {
                if (start.IndexOf(section) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or
                                      NotSupportedException)
        {
            return true;
        }
    }
}
