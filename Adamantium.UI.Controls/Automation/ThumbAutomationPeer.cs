using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a grip that is dragged: a thumb, a splitter, a drag handle or a resize gripper.</summary>
public class ThumbAutomationPeer : UIComponentAutomationPeer
{
    public ThumbAutomationPeer(Control owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Thumb;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
