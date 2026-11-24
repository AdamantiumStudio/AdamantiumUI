namespace Adamantium.UI.WebView.Accessibility;

/// <summary>A node as Chromium said it, whole: what is not said now is not so any longer.</summary>
internal struct WebViewAccessibilityNodeData
{
    public int Id;
    public string Role, Name, Value, Description, HtmlId, HtmlTag, InputType, NameFrom, CheckedState, Restriction,
        ChildTreeId;
    public double ScrollX, ScrollY;
    public bool IsFocusable, IsEditable, IsInvisible, IsIgnored, IsCollapsed, IsExpanded;
    public int[] ChildIds;
    public WebViewAccessibilityPlace Place;
}
