using System.Runtime.CompilerServices;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.WebView.Accessibility;

namespace Adamantium.UI.WebView.Automation;

/// <summary>The peer of a <see cref="WebView"/>: a pane holding its own parts - the status bar and the like - and the
/// page's document, read from Chromium's accessibility tree. Asking for its children is what switches that tree on.
/// </summary>
public class WebViewAutomationPeer : UIComponentAutomationPeer
{
    private readonly ConditionalWeakTable<WebViewAccessibilityNode, WebViewPageNodeAutomationPeer> nodePeers = new();

    public WebViewAutomationPeer(WebView owner) : base(owner)
    {
    }

    internal WebView View => (WebView)Owner;

    public override AutomationControlType ControlType => AutomationControlType.Pane;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        View.WantAccessibility();
        var children = new List<AutomationPeer>(base.ChildrenCore());
        if (View.PageAccessibility?.Document?.Root is { } root)
        {
            children.Add(PeerOf(root));
        }

        return children;
    }

    /// <summary>The peer of <paramref name="node"/>, the same one each time it is asked for.</summary>
    internal WebViewPageNodeAutomationPeer PeerOf(WebViewAccessibilityNode node) =>
        nodePeers.GetValue(node, key => new WebViewPageNodeAutomationPeer(this, key));

    /// <summary>The peer of <paramref name="node"/>, if automation has seen it already; null if not.</summary>
    internal WebViewPageNodeAutomationPeer SeenPeerOf(WebViewAccessibilityNode node) =>
        nodePeers.TryGetValue(node, out var peer) ? peer : null;

    /// <summary>A rectangle of the page, in the view's units, on screen in pixels.</summary>
    internal Rect PageToScreen(Rect rect) => View.PageHost is { } host ? ScreenRect(host, rect) : Rect.Empty;
}
