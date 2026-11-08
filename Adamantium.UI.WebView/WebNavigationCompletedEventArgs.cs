namespace Adamantium.UI.WebView;

/// <summary>The page in the main frame has loaded, or failed to: <see cref="Error"/> says why it did not.</summary>
public sealed class WebNavigationCompletedEventArgs : EventArgs
{
    public WebNavigationCompletedEventArgs(string address, WebViewLoadError error)
    {
        Address = address;
        Error = error;
    }

    /// <summary>The page's address.</summary>
    public string Address { get; }

    /// <summary>Why the page did not load; null when it did.</summary>
    public WebViewLoadError Error { get; }

    /// <summary>Whether the page loaded.</summary>
    public bool IsSuccess => Error == null;
}
