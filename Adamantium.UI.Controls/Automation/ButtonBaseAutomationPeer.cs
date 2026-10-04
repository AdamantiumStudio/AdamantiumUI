using System.Collections.Generic;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>What every button's peer shares: a button, called by its label, with nothing of its template below it.</summary>
public abstract class ButtonBaseAutomationPeer : ContentControlAutomationPeer
{
    protected ButtonBaseAutomationPeer(ButtonBase owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Button;

    protected override string NameCore() => base.NameCore() ?? TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
