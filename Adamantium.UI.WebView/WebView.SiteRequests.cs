using Adamantium.UI.Core;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>What a page asks of the application - files from its file field, a permission to use the camera or know
/// where the device is, where to save a file it downloads: asked of the view model through <see cref="WebViewBridge"/>,
/// out of the browser's own work, so it may ask the user with a modal dialog. Unasked - no bridge - a page gets no
/// files and no permission, and downloads nothing.</summary>
public partial class WebView
{
    bool IWebViewBrowserView.FileDialogRequested(WebViewBrowser from, WebViewFileDialogRequest request)
    {
        if (!ReferenceEquals(browser, from) || Bridge is not { } bridge)
        {
            return false;
        }

        LoopSignal.Post(() =>
        {
            if (request.IsAnswered)
            {
                return;
            }

            var args = new WebFileDialogRequestedEventArgs(request);
            bridge.OnFileDialogRequested(args);
            if (!args.Handled)
            {
                request.Cancel();
            }
        });
        return true;
    }

    bool IWebViewBrowserView.PermissionRequested(WebViewBrowser from, WebViewPermissionRequest request)
    {
        if (!ReferenceEquals(browser, from) || Bridge is not { } bridge)
        {
            return false;
        }

        LoopSignal.Post(() =>
        {
            if (request.IsAnswered)
            {
                return;
            }

            var args = new WebPermissionRequestedEventArgs(request);
            bridge.OnPermissionRequested(args);
            if (!args.Handled)
            {
                request.Answer(false);
            }
        });
        return true;
    }

    bool IWebViewBrowserView.DownloadRequested(WebViewBrowser from, WebDownload download)
    {
        if (!ReferenceEquals(browser, from) || Bridge is not { } bridge)
        {
            return false;
        }

        LoopSignal.Post(() =>
        {
            if (!download.IsAsked)
            {
                return;
            }

            var args = new WebDownloadRequestedEventArgs(download);
            bridge.OnDownloadRequested(args);
            if (!args.Handled)
            {
                args.Refuse();
            }
        });
        return true;
    }
}
