using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// A page's file field asking for files, waiting to be answered: what it asks, and Chromium's callback held until the
/// answer - once. Lives on the UI loop, where CEF calls back.
/// </summary>
internal sealed unsafe class WebViewFileDialogRequest
{
    private CefFileDialogCallback* callback;

    public WebViewFileDialogRequest(WebFileDialogMode mode, string title, string defaultPath,
        IReadOnlyList<WebFileFilter> filters, CefFileDialogCallback* callback)
    {
        Mode = mode;
        Title = title;
        DefaultPath = defaultPath;
        Filters = filters;
        this.callback = callback;
        CefRefCounting.AddRefCef(callback);
    }

    public WebFileDialogMode Mode { get; }

    public string Title { get; }

    public string DefaultPath { get; }

    public IReadOnlyList<WebFileFilter> Filters { get; }

    /// <summary>Whether it is answered.</summary>
    public bool IsAnswered => callback == null;

    /// <summary>Gives the page <paramref name="paths"/>; none is a cancel.</summary>
    public void Choose(IReadOnlyList<string> paths)
    {
        if (paths == null || paths.Count == 0)
        {
            Cancel();
            return;
        }

        if (callback == null)
        {
            return;
        }

        var held = callback;
        callback = null;
        var list = CefInterop.CefStringListAlloc();
        try
        {
            foreach (var path in paths)
            {
                var item = new CefStringUtf16();
                try
                {
                    CefStrings.Set(&item, path);
                    CefInterop.CefStringListAppend(list, &item);
                }
                finally
                {
                    CefStrings.Clear(&item);
                }
            }

            held->cont(held, list);
        }
        finally
        {
            CefInterop.CefStringListFree(list);
            CefRefCounting.ReleaseCef(held);
        }
    }

    /// <summary>Gives the page nothing.</summary>
    public void Cancel()
    {
        if (callback == null)
        {
            return;
        }

        var held = callback;
        callback = null;
        held->cancel(held);
        CefRefCounting.ReleaseCef(held);
    }
}
