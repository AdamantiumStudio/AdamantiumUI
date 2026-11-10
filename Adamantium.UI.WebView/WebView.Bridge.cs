using Adamantium.UI.Core;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>The view's page as its view model reaches it: through the <see cref="WebViewBridge"/> bound to
/// <see cref="Bridge"/>.</summary>
public partial class WebView
{
    public static readonly AdamantiumProperty BridgeProperty = AdamantiumProperty.Register(nameof(Bridge),
        typeof(WebViewBridge), typeof(WebView), new PropertyMetadata(null, OnBridgeChanged));

    /// <summary>What the view model runs the page through - HTML, scripts, messages, where the page goes - bound from
    /// it; none without it.</summary>
    public WebViewBridge Bridge
    {
        get => GetValue<WebViewBridge>(BridgeProperty);
        set => SetValue(BridgeProperty, value);
    }

    internal bool HasPage => browser != null;

    internal void LoadHtml(string html) => browser?.LoadHtml(html);

    internal void Navigate(Uri address) => browser?.Navigate(address.AbsoluteUri);

    internal Task<string> ExecuteScriptAsync(string script) =>
        browser?.EvaluateAsync(script) ?? Task.FromException<string>(new WebViewScriptException("There is no page."));

    internal void PostMessage(string json) => browser?.Post(json);

    internal void ScriptsForNewDocumentsChanged() => browser?.ScriptsForNewDocumentsChanged();

    // The page's requests are asked of the bridge while it listens to them, and of nobody otherwise.
    internal void ResourceInterceptionChanged()
    {
        if (browser != null)
        {
            browser.InterceptsResources = Bridge?.InterceptsResources == true;
        }
    }

    private static void OnBridgeChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var view = (WebView)component;
        (e.OldValue as WebViewBridge)?.Detach(view);
        (e.NewValue as WebViewBridge)?.Attach(view);
        view.ScriptsForNewDocumentsChanged();
        view.ResourceInterceptionChanged();
    }

    void IWebViewBrowserView.ResourceRequested(WebViewBrowser from, WebResourceRequestedEventArgs asked)
    {
        if (ReferenceEquals(browser, from))
        {
            Bridge?.OnResourceRequested(asked);
        }
    }

    IReadOnlyList<WebDocumentScript> IWebViewBrowserView.ScriptsForNewDocuments(WebViewBrowser from) =>
        Bridge?.ScriptsForNewDocuments ?? [];

    bool IWebViewBrowserView.NavigationStarting(WebViewBrowser from, string address, bool userGesture, bool isRedirect) =>
        ReferenceEquals(browser, from) && Bridge?.OnNavigationStarting(address, userGesture, isRedirect) == true;

    void IWebViewBrowserView.NavigationCompleted(WebViewBrowser from, string address, WebViewLoadError error)
    {
        if (ReferenceEquals(browser, from))
        {
            Bridge?.OnNavigationCompleted(address, error);
        }
    }

    void IWebViewBrowserView.MessageReceived(WebViewBrowser from, string json)
    {
        if (ReferenceEquals(browser, from))
        {
            Bridge?.OnMessage(json);
        }
    }

    void IWebViewBrowserView.HostCalled(WebViewBrowser from, WebHostCall call)
    {
        if (ReferenceEquals(browser, from) && Bridge is { } bridge)
        {
            bridge.OnHostCall(call);
        }
        else
        {
            call.Refuse("There is no object here.");
        }
    }

    void IWebViewBrowserView.HostContextGone(WebViewBrowser from, string context) =>
        Bridge?.OnHostContextGone(from, context);
}
