using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's file dialogs: a page's file field asks the view, never a window of Chromium's own; its callback
/// is answered with the files chosen or with none.</summary>
internal sealed unsafe class WebViewDialogHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewDialogHandler(WebViewBrowser browser) : base(sizeof(CefDialogHandler))
    {
        this.browser = browser;
        ((CefDialogHandler*)Native)->on_file_dialog = &OnFileDialog;
    }

    [UnmanagedCallersOnly]
    private static int OnFileDialog(CefDialogHandler* self, CefBrowser* cefBrowser, CefFileDialogMode mode,
        CefStringUtf16* title, CefStringUtf16* defaultFilePath, CefStringList acceptFilters, CefStringList acceptExtensions,
        CefStringList acceptDescriptions, CefFileDialogCallback* callback)
    {
        // Handled whatever comes: Chromium's own dialog is a window of its own over the application. One not asked is
        // answered with no files.
        WebViewFileDialogRequest request = null;
        try
        {
            request = new WebViewFileDialogRequest(
                mode switch
                {
                    CefFileDialogMode.OpenMultiple => WebFileDialogMode.OpenMultiple,
                    CefFileDialogMode.OpenFolder => WebFileDialogMode.OpenFolder,
                    CefFileDialogMode.Save => WebFileDialogMode.Save,
                    _ => WebFileDialogMode.Open
                },
                CefStrings.Read(title), CefStrings.Read(defaultFilePath),
                Filters(acceptFilters, acceptExtensions, acceptDescriptions), callback);
            var owner = From<WebViewDialogHandler>(self).browser;
            if (!owner.Is(cefBrowser) || !owner.OnFileDialog(request))
            {
                request.Cancel();
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
            request?.Cancel();
        }

        CefRefCounting.ReleaseCef(callback);
        CefRefCounting.ReleaseCef(cefBrowser);
        return 1;
    }

    // Each accepted type with the extensions Chromium expanded it to - "image/*" to ".png;.jpg;…" - and its description;
    // a bare extension stands for itself.
    private static IReadOnlyList<WebFileFilter> Filters(CefStringList filters, CefStringList extensions,
        CefStringList descriptions)
    {
        var types = CefStrings.ReadList(filters);
        var expanded = CefStrings.ReadList(extensions);
        var names = CefStrings.ReadList(descriptions);
        var result = new List<WebFileFilter>(types.Length);
        for (var i = 0; i < types.Length; i++)
        {
            var list = i < expanded.Length && expanded[i].Length > 0 ? expanded[i] : types[i].StartsWith('.') ? types[i] : "";
            var bare = list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(extension => extension.TrimStart('.'))
                .Where(extension => extension.Length > 0)
                .ToArray();
            var name = i < names.Length && names[i].Length > 0 ? names[i] : types[i];
            result.Add(new WebFileFilter(name, bare));
        }

        return result;
    }
}
