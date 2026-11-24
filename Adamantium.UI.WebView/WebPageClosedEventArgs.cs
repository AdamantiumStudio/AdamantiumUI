namespace Adamantium.UI.WebView;

/// <summary>The page closed itself: where it was and what it was called then - the view has let go of both by the time
/// this is told.</summary>
public sealed class WebPageClosedEventArgs : EventArgs
{
    public WebPageClosedEventArgs(Uri address, string title)
    {
        Address = address;
        Title = title ?? string.Empty;
    }

    /// <summary>The page's address; null for a page that closed itself before it was ever shown.</summary>
    public Uri Address { get; }

    /// <summary>The page's title; empty when it had none.</summary>
    public string Title { get; }
}
