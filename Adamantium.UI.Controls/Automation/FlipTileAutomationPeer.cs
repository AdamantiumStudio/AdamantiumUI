using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="FlipTile"/>: a button turned over as a click turns it - on is the back shown.</summary>
public class FlipTileAutomationPeer : UIComponentAutomationPeer, IToggleProvider
{
    public FlipTileAutomationPeer(FlipTile owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Button;

    public ToggleState ToggleState => ((FlipTile)Owner).IsFlipped ? ToggleState.On : ToggleState.Off;

    public void Toggle() => ((FlipTile)Owner).Flip();

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
