using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="Slider"/>.</summary>
public class SliderAutomationPeer : RangeBaseAutomationPeer
{
    public SliderAutomationPeer(Slider owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Slider;
}
