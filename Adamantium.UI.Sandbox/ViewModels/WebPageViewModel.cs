using System;
using Adamantium.MVVM;
using Adamantium.UI.WebView;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Web tab: a page drawn by Chromium off screen, at the address typed above it once Enter is pressed. Grows with
/// the WebView's steps - input, popups, navigation - so each shows here as it comes.</summary>
[ViewModel]
public partial class WebPageViewModel : TabPageViewModel
{
    public WebPageViewModel(WebViewEngine engine) : base("Web")
    {
        Engine = engine;
        Source = AddressToUri(_address);
        Bridge.NewWindowRequested += OnNewWindowRequested;
    }

    /// <summary>The application's CEF, the page is drawn by.</summary>
    public WebViewEngine Engine { get; }

    /// <summary>What the view model runs the page through.</summary>
    public WebViewBridge Bridge { get; } = new();

    /// <summary>The page the page opened last - or that one in turn - shown beside it; none until one is.</summary>
    [Bindable] private OpenedWebPageViewModel _openedPage;

    /// <summary>The address as typed.</summary>
    [Bindable] private string _address = "https://example.org";

    /// <summary>The page shown: the address last gone to, read as a web address - or the one the page went to itself,
    /// which the address box then shows.</summary>
    [Bindable] private Uri _source;

    /// <summary>Whether the page's frames may come in Chromium's textures on the GPU rather than as pixels in memory.
    /// </summary>
    [Bindable] private bool _allowSharedTexture = true;

    /// <summary>Whether the page's DevTools are shown beside it.</summary>
    [Bindable] private bool _devToolsOpen;

    /// <summary>The side of the page the DevTools stand on.</summary>
    [Bindable] private WebViewDevToolsDock _devToolsDock = WebViewDevToolsDock.Right;

    /// <summary>The sides the DevTools can stand on.</summary>
    public WebViewDevToolsDock[] DevToolsDocks { get; } = Enum.GetValues<WebViewDevToolsDock>();

    partial void OnSourceChanged(Uri value)
    {
        if (value != null)
        {
            Address = value.AbsoluteUri;
        }
    }

    // A tab of its own in a browser; beside the page here, the page that opened it left as it is. One at a time: the
    // next the page opens takes its place. What the page beside opens in turn it goes to itself - taking its place
    // would close the page that opened it.
    private void OnNewWindowRequested(object sender, WebNewWindowRequestedEventArgs e)
    {
        var opened = new OpenedWebPageViewModel(e.Page, e.Kind);
        var previous = OpenedPage;
        OpenedPage = opened;
        previous?.Dispose();
        e.Handled = true;
    }

    [Command] private void CloseOpened()
    {
        var opened = OpenedPage;
        OpenedPage = null;
        opened?.Dispose();
    }

    [Command] private void Go()
    {
        if (AddressToUri(Address) is { } address)
        {
            Source = address;
        }
    }

    private static Uri AddressToUri(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" or "file" or "about"
            ? absolute
            : Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var web) ? web : null;
    }
}
