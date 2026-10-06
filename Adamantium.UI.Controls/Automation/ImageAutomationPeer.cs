using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an <see cref="Image"/>: a picture, called by the name it is given.</summary>
public class ImageAutomationPeer : UIComponentAutomationPeer
{
    public ImageAutomationPeer(Image owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Image;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
