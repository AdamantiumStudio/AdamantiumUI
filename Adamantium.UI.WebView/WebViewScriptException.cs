namespace Adamantium.UI.WebView;

/// <summary>A script run in the page threw, or the page went away before it answered.</summary>
public sealed class WebViewScriptException : Exception
{
    public WebViewScriptException(string message) : base(message)
    {
    }
}
