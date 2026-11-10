namespace Adamantium.UI.WebView.Cef;

/// <summary>An answer a site was given, as the profile's file keeps it.</summary>
internal sealed record WebViewKeptPermission(string Site, WebPermission Permission, bool Allowed);
