using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DatePicker"/>, <see cref="TimePicker"/> or <see cref="DateTimePicker"/>: a combo box
/// whose value is its date and time, as its field's, that expands to what its button opens; its parts and, while open,
/// what it opens are its children.</summary>
public class DateTimePickerAutomationPeer : DateTimeFieldAutomationPeer, IExpandCollapseProvider
{
    private readonly DateTimePickerBase _picker;

    public DateTimePickerAutomationPeer(DateTimePickerBase owner) : base(owner)
    {
        _picker = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.ComboBox;

    public ExpandCollapseState ExpandCollapseState =>
        _picker.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => Owner.SetCurrentValue(DateTimePickerBase.IsDropDownOpenProperty, true);

    public void Collapse() => Owner.SetCurrentValue(DateTimePickerBase.IsDropDownOpenProperty, false);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>(base.ChildrenCore());
        if (_picker.IsDropDownOpen && _picker.DropDownContent?.GetAutomationPeer() is { } content)
        {
            children.Add(content);
        }

        return children;
    }
}
