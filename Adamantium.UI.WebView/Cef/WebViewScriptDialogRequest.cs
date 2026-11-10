using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// A dialog a page's script asked for, waiting to be answered: what it asks, and Chromium's callback held until the
/// answer - once. Lives on the UI loop, where CEF calls back.
/// </summary>
internal sealed unsafe class WebViewScriptDialogRequest
{
    private CefJsdialogCallback* callback;

    public WebViewScriptDialogRequest(WebViewScriptDialogKind kind, string origin, string message, string defaultText,
        CefJsdialogCallback* callback)
    {
        Kind = kind;
        Origin = origin;
        Message = message;
        DefaultText = defaultText;
        this.callback = callback;
        CefRefCounting.AddRefCef(callback);
    }

    /// <summary>What the script asks.</summary>
    public WebViewScriptDialogKind Kind { get; }

    /// <summary>The address of the page asking; empty for a page leaving or loading again, which asks of itself.
    /// </summary>
    public string Origin { get; }

    /// <summary>The script's own words; for a page leaving or loading again, what the page says, often nothing.
    /// </summary>
    public string Message { get; }

    /// <summary>The text a prompt starts with.</summary>
    public string DefaultText { get; }

    /// <summary>Whether it is answered.</summary>
    public bool IsAnswered => callback == null;

    /// <summary>Lets the callback go unanswered - CEF answers the dialog itself, as nobody asked it.</summary>
    public void Forget()
    {
        if (callback == null)
        {
            return;
        }

        var held = callback;
        callback = null;
        CefRefCounting.ReleaseCef(held);
    }

    /// <summary>Answers: OK - leave, reload - with <paramref name="text"/> for a prompt; or Cancel - stay.</summary>
    public void Answer(bool accepted, string text)
    {
        if (callback == null)
        {
            return;
        }

        var held = callback;
        callback = null;
        var input = new CefStringUtf16();
        try
        {
            CefStrings.Set(&input, accepted ? text : null);
            held->cont(held, accepted ? 1 : 0, &input);
        }
        finally
        {
            CefStrings.Clear(&input);
            CefRefCounting.ReleaseCef(held);
        }
    }
}
