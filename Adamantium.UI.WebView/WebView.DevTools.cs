using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>The page's DevTools, docked inside the view to the right of the page or under it, with a splitter between
/// that the mouse drags.</summary>
public partial class WebView
{
    public static readonly AdamantiumProperty IsDevToolsOpenProperty = AdamantiumProperty.Register(
        nameof(IsDevToolsOpen), typeof(bool), typeof(WebView), new PropertyMetadata(false, OnIsDevToolsOpenChanged));

    public static readonly AdamantiumProperty DevToolsDockProperty = AdamantiumProperty.Register(nameof(DevToolsDock),
        typeof(WebViewDevToolsDock), typeof(WebView), new PropertyMetadata(WebViewDevToolsDock.Right));

    private WebViewDevToolsView devToolsView;

    /// <summary>Whether the page's DevTools are shown beside it, inside the view.</summary>
    public bool IsDevToolsOpen
    {
        get => GetValue<bool>(IsDevToolsOpenProperty);
        set => SetValue(IsDevToolsOpenProperty, value);
    }

    /// <summary>The side of the page the DevTools stand on: right or bottom.</summary>
    public WebViewDevToolsDock DevToolsDock
    {
        get => GetValue<WebViewDevToolsDock>(DevToolsDockProperty);
        set => SetValue(DevToolsDockProperty, value);
    }

    /// <summary>The page's browser, which the DevTools inspect; null while there is none.</summary>
    internal WebViewBrowser Page => browser;

    /// <summary>Whether the view may make its browser now; DevTools wait for the page they inspect.</summary>
    private protected virtual bool CanStartBrowser => true;

    /// <summary>Asks CEF for the browser just made for the view: the page at <see cref="Source"/>.</summary>
    private protected virtual void BeginBrowser(WebViewBrowser created, int width, int height, float scale,
        IntPtr parentWindow) =>
        created.Create(Source?.AbsoluteUri ?? "about:blank", width, height, scale, parentWindow);

    private void TakeDevToolsView(WebViewDevToolsView next)
    {
        if (devToolsView != null)
        {
            devToolsView.Inspected = null;
        }

        devToolsView = next;
        if (devToolsView != null)
        {
            devToolsView.Inspected = this;
        }
    }

    // The DevTools follow the page: put away with it, or when they are closed - the socket they talked through closed
    // too, so nothing stays listening for a front end that is gone.
    private void PageChanged()
    {
        if (!IsDevToolsOpen)
        {
            browser?.CloseDevTools();
        }

        devToolsView?.Restart();
    }

    private static void OnIsDevToolsOpenChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        ((WebView)component).PageChanged();
}
