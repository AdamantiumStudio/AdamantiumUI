using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>Tells whether CEF set a cookie; not set when CEF lets go of it without telling.</summary>
internal sealed unsafe class WebViewSetCookieCallback : CefObject
{
    private readonly TaskCompletionSource<bool> done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WebViewSetCookieCallback() : base(sizeof(CefSetCookieCallback))
    {
        ((CefSetCookieCallback*)Native)->on_complete = &OnComplete;
    }

    /// <summary>Whether the cookie was set.</summary>
    public Task<bool> Set => done.Task;

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefSetCookieCallback* Shared => (CefSetCookieCallback*)Share();

    /// <summary>Ends with nothing set: CEF refused the cookie or its address.</summary>
    public void Refused() => done.TrySetResult(false);

    protected override void OnFreed() => done.TrySetResult(false);

    [UnmanagedCallersOnly]
    private static void OnComplete(CefSetCookieCallback* self, int success)
    {
        try
        {
            From<WebViewSetCookieCallback>(self).done.TrySetResult(success != 0);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }
}
