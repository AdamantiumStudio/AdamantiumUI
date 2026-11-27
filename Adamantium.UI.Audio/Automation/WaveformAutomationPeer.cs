using Adamantium.UI.Controls.Automation;

namespace Adamantium.UI.Audio.Automation;

/// <summary>The peer of a <see cref="Waveform"/>: a slider of the position, as the one it stands for.</summary>
public class WaveformAutomationPeer : RangeBaseAutomationPeer
{
    public WaveformAutomationPeer(Waveform owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Slider;
}
