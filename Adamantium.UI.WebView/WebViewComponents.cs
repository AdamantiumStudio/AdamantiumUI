namespace Adamantium.UI.WebView;

/// <summary>The parts Chromium fetches from Google after it starts and keeps up to date, which only the application
/// lets it fetch (<see cref="WebViewOptions.Components"/>). Chromium's other components - its machine learning models,
/// the lists and prompts of Chrome itself - are never fetched.</summary>
[Flags]
public enum WebViewComponents
{
    /// <summary>None: nothing is fetched.</summary>
    None = 0,

    /// <summary>Google's Widevine, which plays the video and music sites protect - their encrypted media. Software
    /// decryption: a site that asks for a browser Google has signed (VMP) refuses its licence. On Linux Chromium loads
    /// it as it starts, so one fetched while the application runs plays from its next start.</summary>
    Widevine = 1,

    /// <summary>The dictionaries a page's text is hyphenated with (CSS <c>hyphens: auto</c>); without them words are not
    /// broken on Windows and Linux.</summary>
    Hyphenation = 2,

    /// <summary>Google's list of certificates their owners revoked (CRLSet): a site still presenting one is refused.
    /// Without it such a certificate is taken until it expires.</summary>
    RevokedCertificates = 4,
}
