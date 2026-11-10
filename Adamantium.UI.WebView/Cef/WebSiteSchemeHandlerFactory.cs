using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>Answers the requests for <c>app://</c> addresses: the site the address names opens the file its path
/// names, a site or file there is none of answers 404. Called on CEF's IO thread.</summary>
internal sealed unsafe class WebSiteSchemeHandlerFactory : CefObject
{
    private const string AnyType = "application/octet-stream";

    private readonly Func<string, IWebSiteContent> site;

    public WebSiteSchemeHandlerFactory(Func<string, IWebSiteContent> site) : base(sizeof(CefSchemeHandlerFactory))
    {
        this.site = site;
        ((CefSchemeHandlerFactory*)Native)->create = &Create;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefSchemeHandlerFactory* Shared => (CefSchemeHandlerFactory*)Share();

    [UnmanagedCallersOnly]
    private static CefResourceHandler* Create(CefSchemeHandlerFactory* self, CefBrowser* browser, CefFrame* frame,
        CefStringUtf16* schemeName, CefRequest* request)
    {
        CefResourceHandler* handler = null;
        try
        {
            var url = CefStrings.ReadAndFree(request->get_url(request));
            Stream content = null;
            var type = AnyType;
            if (Uri.TryCreate(url, UriKind.Absolute, out var address))
            {
                var path = Uri.UnescapeDataString(address.AbsolutePath).TrimStart('/');
                content = From<WebSiteSchemeHandlerFactory>(self).site(address.Host)?.Open(path);
                type = TypeOf(path);
            }

            handler = new WebSiteResourceHandler(content, type).Shared;
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(browser);
        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(request);
        return handler;
    }

    // The type Chromium gives the path's extension; a folder's path is its index.html.
    private static string TypeOf(string path)
    {
        var extension = path.Length == 0 || path.EndsWith('/') ? "html" : Path.GetExtension(path).TrimStart('.');
        if (extension.Length == 0)
        {
            return AnyType;
        }

        var cefExtension = new CefStringUtf16();
        try
        {
            CefStrings.Set(&cefExtension, extension);
            var type = CefStrings.ReadAndFree(CefInterop.CefGetMimeType(&cefExtension));
            return string.IsNullOrEmpty(type) ? AnyType : type;
        }
        finally
        {
            CefStrings.Clear(&cefExtension);
        }
    }
}
