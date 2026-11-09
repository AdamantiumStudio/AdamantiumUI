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
    }

    /// <summary>The application's CEF, the page is drawn by.</summary>
    public WebViewEngine Engine { get; }

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
