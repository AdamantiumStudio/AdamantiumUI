using System.Collections.Generic;
using Adamantium.UI.Controls.Printing;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="PrintPreview"/>: a picture of the sheet, called by the name it is given.</summary>
public class PrintPreviewAutomationPeer : UIComponentAutomationPeer
{
    public PrintPreviewAutomationPeer(PrintPreview owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Image;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
