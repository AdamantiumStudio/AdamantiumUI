using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ColorPickerButton"/>: a button that shows a color and opens a picker for it, the
/// picker its child while open.</summary>
public class ColorPickerButtonAutomationPeer : ColorAutomationPeer, IExpandCollapseProvider
{
    private readonly ColorPickerButton _button;

    public ColorPickerButtonAutomationPeer(ColorPickerButton owner) : base(owner, ColorPickerButton.SelectedColorProperty)
    {
        _button = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Button;

    public ExpandCollapseState ExpandCollapseState =>
        _button.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => _button.SetCurrentValue(ColorPickerButton.IsOpenProperty, true);

    public void Collapse() => _button.SetCurrentValue(ColorPickerButton.IsOpenProperty, false);

    /// <summary>The picker in its flyout, while it is open.</summary>
    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>();
        if (_button.GetTemplateChild("PART_Popup") is Popup { IsOpen: true, Child: Base.UIComponent shown })
        {
            Collect(shown, children);
        }

        return children;
    }
}
