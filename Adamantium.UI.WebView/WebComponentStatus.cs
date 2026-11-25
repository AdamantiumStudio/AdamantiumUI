namespace Adamantium.UI.WebView;

/// <summary>Whether a component Chromium fetches (<see cref="WebViewComponents"/>) is installed in the profile, and
/// its version.</summary>
/// <param name="IsInstalled">Whether it is installed and used.</param>
/// <param name="Version">Its version, as "4.10.2934.0"; null while it is not installed.</param>
public sealed record WebComponentStatus(bool IsInstalled, string Version)
{
    /// <summary>Not installed.</summary>
    public static WebComponentStatus NotInstalled { get; } = new(false, null);
}
