using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's script dialogs - alert, confirm, prompt, and the question before a page is left or loaded
/// again: the view asks them over the page, Chromium's callback answered once the user has.</summary>
internal sealed unsafe class WebViewJsDialogHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewJsDialogHandler(WebViewBrowser browser) : base(sizeof(CefJsdialogHandler))
    {
        this.browser = browser;
        var handler = (CefJsdialogHandler*)Native;
        handler->on_jsdialog = &OnJsDialog;
        handler->on_before_unload_dialog = &OnBeforeUnloadDialog;
        handler->on_reset_dialog_state = &OnResetDialogState;
    }

    [UnmanagedCallersOnly]
    private static int OnJsDialog(CefJsdialogHandler* self, CefBrowser* cefBrowser, CefStringUtf16* originUrl,
        CefJsdialogType type, CefStringUtf16* messageText, CefStringUtf16* defaultPromptText,
        CefJsdialogCallback* callback, int* suppressMessage)
    {
        var kind = type switch
        {
            CefJsdialogType.Confirm => WebViewScriptDialogKind.Confirm,
            CefJsdialogType.Prompt => WebViewScriptDialogKind.Prompt,
            _ => WebViewScriptDialogKind.Alert
        };
        var handled = Ask(self, cefBrowser, callback, () => new WebViewScriptDialogRequest(kind,
            CefStrings.Read(originUrl), CefStrings.Read(messageText), CefStrings.Read(defaultPromptText), callback));
        // Not asked - a view that cannot show it: the script goes on as if it was cancelled, saying nothing.
        if (!handled)
        {
            *suppressMessage = 1;
        }

        return handled ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static int OnBeforeUnloadDialog(CefJsdialogHandler* self, CefBrowser* cefBrowser, CefStringUtf16* messageText,
        int isReload, CefJsdialogCallback* callback) =>
        Ask(self, cefBrowser, callback, () => new WebViewScriptDialogRequest(
            isReload != 0 ? WebViewScriptDialogKind.ReloadPage : WebViewScriptDialogKind.LeavePage, string.Empty,
            CefStrings.Read(messageText), string.Empty, callback)) ? 1 : 0;

    [UnmanagedCallersOnly]
    private static void OnResetDialogState(CefJsdialogHandler* self, CefBrowser* cefBrowser)
    {
        try
        {
            var owner = From<WebViewJsDialogHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                owner.OnScriptDialogsReset();
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
    }

    // Handed to the view; a request it does not take, or one that failed on the way, is answered as cancelled at once,
    // so the callback is never left held.
    private static bool Ask(CefJsdialogHandler* self, CefBrowser* cefBrowser, CefJsdialogCallback* callback,
        Func<WebViewScriptDialogRequest> make)
    {
        var handled = false;
        WebViewScriptDialogRequest request = null;
        try
        {
            var owner = From<WebViewJsDialogHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                request = make();
                handled = owner.OnScriptDialog(request);
                if (!handled)
                {
                    request.Answer(false, null);
                    handled = true;
                }
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
            if (request != null)
            {
                request.Answer(false, null);
                handled = true;
            }
        }

        CefRefCounting.ReleaseCef(callback);
        CefRefCounting.ReleaseCef(cefBrowser);
        return handled;
    }
}
