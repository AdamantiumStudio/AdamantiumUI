using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>
/// A page another one opened (<see cref="WebNewWindowRequestedEventArgs.Page"/>), waiting for the <see cref="WebView"/>
/// that shows it, given through <see cref="WebView.NewPage"/>. A page the opener made with <c>window.open</c> or a link
/// is Chromium's already - loading, its <c>window.opener</c> joined to the page that opened it - and the view takes it
/// up as it is; one opened by the view's own menu is made there at <see cref="Address"/>. Disposed unshown, it closes.
/// </summary>
public sealed class WebViewNewPage : IDisposable
{
    private WebViewBrowser waiting;
    private WebViewWaitingView waitingView;

    internal WebViewNewPage(string address, WebViewBrowser waiting = null, WebViewWaitingView waitingView = null)
    {
        Address = address;
        this.waiting = waiting;
        this.waitingView = waitingView;
    }

    /// <summary>Where the page was opened at.</summary>
    public string Address { get; }

    private string reachedAddress;

    /// <summary>Where a page that could not be shown as it was had gone meanwhile, for the view to make it there - once;
    /// null when there is none.</summary>
    internal string TakeReachedAddress()
    {
        var reached = reachedAddress;
        reachedAddress = null;
        return reached;
    }

    /// <summary>The browser Chromium made for the page, and what it said while it <paramref name="waited"/>, for the
    /// view to adopt; null for a page the view makes itself, one taken up already, or one that can no longer be shown as
    /// it is - which the view then makes at <see cref="Address"/>.</summary>
    internal WebViewBrowser Take(out WebViewWaitingView waited)
    {
        var taken = waiting;
        waited = waitingView;
        waiting = null;
        waitingView = null;
        if (waited is not { IsLost: true })
        {
            return taken;
        }

        reachedAddress = waited.Address;
        waited.Discard();
        waited = null;
        taken?.Dispose();
        return null;
    }

    /// <summary>Closes the page if no view has taken it up.</summary>
    public void Dispose()
    {
        var unshown = waiting;
        waiting = null;
        waitingView?.Discard();
        waitingView = null;
        unshown?.Dispose();
    }
}
