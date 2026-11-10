namespace Adamantium.UI.WebView;

/// <summary>
/// A page asked to open another - a link to a new tab, <c>window.open</c>, a row of its menu - by the user's own doing;
/// one asked for without it is refused before it comes. The application shows <see cref="Page"/> in a
/// <see cref="WebView"/> of its own, through <see cref="WebView.NewPage"/>, and says so with <see cref="Handled"/>;
/// unhandled, the page that asked goes there itself.
/// </summary>
public sealed class WebNewWindowRequestedEventArgs : EventArgs
{
    internal WebNewWindowRequestedEventArgs(WebViewNewPage page, WebNewWindowKind kind)
    {
        Page = page;
        Kind = kind;
    }

    /// <summary>Where the new page goes.</summary>
    public string Address => Page.Address;

    /// <summary>How it was asked to open.</summary>
    public WebNewWindowKind Kind { get; }

    /// <summary>The new page, for the <see cref="WebView"/> that shows it. The application disposes it if it never
    /// shows it - a tab closed before it was looked at.</summary>
    public WebViewNewPage Page { get; }

    /// <summary>Set when the application shows the page.</summary>
    public bool Handled { get; set; }
}
