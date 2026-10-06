using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="Separator"/>: a rule between rows or groups.</summary>
public class SeparatorAutomationPeer : UIComponentAutomationPeer
{
    public SeparatorAutomationPeer(Separator owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Separator;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
