using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="BusyIndicator"/>: a progress bar with no amount, shown while it runs.</summary>
public class BusyIndicatorAutomationPeer : UIComponentAutomationPeer
{
    public BusyIndicatorAutomationPeer(BusyIndicator owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.ProgressBar;

    public override bool IsOffscreen => !((BusyIndicator)Owner).IsActive || base.IsOffscreen;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
