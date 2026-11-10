namespace Adamantium.UI.WebView;

/// <summary>An answer kept for a site: what it may use - or may not - from now on, without asking again
/// (<see cref="WebViewEngine.SitePermissions"/>).</summary>
public sealed class WebSitePermission
{
    public WebSitePermission(string site, WebPermission permission, bool isAllowed)
    {
        Site = site;
        Permission = permission;
        IsAllowed = isAllowed;
    }

    /// <summary>The site, as its origin: "https://example.org".</summary>
    public string Site { get; }

    /// <summary>One of <see cref="WebPermission"/>.</summary>
    public WebPermission Permission { get; }

    /// <summary>Allowed, or blocked.</summary>
    public bool IsAllowed { get; }
}
