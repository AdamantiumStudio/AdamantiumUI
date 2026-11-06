using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonDropDownButton"/>: a button that opens its menu, the menu its child while open.
/// A <see cref="RibbonSplitButton"/> is a split button whose body is also pressed, and toggled when it is
/// <see cref="RibbonSplitButton.IsCheckable"/>.</summary>
public class RibbonDropDownButtonAutomationPeer : ButtonBaseAutomationPeer, IExpandCollapseProvider, IInvokeProvider,
    IToggleProvider
{
    private readonly RibbonDropDownButton _button;

    public RibbonDropDownButtonAutomationPeer(RibbonDropDownButton owner) : base(owner)
    {
        _button = owner;
    }

    public override AutomationControlType ControlType =>
        _button is RibbonSplitButton ? AutomationControlType.SplitButton : AutomationControlType.Button;

    public ExpandCollapseState ExpandCollapseState =>
        _button.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <summary>What the split button's command says of itself.</summary>
    public ToggleState ToggleState => ToggleButtonAutomationPeer.StateOf(_button.IsChecked);

    public override object GetPattern(PatternId pattern) => pattern switch
    {
        PatternId.Invoke when _button is not RibbonSplitButton => null,
        PatternId.Toggle when _button is not RibbonSplitButton { IsCheckable: true } => null,
        _ => base.GetPattern(pattern)
    };

    public void Expand() => _button.SetCurrentValue(RibbonDropDownButton.IsDropDownOpenProperty, true);

    public void Collapse() => _button.SetCurrentValue(RibbonDropDownButton.IsDropDownOpenProperty, false);

    /// <summary>Presses the body of a split button: what it runs, not its menu.</summary>
    public void Invoke() => _button.PerformClick();

    /// <summary>Presses the body of a split button, which its command turns on or off.</summary>
    public void Toggle() => _button.PerformClick();

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() =>
        _button.DropDownMenu is { IsOpen: true } menu && menu.GetAutomationPeer() is { } open ? [open] : [];
}
