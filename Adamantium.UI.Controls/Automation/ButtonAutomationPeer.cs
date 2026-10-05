using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a button that does one thing when pressed.</summary>
public class ButtonAutomationPeer : ButtonBaseAutomationPeer, IInvokeProvider
{
    public ButtonAutomationPeer(ButtonBase owner) : base(owner)
    {
    }

    public void Invoke() => ((ButtonBase)Owner).PerformClick();
}
