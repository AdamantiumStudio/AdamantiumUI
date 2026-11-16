namespace Adamantium.UI.WebView;

/// <summary>How the profile goes out to the network.</summary>
public enum WebViewProxyMode
{
    /// <summary>As the system says: Windows' settings; on Linux the environment and the desktop's settings.</summary>
    System,

    /// <summary>Straight to every site, whatever the system says.</summary>
    Direct,

    /// <summary>Through <see cref="WebViewProxy.Server"/>, but for the addresses it is bypassed for.</summary>
    Server,

    /// <summary>As the proxy auto-config script at <see cref="WebViewProxy.ScriptAddress"/> says.</summary>
    Script
}
