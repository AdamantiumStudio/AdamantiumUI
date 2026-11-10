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

    private static void OnBridgeChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        var view = (WebView)component;
        (e.OldValue as WebViewBridge)?.Detach(view);
        (e.NewValue as WebViewBridge)?.Attach(view);
        view.ScriptsForNewDocumentsChanged();
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
}
