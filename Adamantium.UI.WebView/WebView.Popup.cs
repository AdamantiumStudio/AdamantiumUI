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

        var scale = Scale();
        popupSurface.Width = descriptor.Width / scale;
        popupSurface.Height = descriptor.Height / scale;
        popupSurface.SetSource(descriptor);
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

    private void HidePopup()
    {
        if (popupSurface != null)
        {
            popupSurface.Visibility = Visibility.Collapsed;
        }
    }
}
