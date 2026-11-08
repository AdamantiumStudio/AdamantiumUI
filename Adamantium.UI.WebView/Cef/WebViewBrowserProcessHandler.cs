using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The browser process's handler: CEF asks through it for its work to be run - at once or after a delay in
/// milliseconds - which the engine's message pump does on the UI loop.</summary>
internal sealed unsafe class WebViewBrowserProcessHandler : CefObject
{
    private readonly Action<long> schedulePumpWork;

    public WebViewBrowserProcessHandler(Action<long> schedulePumpWork) : base(sizeof(CefBrowserProcessHandler))
    {
        this.schedulePumpWork = schedulePumpWork;
        ((CefBrowserProcessHandler*)Native)->on_schedule_message_pump_work = &OnScheduleMessagePumpWork;
    }

    [UnmanagedCallersOnly]
    private static void OnScheduleMessagePumpWork(CefBrowserProcessHandler* self, long delayMs)
    {
        try
        {
            From<WebViewBrowserProcessHandler>(self).schedulePumpWork(delayMs);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }
}
