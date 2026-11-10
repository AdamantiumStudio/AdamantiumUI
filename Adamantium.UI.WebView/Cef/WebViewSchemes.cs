using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The schemes of the application's own: <c>app</c>, whose sites <see cref="WebViewEngine.AddSite"/> serves.
/// Every process of CEF's registers them alike, the browser process and the helper's.</summary>
internal static unsafe class WebViewSchemes
{
    /// <summary>The scheme of the application's own sites: <c>app://site/path</c>.</summary>
    public const string App = "app";

    // A site of the application's is a site as any other - a standard address, an origin of its own, secure, reached by
    // fetch and by CORS - so its pages have what a page on https has.
    private const CefSchemeOptions AppOptions =
        CefSchemeOptions.Standard | CefSchemeOptions.Secure | CefSchemeOptions.CorsEnabled | CefSchemeOptions.FetchEnabled;

    /// <summary>Registers the schemes with <paramref name="registrar"/>, as CEF asks of each process.</summary>
    public static void Register(CefSchemeRegistrar* registrar)
    {
        var name = new CefStringUtf16();
        try
        {
            CefStrings.Set(&name, App);
            registrar->add_custom_scheme(registrar, &name, (int)AppOptions);
        }
        finally
        {
            CefStrings.Clear(&name);
        }
    }
}
