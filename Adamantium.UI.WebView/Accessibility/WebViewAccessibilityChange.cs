namespace Adamantium.UI.WebView.Accessibility;

/// <summary>A change of one of the page's accessibility trees as Chromium said it, read off the UI loop and taken in on
/// it: the tree's updates, in order, and the events that came with them.</summary>
internal sealed class WebViewAccessibilityChange
{
    public WebViewAccessibilityChange(string treeId)
    {
        TreeId = treeId;
    }

    public string TreeId { get; }

    public List<WebViewAccessibilityUpdate> Updates { get; } = [];

    public List<WebViewAccessibilityToldEvent> Events { get; } = [];
}
