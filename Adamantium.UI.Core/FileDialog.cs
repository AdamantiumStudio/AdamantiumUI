namespace Adamantium.UI.Core;

/// <summary>Asks the user for a file through the platform's native dialog; <see cref="SaveFileRequest"/> carries the
/// title, suggested name and file types.</summary>
public static class FileDialog
{
    /// <summary>The platform that answers, registered once at startup. Null where none is written yet - ask
    /// <see cref="IsAvailable"/> rather than reading a canceled answer as a refusal.</summary>
    public static IFileDialogPlatform Platform { get; set; }

    /// <summary>Whether this platform can ask at all. Worth checking BEFORE offering the action: without it, an
    /// application cannot tell a user who pressed Cancel from a platform that never opened anything, and would sit
    /// there having silently done nothing.</summary>
    public static bool IsAvailable => Platform != null;

    /// <summary>Asks where to save, and returns the full path the user chose - or null if they chose nothing. Null is
    /// the only refusal: nothing is written, and there is no error to report, because canceling is not one.</summary>
    public static string Save(SaveFileRequest request) => Platform?.Save(request);

    /// <summary>Asks which file to open, and returns its full path - or null if the user chose nothing.</summary>
    public static string Open(OpenFileRequest request) => Platform?.Open(request);
}
