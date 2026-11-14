using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView;

public sealed unsafe partial class WebViewEngine
{
    /// <summary>The profile's cookies: those that go to <paramref name="address"/>, script-hidden (HttpOnly) ones too,
    /// or, with no address, all of them - the longest path first, then the oldest. On the UI loop; none with no CEF
    /// beside the application or, on Windows, before the main graphics device is made (CEF is started on its adapter) -
    /// as with <see cref="SetCookieAsync"/> and <see cref="DeleteCookiesAsync"/>.</summary>
    public Task<IReadOnlyList<WebCookie>> GetCookiesAsync(Uri address = null)
    {
        var manager = TakeCookieManager();
        if (manager == null)
        {
            return Task.FromResult<IReadOnlyList<WebCookie>>([]);
        }

        var visitor = new WebViewCookieVisitor();
        var text = new CefStringUtf16();
        try
        {
            int reached;
            if (address == null)
            {
                reached = manager->visit_all_cookies(manager, visitor.Shared);
            }
            else
            {
                CefStrings.Set(&text, address.AbsoluteUri);
                reached = manager->visit_url_cookies(manager, &text, 1, visitor.Shared);
            }

            if (reached == 0)
            {
                visitor.Refused();
            }
        }
        finally
        {
            CefStrings.Clear(&text);
            visitor.Release();
            CefRefCounting.ReleaseCef(manager);
        }

        return PumpUntilAnswered(visitor.Cookies);
    }

    /// <summary>Sets <paramref name="cookie"/> for <paramref name="address"/>, as a page there would: true once set,
    /// false for an address or cookie Chromium refuses - a value with ';' in it, a secure cookie for a page over http.
    /// On the UI loop; false with no CEF beside the application.</summary>
    public Task<bool> SetCookieAsync(Uri address, WebCookie cookie)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(cookie);
        var manager = TakeCookieManager();
        if (manager == null)
        {
            return Task.FromResult(false);
        }

        var callback = new WebViewSetCookieCallback();
        var text = new CefStringUtf16();
        var native = new CefCookie();
        try
        {
            CefStrings.Set(&text, address.AbsoluteUri);
            CefCookies.Write(cookie, address, &native);
            if (manager->set_cookie(manager, &text, &native, callback.Shared) == 0)
            {
                callback.Refused();
            }
        }
        finally
        {
            CefCookies.Clear(&native);
            CefStrings.Clear(&text);
            callback.Release();
            CefRefCounting.ReleaseCef(manager);
        }

        return PumpUntilAnswered(callback.Set);
    }

    /// <summary>Deletes cookies, and answers how many: with <paramref name="address"/> and <paramref name="name"/>
    /// the host's and domain's cookies of that name; with an address alone the host's cookies, not its domain's; with
    /// neither, every cookie of the profile. A name needs an address. On the UI loop; none with no CEF beside the
    /// application.</summary>
    public Task<int> DeleteCookiesAsync(Uri address = null, string name = null)
    {
        if (name != null && address == null)
        {
            throw new ArgumentException("A cookie's name is deleted for an address: with none every cookie would go.",
                nameof(name));
        }

        var manager = TakeCookieManager();
        if (manager == null)
        {
            return Task.FromResult(0);
        }

        var callback = new WebViewDeleteCookiesCallback();
        var text = new CefStringUtf16();
        var cookieName = new CefStringUtf16();
        try
        {
            if (address != null)
            {
                CefStrings.Set(&text, address.AbsoluteUri);
            }

            if (name != null)
            {
                CefStrings.Set(&cookieName, name);
            }

            if (manager->delete_cookies(manager, address != null ? &text : null, name != null ? &cookieName : null,
                    callback.Shared) == 0)
            {
                callback.Refused();
            }
        }
        finally
        {
            CefStrings.Clear(&cookieName);
            CefStrings.Clear(&text);
            callback.Release();
            CefRefCounting.ReleaseCef(manager);
        }

        return PumpUntilAnswered(callback.Deleted);
    }

    private CefCookieManager* TakeCookieManager()
    {
        if (!IsAvailable || !CanInitialize)
        {
            return null;
        }

        try
        {
            EnsureInitialized();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
        {
            Serilog.Log.Logger.Warning(exception, "CEF could not be started for the profile's cookies");
            return null;
        }

        return CefInterop.CefCookieManagerGetGlobalManager(null);
    }
}
