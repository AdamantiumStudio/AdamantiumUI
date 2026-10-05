using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="View"/>: a page, found by its class name unless it is given an id or a name.</summary>
public class ViewAutomationPeer : ContentControlAutomationPeer
{
    public ViewAutomationPeer(View owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Pane;

    public override string AutomationId => base.AutomationId is { Length: > 0 } id ? id : ClassName;
}
