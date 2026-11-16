namespace Adamantium.UI.WebView;

/// <summary>What asks a page's user to sign in: a site, or the proxy the profile goes through.</summary>
/// <param name="Host">The site's or the proxy's host name.</param>
/// <param name="Port">Its port.</param>
/// <param name="Realm">The realm the server names - which of its areas; empty when it names none.</param>
/// <param name="Scheme">How the name and password travel: "basic", "digest", "ntlm" and such.</param>
/// <param name="IsProxy">Whether a proxy asks, not the site.</param>
public sealed record WebSignInTarget(string Host, int Port, string Realm, string Scheme, bool IsProxy);
