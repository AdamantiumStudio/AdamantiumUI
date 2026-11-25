using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The status of a component once Chromium's update of it ended - read there, on CEF's UI thread, where alone
/// CEF answers; not installed when CEF lets go of it without telling.</summary>
internal sealed unsafe class WebViewComponentUpdateCallback : CefObject
{
    private readonly Func<WebComponentStatus> readStatus;
    private readonly TaskCompletionSource<WebComponentStatus> done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WebViewComponentUpdateCallback(Func<WebComponentStatus> readStatus) : base(sizeof(CefComponentUpdateCallback))
    {
        this.readStatus = readStatus;
        ((CefComponentUpdateCallback*)Native)->on_complete = &OnComplete;
    }

    /// <summary>The status after the update.</summary>
    public Task<WebComponentStatus> Ended => done.Task;

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefComponentUpdateCallback* Shared => (CefComponentUpdateCallback*)Share();

    protected override void OnFreed() => done.TrySetResult(WebComponentStatus.NotInstalled);

    [UnmanagedCallersOnly]
    private static void OnComplete(CefComponentUpdateCallback* self, CefStringUtf16* componentId,
        CefComponentUpdateError error)
    {
        try
        {
            var callback = From<WebViewComponentUpdateCallback>(self);
            callback.done.TrySetResult(callback.readStatus());
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }
}
