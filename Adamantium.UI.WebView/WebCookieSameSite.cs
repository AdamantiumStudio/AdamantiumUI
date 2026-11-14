namespace Adamantium.UI.WebView;

/// <summary>Whether a cookie goes with requests a page of another site starts (its <c>SameSite</c>).</summary>
public enum WebCookieSameSite
{
    /// <summary>Not said: the browser's default, as <see cref="Lax"/>.</summary>
    Unspecified,

    /// <summary>With every request, of any site; the cookie must be secure.</summary>
    None,

    /// <summary>With the site's own requests and with going to it from another site.</summary>
    Lax,

    /// <summary>With the site's own requests only.</summary>
    Strict
}
