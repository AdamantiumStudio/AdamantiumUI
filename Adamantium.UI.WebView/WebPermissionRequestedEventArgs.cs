using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>
/// A site asks to use something it was never answered for - the camera, the microphone, where the device is. The
/// application asks the user and answers with <see cref="Allow"/> or <see cref="Block"/>, kept for the site from then on
/// (<see cref="WebViewEngine.SitePermissions"/>); it says it will with <see cref="Handled"/>, and may answer later.
/// Unhandled, the site is refused, and asks again next time. Raised on the UI thread.
/// </summary>
public sealed class WebPermissionRequestedEventArgs : EventArgs
{
    private readonly WebViewPermissionRequest request;

    internal WebPermissionRequestedEventArgs(WebViewPermissionRequest request)
    {
        this.request = request;
        request.Closed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The site asking, as its origin: "https://example.org".</summary>
    public string Site => request.Site;

    /// <summary>What it asks to use.</summary>
    public WebPermission Permissions => request.Permissions;

    /// <summary>Set when the application answers, now or later.</summary>
    public bool Handled { get; set; }

    /// <summary>Whether it is answered - or gone, its page gone on.</summary>
    public bool IsAnswered => request.IsAnswered;

    /// <summary>Answered, or gone: whatever shows it puts it away.</summary>
    public event EventHandler Closed;

    /// <summary>Lets the site use what it asked for, now and from now on.</summary>
    public void Allow() => request.Decide(true);

    /// <summary>Refuses it, now and from now on.</summary>
    public void Block() => request.Decide(false);

    /// <summary>Refuses it now, keeping nothing: the site asks again next time - a question put away unanswered.</summary>
    public void Dismiss() => request.Answer(false);
}
