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
    // How soon after an answer the same asking again means the answer was refused: a server's refusal comes back at
    // once, while a site asking much later - its session over - asks anew.
    private const long RetryWindowMs = 10_000;

    private readonly List<WebViewSignInRequest> askedSignIns = [];
    private WebViewSignInDialog signInDialog;
    // The last answer while the page loads: who it went to, the name, whether the store gave it, and when.
    private WebSignInTarget answeredSignIn;
    private string answeredUserName;
    private bool answeredFromStore;
    private long answeredAt;

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

        var isRetry = IsRetry(request.Target);
        // What the store gave was refused: it is not offered again.
        if (isRetry && answeredFromStore)
        {
            ForgetKept(request.Target);
        }

        if (Bridge is { } bridge)
        {
            var args = new WebSignInRequestedEventArgs(request.Target, request.Address, isRetry);
            try
            {
                bridge.OnSignInRequested(args);
            }
            catch (Exception exception)
            {
                // The application's handler failed: the user is asked instead.
                Serilog.Log.Logger.Error(exception, "A sign-in handler threw; the user is asked");
                args.Cancel = false;
                args.Credential = null;
            }

            // The handler may have taken the page away - stopped or replaced its browser - or answered it itself.
            if (!ReferenceEquals(browser, from) || request.IsAnswered)
            {
                request.Refuse();
                return true;
            }

            if (args.Cancel)
            {
                request.Refuse();
                return true;
            }

            if (args.Credential != null)
            {
                Answer(request, args.Credential, false);
                return true;
            }
        }

        if (!isRetry && FindKept(request.Target) is { } kept)
        {
            Answer(request, kept, true);
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

    private bool IsRetry(WebSignInTarget target) =>
        target == answeredSignIn && Environment.TickCount64 - answeredAt < RetryWindowMs;

    // What the store keeps for the target; null for nothing, or a store that failed to say.
    private WebCredential FindKept(WebSignInTarget target)
    {
        if (Engine?.Credentials is not { } store)
        {
            return null;
        }

        try
        {
            return store.Find(target);
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Warning(exception, "The kept sign-in for {Host} could not be read", target.Host);
            return null;
        }
    }

    private void ForgetKept(WebSignInTarget target)
    {
        try
        {
            Engine?.Credentials?.Forget(target);
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Warning(exception, "The refused sign-in for {Host} could not be forgotten", target.Host);
        }
    }

    private void ShowSignIn(WebViewSignInRequest request, bool isRetry)
    {
        DismissPageMenu();
        HidePageTip();
        var target = request.Target;
        var who = target.IsProxy || !IsDefaultPort(target, request.Address) ? $"{target.Host}:{target.Port}" : target.Host;
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
        // Asked again, the name just sent; else the one kept for the site: only the password is typed anew.
        var userName = isRetry ? answeredUserName : FindKept(target)?.UserName;
        signInDialog.SetCurrentValue(WebViewSignInDialog.UserNameProperty, userName ?? string.Empty);
        signInDialog.SetCurrentValue(WebViewSignInDialog.PasswordProperty, string.Empty);
        signInDialog.SetCurrentValue(WebViewSignInDialog.CanRememberProperty, CanKeep());
        signInDialog.SetCurrentValue(WebViewSignInDialog.RememberProperty, false);
        signInDialog.SetCurrentValue(WebViewSignInDialog.IsOpenProperty, true);
        RaiseSignInCommands();
        LoopSignal.Post(() =>
        {
            if (askedSignIns.Count > 0 && askedSignIns[0] == request && signInDialog != null)
            {
                signInDialog.FocusField();
            }
        });
    }

    // Whether the port is the one the page's scheme goes to anyway, and need not be said.
    private static bool IsDefaultPort(WebSignInTarget target, string address)
    {
        if (target.Port == 0)
        {
            return true;
        }

        return Uri.TryCreate(address, UriKind.Absolute, out var parsed) &&
               (parsed.Scheme == Uri.UriSchemeHttp && target.Port == 80 ||
                parsed.Scheme == Uri.UriSchemeHttps && target.Port == 443);
    }

    private static bool IsPlainHttp(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttp;

    private bool CanKeep()
    {
        try
        {
            return Engine?.Credentials?.CanSave == true;
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Warning(exception, "The sign-in store could not say whether it keeps sign-ins");
            return false;
        }
    }

    private void Answer(WebViewSignInRequest request, WebCredential credential, bool fromStore)
    {
        answeredSignIn = request.Target;
        answeredUserName = credential.UserName;
        answeredFromStore = fromStore;
        answeredAt = Environment.TickCount64;
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
            if (signInDialog.Remember && CanKeep())
            {
                try
                {
                    Engine.Credentials.Save(target, credential);
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
                Answer(request, credential, false);
            }
            else
            {
                request.Refuse();
            }
        }

        if (askedSignIns.Count > 0)
        {
            ShowSignIn(askedSignIns[0], IsRetry(askedSignIns[0].Target));
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
