using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ScrollBar"/> standing on its own; the bars of a scroll viewer are its parts, and it
/// scrolls through its own peer.</summary>
public class ScrollBarAutomationPeer : RangeBaseAutomationPeer
{
    public ScrollBarAutomationPeer(ScrollBar owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.ScrollBar;
}
