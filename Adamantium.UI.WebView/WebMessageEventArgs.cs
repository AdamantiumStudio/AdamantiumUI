namespace Adamantium.UI.WebView;

/// <summary>A message the page sent with <c>window.adamantium.postMessage(value)</c>: the value as JSON.</summary>
public sealed class WebMessageEventArgs : EventArgs
{
    public WebMessageEventArgs(string json)
    {
        Json = json;
    }

    /// <summary>What the page sent, as JSON.</summary>
    public string Json { get; }
}
