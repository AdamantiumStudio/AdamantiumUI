using Adamantium.UI.Core;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>Pages the page opens - a link to a new tab, <c>window.open</c>, a row of the page's menu: asked of the view
/// model through <see cref="WebViewBridge.NewWindowRequested"/>, and shown by the view it gives one to through
/// <see cref="NewPage"/>.</summary>
public partial class WebView
{
    public static readonly AdamantiumProperty NewPageProperty = AdamantiumProperty.Register(nameof(NewPage),
        typeof(WebViewNewPage), typeof(WebView), new PropertyMetadata(null, OnPageMakerChanged));

    /// <summary>A page another one opened, shown here in place of <see cref="Source"/>: as Chromium made it, still the
    /// opener's <c>window.opener</c>, or made here at its address. Let go of once shown, it is the view's page all the
    /// same.</summary>
    public WebViewNewPage NewPage
    {
        get => GetValue<WebViewNewPage>(NewPageProperty);
        set => SetValue(NewPageProperty, value);
    }

    bool IWebViewBrowserView.NewWindowRequested(WebViewBrowser from, WebViewNewPage page, WebNewWindowKind kind) =>
        ReferenceEquals(browser, from) && Bridge?.OnNewWindowRequested(page, kind) == true;

    // The page given was Chromium's, given up before it was made: made here at its address instead.
    void IWebViewBrowserView.BrowserLost(WebViewBrowser from)
    {
        if (ReferenceEquals(browser, from))
        {
            RestartBrowser();
        }
    }

    // A page the view itself opens - a row of its menu: in a view of the view model's, or here when it shows none.
    private void OpenNewPage(string address, WebNewWindowKind kind)
    {
        var page = new WebViewNewPage(address);
        if (Bridge?.OnNewWindowRequested(page, kind) == true)
        {
            return;
        }

        page.Dispose();
        browser?.Navigate(address);
    }

    // The browser Chromium made for the page given, taken up as it is: told this view's size, and what it said meanwhile.
    private WebViewBrowser AdoptNewPage(int width, int height, float scale)
    {
        if (NewPage?.Take(out var waited) is not { } adopted)
        {
            return null;
        }

        browser = adopted;
        ResourceInterceptionChanged();
        adopted.Adopt(this, waited);
        adopted.Resize(width, height, scale);
        return adopted;
    }

    // Where a page made here starts: where a page given got to before it had to be made again; else where the view model
    // has it, else where the page given was opened at.
    private string StartAddress() => NewPage?.TakeReachedAddress() ?? Source?.AbsoluteUri ?? NewPage?.Address ?? "about:blank";
}
