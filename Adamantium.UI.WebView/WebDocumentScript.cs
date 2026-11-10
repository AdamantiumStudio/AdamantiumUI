namespace Adamantium.UI.WebView;

/// <summary>A script every new document of the page runs before its own: what
/// <see cref="WebViewBridge.AddScriptToNewDocument"/> gives, and what takes it away again.</summary>
public sealed class WebDocumentScript
{
    internal WebDocumentScript(string source)
    {
        Source = source;
    }

    /// <summary>The script's text.</summary>
    public string Source { get; }
}
