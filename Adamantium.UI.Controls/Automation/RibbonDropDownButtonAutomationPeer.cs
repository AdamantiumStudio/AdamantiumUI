using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonDropDownButton"/>: a button that opens its menu, the menu its child while open.
/// A <see cref="RibbonSplitButton"/> is a split button whose body is also pressed.</summary>
public class RibbonDropDownButtonAutomationPeer : ButtonBaseAutomationPeer, IExpandCollapseProvider, IInvokeProvider
{
    private readonly RibbonDropDownButton _button;

    public RibbonDropDownButtonAutomationPeer(RibbonDropDownButton owner) : base(owner)
    {
        _button = owner;
    }

    public override AutomationControlType ControlType =>
        _button is RibbonSplitButton ? AutomationControlType.SplitButton : AutomationControlType.Button;

    public ExpandCollapseState ExpandCollapseState =>
        _button.IsChecked == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.Invoke && _button is not RibbonSplitButton ? null : base.GetPattern(pattern);

    public void Expand() => _button.SetCurrentValue(Primitives.ToggleButton.IsCheckedProperty, true);

    public void Collapse() => _button.SetCurrentValue(Primitives.ToggleButton.IsCheckedProperty, false);

    /// <summary>Presses the body of a split button: what it runs, not its menu.</summary>
    public void Invoke() => _button.PerformClick();

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() =>
        _button.DropDownMenu is { IsOpen: true } menu && menu.GetAutomationPeer() is { } open ? [open] : [];
}
