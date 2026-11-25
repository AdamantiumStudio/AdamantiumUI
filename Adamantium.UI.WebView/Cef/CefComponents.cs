namespace Adamantium.UI.WebView.Cef;

/// <summary>The ids Chromium's Component Updater knows <see cref="WebViewComponents"/> by, and the switch of our CEF
/// build that lets only the listed ones be registered (native/cef-build, chromium-allowed-components.patch).</summary>
internal static class CefComponents
{
    /// <summary>Comma-separated ids of the only components registered; present and empty, none is.</summary>
    public const string AllowedSwitch = "allowed-components";

    private const string WidevineId = "oimompecagnajdejgnnjijobebaeigek";
    private const string HyphenationId = "jamhcnnkihinmdlkakkaopbjbbcngflc";
    private const string RevokedCertificatesId = "hfnkpimlhhgieaddgfemjhofmfblmnib";

    /// <summary>The value of <see cref="AllowedSwitch"/> for <paramref name="components"/>: their ids, or empty for
    /// none.</summary>
    public static string Allowed(WebViewComponents components)
    {
        var ids = new List<string>(3);
        if (components.HasFlag(WebViewComponents.Widevine))
        {
            ids.Add(WidevineId);
        }

        if (components.HasFlag(WebViewComponents.Hyphenation))
        {
            ids.Add(HyphenationId);
        }

        if (components.HasFlag(WebViewComponents.RevokedCertificates))
        {
            ids.Add(RevokedCertificatesId);
        }

        return string.Join(',', ids);
    }

    /// <summary>The id of the one component <paramref name="component"/> names.</summary>
    public static string IdOf(WebViewComponents component) => component switch
    {
        WebViewComponents.Widevine => WidevineId,
        WebViewComponents.Hyphenation => HyphenationId,
        WebViewComponents.RevokedCertificates => RevokedCertificatesId,
        _ => throw new ArgumentException($"One component is named, not {component}.", nameof(component)),
    };
}
