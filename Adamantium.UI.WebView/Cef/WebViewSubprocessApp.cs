using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>The application a CEF subprocess runs with: it registers the application's schemes, as the browser
/// process does - a renderer that did not would take <c>app://</c> addresses for others.</summary>
internal sealed unsafe class WebViewSubprocessApp : CefObject
{
    public WebViewSubprocessApp() : base(sizeof(CefApp))
    {
        ((CefApp*)Native)->on_register_custom_schemes = &OnRegisterCustomSchemes;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefApp* Shared => (CefApp*)Share();

    // Nothing here throws - a string set and handed over - and the helper has no log to tell of it if it did.
    [UnmanagedCallersOnly]
    private static void OnRegisterCustomSchemes(CefApp* self, CefSchemeRegistrar* registrar) =>
        WebViewSchemes.Register(registrar);
}
