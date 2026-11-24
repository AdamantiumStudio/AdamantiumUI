using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Accessibility;

/// <summary>One node of a page's accessibility tree as Chromium last said it: what it is, what it is called and holds,
/// its state, its children and where it stands - in the view's pixels, against the node it is placed in.</summary>
internal sealed class WebViewAccessibilityNode
{
    public WebViewAccessibilityNode(WebViewAccessibilityTree tree, int id)
    {
        Tree = tree;
        Id = id;
    }

    /// <summary>The tree it belongs to: the page's own, or a frame's.</summary>
    public WebViewAccessibilityTree Tree { get; }

    public int Id { get; }

    /// <summary>Chromium's role: <c>link</c>, <c>button</c>, <c>textField</c>, <c>checkBox</c>, <c>heading</c>...</summary>
    public string Role { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The element's id in the page's markup; empty when it has none.</summary>
    public string HtmlId { get; set; } = string.Empty;

    public string HtmlTag { get; set; } = string.Empty;

    /// <summary>An input element's type - <c>text</c>, <c>password</c>, <c>checkbox</c>...; empty for other elements.
    /// </summary>
    public string InputType { get; set; } = string.Empty;

    /// <summary>Where the name came from: <c>contents</c> when it is the text inside the node.</summary>
    public string NameFrom { get; set; } = string.Empty;

    /// <summary><c>true</c>, <c>false</c> or <c>mixed</c> for a check box; empty for anything else.</summary>
    public string CheckedState { get; set; } = string.Empty;

    /// <summary><c>disabled</c> or <c>readOnly</c>; empty when the node may be used.</summary>
    public string Restriction { get; set; } = string.Empty;

    /// <summary>The tree of the frame shown in the node; null for a node holding none.</summary>
    public string ChildTreeId { get; set; }

    public bool IsFocusable { get; set; }

    public bool IsEditable { get; set; }

    public bool IsInvisible { get; set; }

    public bool IsIgnored { get; set; }

    public bool IsCollapsed { get; set; }

    public bool IsExpanded { get; set; }

    /// <summary>Where it stands against <see cref="OffsetContainerId"/>, in the view's pixels.</summary>
    public Rect Location { get; set; }

    /// <summary>The node <see cref="Location"/> is measured from; 0 for the tree's own top-left.</summary>
    public int OffsetContainerId { get; set; }

    /// <summary>A turn or a scale it is drawn with, in its container's place; null when it has none.</summary>
    public WebViewAccessibilityTransform Transform { get; set; }

    /// <summary>How far it is scrolled, when it scrolls: what it holds is drawn that much up and to the left.</summary>
    public Vector2<double> Scroll { get; set; }

    public int[] ChildIds { get; set; } = [];

    /// <summary>The node holding it, as Chromium last named it among the holder's children; null for a tree's root.
    /// </summary>
    public WebViewAccessibilityNode Parent { get; set; }
}
