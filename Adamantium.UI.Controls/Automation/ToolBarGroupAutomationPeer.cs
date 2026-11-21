using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a section of a tool bar: a group called by its header, of the commands it holds.</summary>
public class ToolBarGroupAutomationPeer : UIComponentAutomationPeer
{
    public ToolBarGroupAutomationPeer(ToolBarGroup owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Group;

    protected override string NameCore() => (Owner as ToolBarGroup)?.Header as string;
}
