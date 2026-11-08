namespace Adamantium.UI.WebView;

/// <summary>The page is about to go to <see cref="Address"/> in its main frame; <see cref="Cancel"/> keeps it where it
/// is.</summary>
public sealed class WebNavigationStartingEventArgs : EventArgs
{
    public WebNavigationStartingEventArgs(string address, bool userGesture, bool isRedirect)
    {
        Address = address;
        UserGesture = userGesture;
        IsRedirect = isRedirect;
    }

    /// <summary>Where the page is going.</summary>
    public string Address { get; }

    /// <summary>Whether the user asked for it - a click on a link - rather than a script.</summary>
    public bool UserGesture { get; }

    /// <summary>Whether a server sent the page on from where it was going.</summary>
    public bool IsRedirect { get; }

    /// <summary>Set to keep the page where it is.</summary>
    public bool Cancel { get; set; }
}
