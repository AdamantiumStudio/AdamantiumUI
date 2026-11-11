using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A request of the page's waiting for the view to decide, with the references kept for it until it is let
/// go on, or refused.</summary>
internal sealed unsafe class WebViewWaitingRequest
{
    private readonly CefRequest* request;
    private readonly CefCallback* callback;

    public WebViewWaitingRequest(CefRequest* request, CefCallback* callback)
    {
        this.request = request;
        this.callback = callback;
    }

    /// <summary>Whether the page gave the request up while it waited.</summary>
    public volatile bool GivenUp;

    /// <summary>Sends the request to <paramref name="address"/> instead.</summary>
    public void Redirect(string address)
    {
        var url = new CefStringUtf16();
        try
        {
            CefStrings.Set(&url, address);
            request->set_url(request, &url);
        }
        finally
        {
            CefStrings.Clear(&url);
        }
    }

    /// <summary>Lets the request go on, or refuses it, and lets go of the references kept for it.</summary>
    public void Finish(bool goesOn)
    {
        if (goesOn)
        {
            callback->cont(callback);
        }
        else
        {
            callback->cancel(callback);
        }

        CefRefCounting.ReleaseCef(request);
        CefRefCounting.ReleaseCef(callback);
    }
}
