namespace Adamantium.UI.WebView;

/// <summary>What interrupted a <see cref="WebDownload"/>.</summary>
public enum WebDownloadInterruption
{
    /// <summary>Nothing: it is not interrupted.</summary>
    None,

    /// <summary>The file: no room, no access, a name too long, a folder that cannot be made.</summary>
    File,

    /// <summary>The connection: it failed, timed out, or was lost.</summary>
    Network,

    /// <summary>The server: it failed, refused, or sent less than it said.</summary>
    Server,

    /// <summary>The user, or the application shutting down.</summary>
    User,

    /// <summary>The browser stopped while it came in.</summary>
    Crash,

    /// <summary>Anything else Chromium names.</summary>
    Other
}
