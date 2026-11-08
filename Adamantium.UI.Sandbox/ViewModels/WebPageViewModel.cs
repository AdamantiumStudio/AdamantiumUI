using System;
using Adamantium.MVVM;

namespace Adamantium.UI.Sandbox.ViewModels;

/// <summary>Web tab: a page drawn by Chromium off screen, at the address typed above it once Enter is pressed. Grows with
/// the WebView's steps - input, popups, navigation - so each shows here as it comes.</summary>
[ViewModel]
public partial class WebPageViewModel : TabPageViewModel
{
    public WebPageViewModel() : base("Web")
    {
        Source = AddressToUri(_address);
    }

    /// <summary>The address as typed.</summary>
    [Bindable] private string _address = "https://example.org";

    /// <summary>The page shown: the address last gone to, read as a web address.</summary>
    [Bindable] private Uri _source;

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
