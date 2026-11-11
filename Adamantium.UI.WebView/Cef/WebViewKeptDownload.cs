namespace Adamantium.UI.WebView.Cef;

/// <summary>A download as the profile's file keeps it; what a record leaves out reads as not known.</summary>
internal sealed record WebViewKeptDownload(
    string Address = null,
    string OriginalAddress = null,
    string PageAddress = null,
    string Name = null,
    string MimeType = null,
    string FilePath = null,
    string PartialPath = null,
    long TotalBytes = -1,
    long ReceivedBytes = 0,
    WebDownloadState State = WebDownloadState.Interrupted,
    WebDownloadInterruption InterruptReason = WebDownloadInterruption.None,
    DateTimeOffset? StartTime = null,
    DateTimeOffset? EndTime = null);
