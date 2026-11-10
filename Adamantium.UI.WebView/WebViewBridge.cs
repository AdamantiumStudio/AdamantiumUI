namespace Adamantium.UI.WebView;

/// <summary>
/// What a view model does with the page of the <see cref="WebView"/> whose <see cref="WebView.Bridge"/> it is bound
/// to: show HTML it has, run a script and get its answer, send the page a message and hear the ones it sends, and know
/// where the page goes - and stop it. The page sends with <c>window.adamantium.postMessage(value)</c> and hears with
/// <c>window.adamantium.addEventListener('message', e =&gt; e.data)</c>. Used on the UI thread.
/// </summary>
public sealed class WebViewBridge
{
    private WebView view;

    /// <summary>The page sent a message.</summary>
    public event EventHandler<WebMessageEventArgs> MessageReceived;

    /// <summary>The page is about to go somewhere in its main frame; a handler may keep it where it is.</summary>
    public event EventHandler<WebNavigationStartingEventArgs> NavigationStarting;

    /// <summary>The page in the main frame loaded, or failed to.</summary>
    public event EventHandler<WebNavigationCompletedEventArgs> NavigationCompleted;

    /// <summary>The page opens another, by the user's doing: the view model shows it in a view of its own, or the page
    /// goes there itself.</summary>
    public event EventHandler<WebNewWindowRequestedEventArgs> NewWindowRequested;

    /// <summary>The page's file field asks for files: the view model asks the user, or the page gets none.</summary>
    public event EventHandler<WebFileDialogRequestedEventArgs> FileDialogRequested;

    /// <summary>A site asks to use something it was never answered for: the view model asks the user, or it is refused.
    /// </summary>
    public event EventHandler<WebPermissionRequestedEventArgs> PermissionRequested;

    /// <summary>Whether the bridge is bound to a view with a page.</summary>
    public bool HasPage => view?.HasPage == true;

    /// <summary>Goes to <paramref name="address"/>, loading it again when the page is there already - as Enter in a
    /// browser's address does, which setting <see cref="WebView.Source"/> to the address it has cannot.</summary>
    public void Navigate(Uri address) => Page().Navigate(address);

    /// <summary>Shows <paramref name="html"/> as the page.</summary>
    public void LoadHtml(string html) => Page().LoadHtml(html);

    /// <summary>Runs <paramref name="script"/> in the page and answers with what it comes to, as JSON (a promise is
    /// waited for); throws <see cref="WebViewScriptException"/> with the page's message when it throws.</summary>
    public Task<string> ExecuteScriptAsync(string script) => Page().ExecuteScriptAsync(script);

    /// <summary>Sends the page <paramref name="json"/>: its message listeners get it, parsed, as the event's data.
    /// </summary>
    public void PostMessage(string json) => Page().PostMessage(json);

    internal void Attach(WebView attached) => view = attached;

    internal void Detach(WebView detached)
    {
        if (ReferenceEquals(view, detached))
        {
            view = null;
        }
    }

    internal void OnMessage(string json) => MessageReceived?.Invoke(this, new WebMessageEventArgs(json));

    internal bool OnNavigationStarting(string address, bool userGesture, bool isRedirect)
    {
        var args = new WebNavigationStartingEventArgs(address, userGesture, isRedirect);
        NavigationStarting?.Invoke(this, args);
        return args.Cancel;
    }

    internal void OnNavigationCompleted(string address, WebViewLoadError error) =>
        NavigationCompleted?.Invoke(this, new WebNavigationCompletedEventArgs(address, error));

    internal void OnFileDialogRequested(WebFileDialogRequestedEventArgs args) => FileDialogRequested?.Invoke(this, args);

    internal void OnPermissionRequested(WebPermissionRequestedEventArgs args) => PermissionRequested?.Invoke(this, args);

    internal bool OnNewWindowRequested(WebViewNewPage page, WebNewWindowKind kind)
    {
        var args = new WebNewWindowRequestedEventArgs(page, kind);
        NewWindowRequested?.Invoke(this, args);
        return args.Handled;
    }

    private WebView Page() =>
        view?.HasPage == true ? view : throw new InvalidOperationException("The bridge is bound to no WebView with a page.");
}
