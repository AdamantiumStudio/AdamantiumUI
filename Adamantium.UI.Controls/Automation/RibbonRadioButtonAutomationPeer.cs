using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonRadioButton"/>: one choice of its group's set, selected the way a press
/// selects it. A choice is never toggled off, so it offers no toggle.</summary>
public class RibbonRadioButtonAutomationPeer : ButtonBaseAutomationPeer, ISelectionItemProvider
{
    private readonly RibbonRadioButton _radio;

    public RibbonRadioButtonAutomationPeer(RibbonRadioButton owner) : base(owner)
    {
        _radio = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.RadioButton;

    public bool IsSelected => _radio.IsChecked == true;

    public AutomationPeer SelectionContainer => GetParent();

    public void Select()
    {
        if (!IsSelected)
        {
            _radio.PerformClick();
        }
    }
}
