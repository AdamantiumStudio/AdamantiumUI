using System.Collections.Generic;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TextOnPath"/>: text named by what it reads.</summary>
public class TextOnPathAutomationPeer : UIComponentAutomationPeer
{
    public TextOnPathAutomationPeer(TextOnPath owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Text;

    protected override string NameCore() => ((TextOnPath)Owner).Text;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
