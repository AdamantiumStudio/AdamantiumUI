namespace Adamantium.UI.Core;

/// <summary>A platform's way to <see cref="FileManager"/>: the desktop's file manager.</summary>
public interface IFileManagerPlatform
{
    /// <summary>Shows the file or folder at <paramref name="path"/> in the file manager; false when it could not.</summary>
    bool ShowItem(string path);

    /// <summary>Opens the file or folder at <paramref name="path"/>, a full path, with the application the desktop opens
    /// it with - asking which, where the desktop does; false when nothing could be started to open it. Where the
    /// desktop's own opener is started, its answer is not waited for.</summary>
    bool OpenItem(string path);

    /// <summary>Whether the desktop takes a file named <paramref name="fileName"/> for one that runs code when opened: a
    /// program, script, shortcut, installer.</summary>
    bool IsDangerousToOpen(string fileName);

    /// <summary>Opens a file that came from outside, at <paramref name="path"/>, a full path, after the desktop's own
    /// checks, its warnings shown over <paramref name="ownerWindow"/>; false when it was not opened.</summary>
    bool OpenUntrustedItem(string path, IntPtr ownerWindow);

    /// <summary>Marks the file at <paramref name="path"/>, a full path, as come from outside, as browsers mark downloads;
    /// false when it could not be marked.</summary>
    bool MarkUntrusted(string path);

    /// <summary>The user's downloads folder, a full path, where the desktop says it is - moved or named in the user's
    /// language; null when the desktop does not say. Anything but a full path is taken as not said.</summary>
    string DownloadsFolder { get; }
}
