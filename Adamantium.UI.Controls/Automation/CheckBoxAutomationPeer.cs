using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="CheckBox"/>.</summary>
public class CheckBoxAutomationPeer : ToggleButtonAutomationPeer
{
    public CheckBoxAutomationPeer(CheckBox owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.CheckBox;
}
