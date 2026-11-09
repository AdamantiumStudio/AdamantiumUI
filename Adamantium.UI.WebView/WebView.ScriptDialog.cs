using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Localization;

namespace Adamantium.UI.WebView;

/// <summary>The dialogs a page's script asks, in the template's <c>PART_ScriptDialog</c> over the page only: the page
/// waits - it takes no pointer nor key - while the rest of the window goes on; the answer goes back to the script.
/// </summary>
public partial class WebView
{
    private WebViewScriptDialog scriptDialog;
    private WebViewScriptDialogRequest askedDialog;

    // Whether the page waits for its script's dialog to be answered: what the user does goes to the dialog alone.
    private bool PageWaits => askedDialog != null;

    private void TakeScriptDialog(WebViewScriptDialog dialog)
    {
        AnswerScriptDialog(false);
        scriptDialog = dialog;
        if (scriptDialog != null)
        {
            scriptDialog.SetCurrentValue(WebViewScriptDialog.AcceptCommandProperty,
                new WebViewCommand(() => AnswerScriptDialog(true), () => askedDialog != null));
            scriptDialog.SetCurrentValue(WebViewScriptDialog.CancelCommandProperty,
                new WebViewCommand(() => AnswerScriptDialog(false), () => askedDialog != null));
        }
    }

    bool IWebViewBrowserView.ScriptDialogRequested(WebViewBrowser from, WebViewScriptDialogRequest request)
    {
        if (!ReferenceEquals(browser, from) || scriptDialog == null)
        {
            return false;
        }

        AnswerScriptDialog(false);
        DismissPageMenu();
        HidePageTip();
        askedDialog = request;
        var leaving = request.Kind is WebViewScriptDialogKind.LeavePage or WebViewScriptDialogKind.ReloadPage;
        scriptDialog.SetCurrentValue(WebViewScriptDialog.TitleProperty, request.Kind switch
        {
            WebViewScriptDialogKind.LeavePage => WebViewStrings.LeaveSite,
            WebViewScriptDialogKind.ReloadPage => WebViewStrings.ReloadSite,
            _ => SiteOf(request.Origin) is { } site ? WebViewStrings.PageSays(site) : WebViewStrings.ThisPageSays
        });
        // A page leaving or loading again says nothing of its own, as browsers let none: what it would lose is said.
        scriptDialog.SetCurrentValue(WebViewScriptDialog.MessageProperty,
            leaving ? WebViewStrings.ChangesMayBeLost : request.Message);
        scriptDialog.SetCurrentValue(WebViewScriptDialog.TextProperty, request.DefaultText ?? string.Empty);
        scriptDialog.SetCurrentValue(WebViewScriptDialog.AcceptTextProperty, request.Kind switch
        {
            WebViewScriptDialogKind.LeavePage => WebViewStrings.Leave,
            WebViewScriptDialogKind.ReloadPage => WebViewStrings.Reload,
            _ => WebViewStrings.Ok
        });
        scriptDialog.SetCurrentValue(WebViewScriptDialog.KindProperty, request.Kind);
        RaiseScriptDialogCommands();
        LoopSignal.Post(() =>
        {
            if (askedDialog == request && scriptDialog != null)
            {
                KeyboardNavigation.MoveInto(scriptDialog);
            }
        });
        return true;
    }

    void IWebViewBrowserView.ScriptDialogsReset(WebViewBrowser from)
    {
        if (ReferenceEquals(browser, from))
        {
            AnswerScriptDialog(false);
        }
    }

    // The site that asks; null for a page with none - a data or file address, said as "this page".
    private static string SiteOf(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var address) && address.Host.Length > 0 ? address.Host : null;

    // Answers what the page asks - agreed, with the text typed for a prompt, or cancelled - and puts the dialog away;
    // the keyboard goes back to the page.
    private void AnswerScriptDialog(bool accepted)
    {
        if (askedDialog == null)
        {
            return;
        }

        var request = askedDialog;
        askedDialog = null;
        var text = scriptDialog?.Text;
        if (scriptDialog != null)
        {
            var hadFocus = scriptDialog.IsKeyboardFocusWithin;
            scriptDialog.SetCurrentValue(WebViewScriptDialog.KindProperty, WebViewScriptDialogKind.None);
            if (hadFocus)
            {
                Focus();
            }
        }

        RaiseScriptDialogCommands();
        request.Answer(accepted, text);
        // Stayed: the address asked for was not gone to, and the view shows the page's own again.
        if (!accepted && request.Kind is WebViewScriptDialogKind.LeavePage or WebViewScriptDialogKind.ReloadPage)
        {
            browser?.KeepAddress();
        }
    }

    private void RaiseScriptDialogCommands()
    {
        (scriptDialog?.AcceptCommand as WebViewCommand)?.RaiseCanExecuteChanged();
        (scriptDialog?.CancelCommand as WebViewCommand)?.RaiseCanExecuteChanged();
    }
}
