namespace Adamantium.UI.WebView;

/// <summary>A cookie of the profile every <see cref="WebView"/> shares (<see cref="WebViewEngine"/>): as a site set it,
/// or one the application sets.</summary>
public sealed record WebCookie
{
    /// <summary>The cookie's name.</summary>
    public required string Name { get; init; }

    /// <summary>The cookie's value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Where it goes. A domain cookie goes to its subdomains too and is read with a leading dot
    /// (".example.org"); a host cookie goes only to its host and is read as that host ("example.org"). Set, a domain
    /// makes a domain cookie; empty, or the address's host itself, makes a host cookie.</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>The path at and below which it goes, "/" for the whole site; set empty, the address's own directory
    /// ("/shop" for ".../shop/page").</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Whether it goes only over HTTPS.</summary>
    public bool IsSecure { get; init; }

    /// <summary>Whether it goes only with requests, unseen by the page's scripts.</summary>
    public bool IsHttpOnly { get; init; }

    /// <summary>Whether it goes with requests another site starts.</summary>
    public WebCookieSameSite SameSite { get; init; }

    /// <summary>When it expires; null for a session cookie, gone when the application closes.</summary>
    public DateTimeOffset? Expires { get; init; }

    /// <summary>When it was made, as Chromium keeps it; not set by the application.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>When it last went with a request, as Chromium keeps it; not set by the application.</summary>
    public DateTimeOffset? LastAccessed { get; init; }
}
