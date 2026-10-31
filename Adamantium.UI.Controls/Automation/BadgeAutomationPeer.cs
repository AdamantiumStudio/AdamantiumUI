using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="Badge"/>: text, called by the number it shows; a dot has no name. The text in its
/// template is not a child, so the number is said once.</summary>
public class BadgeAutomationPeer : UIComponentAutomationPeer
{
    public BadgeAutomationPeer(Badge owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Text;

    protected override string NameCore() => ((Badge)Owner).IsDot ? string.Empty : ((Badge)Owner).Text;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
