namespace Adamantium.UI.WebView;

/// <summary>How a profile goes out to the network - <see cref="WebViewEngine.Proxy"/>. The system's way by default.
/// </summary>
public sealed record WebViewProxy
{
    /// <summary>The system's way.</summary>
    public static WebViewProxy System { get; } = new();

    /// <summary>Which way.</summary>
    public WebViewProxyMode Mode { get; init; }

    /// <summary>What the proxy server speaks, for <see cref="WebViewProxyMode.Server"/>.</summary>
    public WebViewProxyKind Kind { get; init; }

    /// <summary>The proxy server's host, for <see cref="WebViewProxyMode.Server"/>; null is taken as none.</summary>
    public string Server { get; init => field = value ?? string.Empty; } = string.Empty;

    /// <summary>The proxy server's port, for <see cref="WebViewProxyMode.Server"/>.</summary>
    public int Port { get; init; }

    /// <summary>The addresses that go straight, not through the server: hosts, "*.example.org" for a domain and its
    /// subdomains, "127.0.0.1", and such. Null is taken as none.</summary>
    public IReadOnlyList<string> Bypass { get; init => field = value ?? []; } = [];

    /// <summary>The address of the proxy auto-config script, for <see cref="WebViewProxyMode.Script"/>; null is taken
    /// as none.</summary>
    public string ScriptAddress { get; init => field = value ?? string.Empty; } = string.Empty;
}
