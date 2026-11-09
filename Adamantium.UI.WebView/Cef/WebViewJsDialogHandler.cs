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
        var asked = Ask(self, cefBrowser, callback, () => new WebViewScriptDialogRequest(kind,
            CefStrings.Read(originUrl), CefStrings.Read(messageText), CefStrings.Read(defaultPromptText), callback),
            refusedAnswer: null);
        // Not asked - a view that cannot show it: the script goes on as if it was cancelled, saying nothing.
        if (!asked)
        {
            *suppressMessage = 1;
        }

        return asked ? 1 : 0;
    }

    // A view that cannot ask lets the page go: a page kept by a question nobody saw could never be left.
    [UnmanagedCallersOnly]
    private static int OnBeforeUnloadDialog(CefJsdialogHandler* self, CefBrowser* cefBrowser, CefStringUtf16* messageText,
        int isReload, CefJsdialogCallback* callback) =>
        Ask(self, cefBrowser, callback, () => new WebViewScriptDialogRequest(
            isReload != 0 ? WebViewScriptDialogKind.ReloadPage : WebViewScriptDialogKind.LeavePage, string.Empty,
            CefStrings.Read(messageText), string.Empty, callback), refusedAnswer: true) ? 1 : 0;

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

    // Handed to the view. One it does not take is answered at once with refusedAnswer, or - null - left to CEF unasked;
    // one that failed on the way is answered as cancelled. Either way the callback is never left held.
    private static bool Ask(CefJsdialogHandler* self, CefBrowser* cefBrowser, CefJsdialogCallback* callback,
        Func<WebViewScriptDialogRequest> make, bool? refusedAnswer)
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
                    owner.ForgetScriptDialog(request);
                    if (refusedAnswer is { } answer)
                    {
                        request.Answer(answer, null);
                        handled = true;
                    }
                    else
                    {
                        // The request never answers: the reference it took goes, CEF answers for itself.
                        request.Forget();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
            if (request is { IsAnswered: false })
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
