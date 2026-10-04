using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ProgressBar"/> or a <see cref="RingProgressBar"/>: a number to read, not to set.</summary>
public class ProgressBarAutomationPeer : RangeBaseAutomationPeer
{
    public ProgressBarAutomationPeer(RangeBase owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.ProgressBar;

    public override bool IsReadOnly => true;
}
