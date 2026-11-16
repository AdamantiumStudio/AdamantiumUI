namespace Adamantium.UI.WebView;

/// <summary>A user name and its password, as a site or a proxy takes them to sign in.</summary>
public sealed record WebCredential(string UserName, string Password);
