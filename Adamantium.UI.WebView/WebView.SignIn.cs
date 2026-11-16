using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Localization;

namespace Adamantium.UI.WebView;

/// <summary>A site or a proxy asking the page's user to sign in, in the template's <c>PART_SignInDialog</c> over the
/// page only. The application is asked first (<see cref="WebViewBridge.SignInRequested"/>), then what the engine's store
/// kept (<see cref="WebViewEngine.Credentials"/>), then the user; the page waits while the dialog shows. Asked again
/// right after an answer - the name or the password was refused - the store is not asked and the dialog says why.
/// </summary>
public partial class WebView
{
    private readonly List<WebViewSignInRequest> askedSignIns = [];
    private WebViewSignInDialog signInDialog;
    // Who was last answered while the page loads: asked again before it loaded, the answer was refused.
    private WebSignInTarget answeredSignIn;

    private void TakeSignInDialog(WebViewSignInDialog dialog)
    {
        RefuseSignIns();
        signInDialog = dialog;
        if (signInDialog != null)
        {
            signInDialog.SetCurrentValue(WebViewSignInDialog.AcceptCommandProperty,
                new WebViewCommand(() => AnswerSignIn(true), () => askedSignIns.Count > 0));
            signInDialog.SetCurrentValue(WebViewSignInDialog.CancelCommandProperty,
                new WebViewCommand(() => AnswerSignIn(false), () => askedSignIns.Count > 0));
        }
    }

    bool IWebViewBrowserView.SignInRequested(WebViewBrowser from, WebViewSignInRequest request)
    {
        if (!ReferenceEquals(browser, from))
        {
            return false;
        }

        var isRetry = request.Target == answeredSignIn;
        if (Bridge is { } bridge)
        {
            var args = new WebSignInRequestedEventArgs(request.Target, request.Address, isRetry);
            bridge.OnSignInRequested(args);
            if (args.Cancel)
            {
                request.Refuse();
                return true;
            }

            if (args.Credential != null)
            {
                Answer(request, args.Credential);
                return true;
            }
        }

        if (!isRetry && Engine?.Credentials?.Find(request.Target) is { } kept)
        {
            Answer(request, kept);
            return true;
        }

        if (signInDialog == null)
        {
            return false;
        }

        askedSignIns.Add(request);
        if (askedSignIns.Count == 1)
        {
            ShowSignIn(request, isRetry);
        }

        return true;
    }

    // The page loaded: whoever asks from now on asks anew.
    private void SignInsLoaded() => answeredSignIn = null;

    private void ShowSignIn(WebViewSignInRequest request, bool isRetry)
    {
        DismissPageMenu();
        HidePageTip();
        var target = request.Target;
        var who = target.IsProxy || !IsDefaultPort(target) ? $"{target.Host}:{target.Port}" : target.Host;
        var named = !string.IsNullOrEmpty(target.Realm);
        signInDialog.SetCurrentValue(WebViewSignInDialog.TitleProperty,
            target.IsProxy ? WebViewStrings.ProxySignInTitle : WebViewStrings.SignInTitle);
        signInDialog.SetCurrentValue(WebViewSignInDialog.MessageProperty, (target.IsProxy, named) switch
        {
            (true, true) => WebViewStrings.ProxyAsksSignInTo(who, target.Realm),
            (true, false) => WebViewStrings.ProxyAsksSignIn(who),
            (false, true) => WebViewStrings.SiteAsksSignInTo(who, target.Realm),
            _ => WebViewStrings.SiteAsksSignIn(who)
        });
        signInDialog.SetCurrentValue(WebViewSignInDialog.IsInsecureProperty, !target.IsProxy && IsPlainHttp(request.Address));
        signInDialog.SetCurrentValue(WebViewSignInDialog.ErrorProperty, isRetry ? WebViewStrings.WrongSignIn : string.Empty);
        var store = Engine?.Credentials;
        signInDialog.SetCurrentValue(WebViewSignInDialog.UserNameProperty,
            store?.Find(target)?.UserName ?? string.Empty);
        signInDialog.SetCurrentValue(WebViewSignInDialog.PasswordProperty, string.Empty);
        signInDialog.SetCurrentValue(WebViewSignInDialog.CanRememberProperty, store?.CanSave == true);
        signInDialog.SetCurrentValue(WebViewSignInDialog.RememberProperty, false);
        signInDialog.SetCurrentValue(WebViewSignInDialog.IsOpenProperty, true);
        RaiseSignInCommands();
        LoopSignal.Post(() =>
        {
            if (askedSignIns.Count > 0 && askedSignIns[0] == request && signInDialog != null)
            {
                KeyboardNavigation.MoveInto(signInDialog);
            }
        });
    }

    private static bool IsDefaultPort(WebSignInTarget target) => target.Port is 80 or 443 or 0;

    private static bool IsPlainHttp(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttp;

    private void Answer(WebViewSignInRequest request, WebCredential credential)
    {
        answeredSignIn = request.Target;
        request.Answer(credential);
    }

    // Answers the sign-in shown - and every other asked by the same - with what is typed, kept when the user asked to,
    // or refused; the next one asked by someone else shows, or the dialog goes and the keyboard goes back to the page.
    private void AnswerSignIn(bool accepted)
    {
        if (askedSignIns.Count == 0)
        {
            return;
        }

        var target = askedSignIns[0].Target;
        WebCredential credential = null;
        if (accepted && signInDialog != null)
        {
            credential = new WebCredential(signInDialog.UserName ?? string.Empty, signInDialog.Password ?? string.Empty);
            if (signInDialog.Remember && Engine?.Credentials is { CanSave: true } store)
            {
                try
                {
                    store.Save(target, credential);
                }
                catch (Exception exception)
                {
                    Serilog.Log.Logger.Warning(exception, "The sign-in for {Host} could not be kept", target.Host);
                }
            }
        }

        for (var i = askedSignIns.Count - 1; i >= 0; i--)
        {
            var request = askedSignIns[i];
            if (request.Target != target && !request.IsAnswered)
            {
                continue;
            }

            askedSignIns.RemoveAt(i);
            if (credential != null)
            {
                Answer(request, credential);
            }
            else
            {
                request.Refuse();
            }
        }

        if (askedSignIns.Count > 0)
        {
            ShowSignIn(askedSignIns[0], askedSignIns[0].Target == answeredSignIn);
            return;
        }

        CloseSignInDialog();
    }

    // Refuses every sign-in asked: the page's browser goes, or the dialog does.
    private void RefuseSignIns()
    {
        for (var i = 0; i < askedSignIns.Count; i++)
        {
            askedSignIns[i].Refuse();
        }

        askedSignIns.Clear();
        answeredSignIn = null;
        CloseSignInDialog();
    }

    private void CloseSignInDialog()
    {
        if (signInDialog != null)
        {
            var hadFocus = signInDialog.IsKeyboardFocusWithin;
            signInDialog.SetCurrentValue(WebViewSignInDialog.IsOpenProperty, false);
            signInDialog.SetCurrentValue(WebViewSignInDialog.PasswordProperty, string.Empty);
            if (hadFocus)
            {
                Focus();
            }
        }

        RaiseSignInCommands();
    }

    private void RaiseSignInCommands()
    {
        (signInDialog?.AcceptCommand as WebViewCommand)?.RaiseCanExecuteChanged();
        (signInDialog?.CancelCommand as WebViewCommand)?.RaiseCanExecuteChanged();
    }
}
