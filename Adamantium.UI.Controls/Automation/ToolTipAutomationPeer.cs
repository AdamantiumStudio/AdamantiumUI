using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ToolTip"/>: called by what it says.</summary>
public class ToolTipAutomationPeer : ContentControlAutomationPeer
{
    public ToolTipAutomationPeer(ToolTip owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.ToolTip;

    protected override string NameCore() => base.NameCore() ?? TextOf(Owner);
}
