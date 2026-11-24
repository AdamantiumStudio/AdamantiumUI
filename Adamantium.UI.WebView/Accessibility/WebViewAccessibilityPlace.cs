using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Accessibility;

/// <summary>Where a node stands: its rectangle against its container, the container, and a transform.</summary>
internal struct WebViewAccessibilityPlace
{
    public Rect Bounds;
    public int OffsetContainerId;
    public string Transform;
}
