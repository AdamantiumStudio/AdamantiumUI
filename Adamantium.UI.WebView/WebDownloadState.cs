namespace Adamantium.UI.WebView;

/// <summary>Where a <see cref="WebDownload"/> is.</summary>
public enum WebDownloadState
{
    /// <summary>Asked of the application, which has not said where to save it yet.</summary>
    Asked,

    /// <summary>Coming in.</summary>
    InProgress,

    /// <summary>Paused; <see cref="WebDownload.Resume"/> goes on.</summary>
    Paused,

    /// <summary>All of it saved at <see cref="WebDownload.FilePath"/>.</summary>
    Completed,

    /// <summary>Cancelled, or refused when asked; nothing is kept.</summary>
    Cancelled,

    /// <summary>Stopped by what went wrong - the connection, the server, the disk (<see cref="WebDownload.InterruptReason"/>):
    /// not finished, it may go on - by itself, or <see cref="WebDownload.Resume"/> - or be cancelled; until then the part
    /// come in stays beside <see cref="WebDownload.FilePath"/>.</summary>
    Interrupted
}
