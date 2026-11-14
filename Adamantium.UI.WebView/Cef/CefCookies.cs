using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A cookie between CEF's struct and <see cref="WebCookie"/>.</summary>
internal static unsafe class CefCookies
{
    /// <summary>The cookie <paramref name="cookie"/> holds.</summary>
    public static WebCookie Read(CefCookie* cookie) => new()
    {
        Name = CefStrings.Read(&cookie->name),
        Value = CefStrings.Read(&cookie->value),
        Domain = CefStrings.Read(&cookie->domain),
        Path = CefStrings.Read(&cookie->path),
        IsSecure = cookie->secure != 0,
        IsHttpOnly = cookie->httponly != 0,
        SameSite = ToSameSite(cookie->same_site),
        Expires = cookie->has_expires != 0 ? CefTimes.Read(cookie->expires) : null,
        Created = CefTimes.Read(cookie->creation),
        LastAccessed = CefTimes.Read(cookie->last_access)
    };

    /// <summary>Writes <paramref name="cookie"/>, set for <paramref name="address"/>, into <paramref name="target"/>,
    /// for CEF to set; its strings are freed by <see cref="Clear"/>. A domain that is the address's host itself, as a
    /// host cookie is read, stays a host cookie.</summary>
    public static void Write(WebCookie cookie, Uri address, CefCookie* target)
    {
        *target = new CefCookie { size = (ulong)sizeof(CefCookie) };
        var domain = string.Equals(cookie.Domain, address.Host, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : cookie.Domain;
        CefStrings.Set(&target->name, cookie.Name);
        CefStrings.Set(&target->value, cookie.Value);
        CefStrings.Set(&target->domain, domain);
        CefStrings.Set(&target->path, cookie.Path);
        target->secure = cookie.IsSecure ? 1 : 0;
        target->httponly = cookie.IsHttpOnly ? 1 : 0;
        target->same_site = FromSameSite(cookie.SameSite);
        target->priority = CefCookiePriority.Medium;
        if (cookie.Expires is { } expires)
        {
            target->has_expires = 1;
            target->expires = CefTimes.Write(expires);
        }
    }

    /// <summary>Frees the strings <see cref="Write"/> put in <paramref name="target"/>.</summary>
    public static void Clear(CefCookie* target)
    {
        CefStrings.Clear(&target->name);
        CefStrings.Clear(&target->value);
        CefStrings.Clear(&target->domain);
        CefStrings.Clear(&target->path);
    }

    private static WebCookieSameSite ToSameSite(CefCookieSameSite sameSite) => sameSite switch
    {
        CefCookieSameSite.NoRestriction => WebCookieSameSite.None,
        CefCookieSameSite.LaxMode => WebCookieSameSite.Lax,
        CefCookieSameSite.StrictMode => WebCookieSameSite.Strict,
        _ => WebCookieSameSite.Unspecified
    };

    private static CefCookieSameSite FromSameSite(WebCookieSameSite sameSite) => sameSite switch
    {
        WebCookieSameSite.None => CefCookieSameSite.NoRestriction,
        WebCookieSameSite.Lax => CefCookieSameSite.LaxMode,
        WebCookieSameSite.Strict => CefCookieSameSite.StrictMode,
        _ => CefCookieSameSite.Unspecified
    };
}
