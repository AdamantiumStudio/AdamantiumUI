using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>Tells how many cookies CEF deleted; none when CEF lets go of it without telling.</summary>
internal sealed unsafe class WebViewDeleteCookiesCallback : CefObject
{
    private readonly TaskCompletionSource<int> done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WebViewDeleteCookiesCallback() : base(sizeof(CefDeleteCookiesCallback))
    {
        ((CefDeleteCookiesCallback*)Native)->on_complete = &OnComplete;
    }

    /// <summary>How many cookies were deleted.</summary>
    public Task<int> Deleted => done.Task;

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefDeleteCookiesCallback* Shared => (CefDeleteCookiesCallback*)Share();

    /// <summary>Ends with none deleted: CEF refused the address.</summary>
    public void Refused() => done.TrySetResult(0);

    protected override void OnFreed() => done.TrySetResult(0);

    [UnmanagedCallersOnly]
    private static void OnComplete(CefDeleteCookiesCallback* self, int deleted)
    {
        try
        {
            From<WebViewDeleteCookiesCallback>(self).done.TrySetResult(deleted);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }
}
