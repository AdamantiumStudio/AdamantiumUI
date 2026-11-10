using Adamantium.Graphics.Core;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>The list a page drops - a select's: Chromium draws it apart from the page, and the template's
/// <c>PART_PopupSurface</c> shows its frames over the page where the page places it, one to one as the page's are.
/// Presses on it reach the page, which takes them for the list.</summary>
public partial class WebView
{
    private RenderTargetPanel popupSurface;
    private uint popupFrameWidth;
    private uint popupFrameHeight;

    private void TakePopupSurface(RenderTargetPanel panel)
    {
        popupSurface?.ClearSource();
        popupSurface = panel;
    }

    void IWebViewBrowserView.ShowPopup(WebViewBrowser from, SharedSurfaceDescriptor descriptor)
    {
        if (!ReferenceEquals(browser, from) || popupSurface == null)
        {
            return;
        }

        popupFrameWidth = descriptor.Width;
        popupFrameHeight = descriptor.Height;
        SizePopupToFrame();
        popupSurface.SetSource(descriptor);
    }

    // One to one, as the page's frame: the list's pixels at the screen's scale.
    private void SizePopupToFrame()
    {
        if (popupSurface == null || popupFrameWidth == 0 || popupFrameHeight == 0)
        {
            return;
        }

        var scale = Scale();
        popupSurface.Width = popupFrameWidth / scale;
        popupSurface.Height = popupFrameHeight / scale;
    }

    void IWebViewBrowserView.PopupPlaced(WebViewBrowser from, Rect bounds)
    {
        if (!ReferenceEquals(browser, from) || popupSurface == null)
        {
            return;
        }

        Canvas.SetLeft(popupSurface, bounds.X);
        Canvas.SetTop(popupSurface, bounds.Y);
        popupSurface.Visibility = Visibility.Visible;
    }

    void IWebViewBrowserView.PopupClosed(WebViewBrowser from)
    {
        if (ReferenceEquals(browser, from))
        {
            HidePopup();
        }
    }

    // Put away with its surfaces: the next list comes on its own.
    private void HidePopup()
    {
        if (popupSurface != null)
        {
            popupSurface.Visibility = Visibility.Collapsed;
            popupSurface.ClearSource();
        }

        popupFrameWidth = 0;
        popupFrameHeight = 0;
    }
}
