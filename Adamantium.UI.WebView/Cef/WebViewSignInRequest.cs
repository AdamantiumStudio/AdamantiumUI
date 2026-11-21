using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// A site or a proxy asking to sign in, waiting for an answer: who asks, and Chromium's callback held until the answer -
/// once. Made on CEF's IO thread, answered on the UI loop; CEF takes the answer on any thread.
/// </summary>
internal sealed unsafe class WebViewSignInRequest
{
    private readonly Lock gate = new();
    private CefAuthCallback* callback;

    public WebViewSignInRequest(WebSignInTarget target, string address, int navigation, CefAuthCallback* callback)
    {
        Target = target;
        Address = address;
        Navigation = navigation;
        this.callback = callback;
        CefRefCounting.AddRefCef(callback);
    }

    /// <summary>Who asks.</summary>
    public WebSignInTarget Target { get; }

    /// <summary>The address the page was going to.</summary>
    public string Address { get; }

    /// <summary>The navigation of the page's main frame it was asked of (<see cref="WebViewBrowser.NavigationAsking"/>):
    /// the page shown's, or one being gone to (<see cref="WebViewBrowser.IsOfThePageShown"/>).</summary>
    public int Navigation { get; }

    /// <summary>Whether it is answered.</summary>
    public bool IsAnswered
    {
        get
        {
            lock (gate)
            {
                return callback == null;
            }
        }
    }

    /// <summary>Signs in with <paramref name="credential"/>.</summary>
    public void Answer(WebCredential credential)
    {
        var held = Take();
        if (held == null)
        {
            return;
        }

        var user = new CefStringUtf16();
        var password = new CefStringUtf16();
        try
        {
            CefStrings.Set(&user, credential.UserName);
            CefStrings.Set(&password, credential.Password);
            held->cont(held, &user, &password);
        }
        finally
        {
            CefStrings.Clear(&user);
            CefStrings.Clear(&password);
            CefRefCounting.ReleaseCef(held);
        }
    }

    /// <summary>Refuses: the page gets the server's refusal as it is.</summary>
    public void Refuse()
    {
        var held = Take();
        if (held == null)
        {
            return;
        }

        held->cancel(held);
        CefRefCounting.ReleaseCef(held);
    }

    private CefAuthCallback* Take()
    {
        lock (gate)
        {
            var held = callback;
            callback = null;
            return held;
        }
    }
}
