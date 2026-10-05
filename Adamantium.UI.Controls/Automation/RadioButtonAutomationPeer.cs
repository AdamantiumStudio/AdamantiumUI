using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RadioButton"/>: one choice of its group, selected the way a click selects it.</summary>
public class RadioButtonAutomationPeer : ButtonBaseAutomationPeer, ISelectionItemProvider
{
    private readonly RadioButton _radio;

    public RadioButtonAutomationPeer(RadioButton owner) : base(owner)
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
