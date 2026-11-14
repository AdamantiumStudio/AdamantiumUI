using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>Gathers the cookies CEF visits; done when CEF lets go of it, which it does with no cookie to visit too.
/// </summary>
internal sealed unsafe class WebViewCookieVisitor : CefObject
{
    private readonly List<WebCookie> cookies = [];
    private readonly TaskCompletionSource<IReadOnlyList<WebCookie>> done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WebViewCookieVisitor() : base(sizeof(CefCookieVisitor))
    {
        ((CefCookieVisitor*)Native)->visit = &Visit;
    }

    /// <summary>The cookies, once all are visited.</summary>
    public Task<IReadOnlyList<WebCookie>> Cookies => done.Task;

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefCookieVisitor* Shared => (CefCookieVisitor*)Share();

    /// <summary>Ends the visit with what was gathered: CEF could not reach the cookies.</summary>
    public void Refused() => done.TrySetResult(cookies.ToArray());

    protected override void OnFreed() => done.TrySetResult(cookies.ToArray());

    [UnmanagedCallersOnly]
    private static int Visit(CefCookieVisitor* self, CefCookie* cookie, int count, int total, int* deleteCookie)
    {
        try
        {
            From<WebViewCookieVisitor>(self).cookies.Add(CefCookies.Read(cookie));
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        return 1;
    }
}
