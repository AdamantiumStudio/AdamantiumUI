using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an element automation would look through - a border, a panel - that was given a name: a group of
/// what it holds, so a section's controls are told apart from another section's that read the same.</summary>
public class GroupAutomationPeer : UIComponentAutomationPeer
{
    public GroupAutomationPeer(UIComponent owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Group;
}
