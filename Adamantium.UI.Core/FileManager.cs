namespace Adamantium.UI.Core;

/// <summary>Shows a file in the desktop's own file manager: its folder open, the file picked out, as "Show in folder"
/// does; or opens it with the application the desktop opens it with.</summary>
public static class FileManager
{
    /// <summary>The platform that shows it, registered once at startup; null where none is written.</summary>
    public static IFileManagerPlatform Platform { get; set; }

    /// <summary>Whether this platform can show a file at all - worth asking before offering the action.</summary>
    public static bool IsAvailable => Platform != null;

    /// <summary>Shows the file or folder at <paramref name="path"/>; false when it is not there or nothing could show it.
    /// </summary>
    public static bool ShowItem(string path) =>
        Platform != null && !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)) &&
        Platform.ShowItem(path);

    /// <summary>Opens the file or folder at <paramref name="path"/> as a double click in the file manager would; false
    /// when it is not there or nothing could be started to open it - the opener started (xdg-open, open) may still find
    /// no application.</summary>
    public static bool OpenItem(string path) =>
        Platform != null && !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)) &&
        Platform.OpenItem(Path.GetFullPath(path));

    /// <summary>Whether a file named <paramref name="fileName"/> runs code when opened - a program, script, shortcut,
    /// installer - so it is only to be saved, never opened from here; true where no platform can tell.</summary>
    public static bool IsDangerousToOpen(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        if (OperatingSystem.IsWindows())
        {
            // Windows opens "setup.exe." as setup.exe, and a stream in a name (setup.exe::$DATA) hides its kind.
            name = name.TrimEnd('.', ' ');
            if (name.Contains(':'))
            {
                return true;
            }
        }

        return Platform == null || name.Length == 0 || Dangerous.Contains(Path.GetExtension(name)) ||
               Platform.IsDangerousToOpen(name);
    }

    /// <summary>Opens a file that came from outside - an attachment, a download - after the desktop's own checks: marked
    /// as from the internet, the system's policy asked, its warning shown over <paramref name="owner"/>. False when it is
    /// not there, is a link, is <see cref="IsDangerousToOpen">dangerous to open</see>, is a program by its content or was
    /// not opened. Called on the UI thread.</summary>
    public static bool OpenUntrustedItem(string path, IWindow owner) =>
        FullPathOfAFile(path) is { } full && !IsDangerousToOpen(full) && !IsAProgramByContent(full) &&
        Platform.OpenUntrustedItem(full, owner?.Handle ?? IntPtr.Zero);

    /// <summary>Marks a file saved from outside, at <paramref name="path"/>, as browsers mark downloads, so the system
    /// warns before it is opened; false when it is not there, is a link or could not be marked. Called on the UI thread.
    /// </summary>
    public static bool MarkUntrusted(string path) =>
        Platform != null && FullPathOfAFile(path) is { } full && Platform.MarkUntrusted(full);

    // The path as the system will open it (Windows drops dots and spaces at the end), for a file that is no link.
    private static string FullPathOfAFile(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(Path.GetFullPath(path));
            return file.Exists && file.LinkTarget == null ? file.FullName : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or
                                      NotSupportedException)
        {
            return null;
        }
    }

    // Linux and macOS pick the application by content when the name says nothing they know: a Windows program with no
    // extension goes to Wine, which needs no right to run. Windows goes by the extension alone.
    private static bool IsAProgramByContent(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

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

            return start.IndexOf("[Desktop Entry]"u8) >= 0 || start.IndexOf("[KDE Desktop Entry]"u8) >= 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private const int ContentChecked = 1024;

    // Windows and Linux programs, scripts, Mach-O (both byte orders, 32 and 64 bits) and fat binaries.
    private static readonly byte[][] ProgramStarts =
    [
        "MZ"u8.ToArray(), [0x7F, (byte)'E', (byte)'L', (byte)'F'], "#!"u8.ToArray(), [0xCA, 0xFE, 0xBA, 0xBE],
        [0xFE, 0xED, 0xFA, 0xCE], [0xFE, 0xED, 0xFA, 0xCF], [0xCE, 0xFA, 0xED, 0xFE], [0xCF, 0xFA, 0xED, 0xFE]
    ];

    // Kinds that run code when opened wherever an interpreter for them is installed, whatever the system says of them -
    // Windows' own list misses Python, Java and more, Linux and macOS keep none. Disk images mount past the system's
    // checks; some kinds reach out to a server (.rdp, .searchconnector-ms).
    private static readonly HashSet<string> Dangerous = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".mst", ".msix", ".appx", ".appinstaller", ".application",
        ".appref-ms", ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".ps1xml", ".ps2", ".ps2xml", ".psc1", ".psc2", ".msh",
        ".msh1", ".msh2", ".mshxml", ".vb", ".vbs", ".vbe", ".js", ".jse", ".ws", ".wsf", ".wsh", ".wsc", ".sct", ".hta",
        ".chm", ".hlp", ".msc", ".diagcab", ".xll", ".xbap", ".rdp", ".searchconnector-ms", ".theme", ".themepack",
        ".desktopthemepackfile", ".accde", ".ade", ".adp", ".mde", ".website", ".shb", ".shs", ".ins", ".isp", ".job",
        ".lnk", ".url", ".reg", ".inf", ".gadget", ".settingcontent-ms", ".library-ms", ".search-ms", ".scf",
        ".py", ".pyw", ".pyc", ".pyo", ".pyz", ".pyzw", ".jar", ".jnlp", ".rb", ".rbw", ".pl", ".pm", ".php", ".tcl",
        ".lua", ".sh", ".bash", ".zsh", ".csh", ".ksh", ".fish", ".run", ".bin", ".desktop", ".appimage", ".deb", ".rpm",
        ".snap", ".flatpak", ".flatpakref", ".flatpakrepo", ".app", ".command", ".tool", ".terminal", ".pkg", ".mpkg",
        ".scpt", ".scptd", ".applescript", ".workflow", ".action", ".shortcut", ".webloc", ".inetloc", ".fileloc",
        ".prefpane", ".kext", ".osax", ".xpc", ".plugin", ".bundle", ".mobileconfig", ".dll", ".dylib", ".so",
        ".iso", ".img", ".vhd", ".vhdx", ".dmg"
    };

    /// <summary>The user's downloads folder: where the desktop keeps it, else "Downloads" in the home folder. A full
    /// path; the folder may not be there yet.</summary>
    public static string DownloadsFolder =>
        Platform?.DownloadsFolder is { Length: > 0 } folder && Path.IsPathFullyQualified(folder)
            ? folder
            : Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
}
