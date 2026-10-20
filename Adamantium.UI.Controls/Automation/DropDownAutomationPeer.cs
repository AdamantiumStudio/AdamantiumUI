using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DropDown"/>: a combo box whose items - in its list, open or not - are its children,
/// and whose value is what it shows picked.</summary>
public class DropDownAutomationPeer : ItemsControlAutomationPeer, ISelectionProvider, IExpandCollapseProvider, IValueProvider
{
    public DropDownAutomationPeer(DropDown owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.ComboBox;

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => false;

    public ExpandCollapseState ExpandCollapseState =>
        ((DropDown)Owner).IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <summary>What it shows picked: the text in its box, else the picked item's name; empty with nothing picked. An
    /// editable one's is the text typed into it.</summary>
    public string Value => Owner is DropDown { IsEditable: true } editable
        ? editable.TypedText ?? string.Empty
        : ((DropDown)Owner).SelectedItem == null
            ? string.Empty
            : TextOf(Owner) ?? GetSelection().FirstOrDefault()?.Name ?? string.Empty;

    public bool IsReadOnly => !((DropDown)Owner).IsEditable;

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        [.. GetChildren().Where(child => child.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider { IsSelected: true })];

    public void Expand() => Owner.SetCurrentValue(DropDown.IsDropDownOpenProperty, true);

    public void Collapse() => Owner.SetCurrentValue(DropDown.IsDropDownOpenProperty, false);

    /// <summary>An editable one takes it as typed and Enter pressed.</summary>
    public void SetValue(string value)
    {
        if (Owner is not DropDown { IsEditable: true } editable)
        {
            throw new InvalidOperationException($"'{AutomationId}' takes no typed value: select one of its items.");
        }

        editable.TakeText(value);
    }

    /// <summary>Its placeholder, when it is text and nothing else names it.</summary>
    protected override string NameCore() => ((DropDown)Owner).Placeholder as string;
}
