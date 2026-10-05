using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a button that stays pressed: toggled the way a click toggles it.</summary>
public class ToggleButtonAutomationPeer : ButtonBaseAutomationPeer, IToggleProvider
{
    public ToggleButtonAutomationPeer(ToggleButton owner) : base(owner)
    {
    }

    public ToggleState ToggleState => StateOf(((ToggleButton)Owner).IsChecked);

    internal static ToggleState StateOf(bool? isChecked) => isChecked switch
    {
        true => ToggleState.On,
        false => ToggleState.Off,
        null => ToggleState.Indeterminate
    };

    public void Toggle() => ((ToggleButton)Owner).PerformClick();
}
