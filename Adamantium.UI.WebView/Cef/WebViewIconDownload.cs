using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>One download of a page's icon: hands what came - the image, or nothing - to the browser that asked, with
/// the number it asked under, so an icon of a page gone is told apart, and which of the page's addresses it was.
/// </summary>
internal sealed unsafe class WebViewIconDownload : CefObject
{
    private readonly WebViewBrowser browser;
    private readonly int asked;
    private readonly int index;

    public WebViewIconDownload(WebViewBrowser browser, int asked, int index) : base(sizeof(CefDownloadImageCallback))
    {
        this.browser = browser;
        this.asked = asked;
        this.index = index;
        ((CefDownloadImageCallback*)Native)->on_download_image_finished = &OnDownloadImageFinished;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefDownloadImageCallback* Shared => (CefDownloadImageCallback*)Share();

    [UnmanagedCallersOnly]
    private static void OnDownloadImageFinished(CefDownloadImageCallback* self, CefStringUtf16* address, int status,
        CefImage* image)
    {
        try
        {
            var download = From<WebViewIconDownload>(self);
            download.browser.OnIconDownloaded(download.asked, download.index, image);
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        if (image != null)
        {
            CefRefCounting.ReleaseCef(image);
        }
    }
}
