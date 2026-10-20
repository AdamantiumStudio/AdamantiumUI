using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DropDownButton"/>: a button that expands into its items, which are its children.
/// </summary>
public class DropDownButtonAutomationPeer : ItemsControlAutomationPeer, IExpandCollapseProvider
{
    public DropDownButtonAutomationPeer(DropDownButton owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Button;

    protected override AutomationControlType ItemControlType => AutomationControlType.MenuItem;

    public ExpandCollapseState ExpandCollapseState =>
        ((DropDownButton)Owner).IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand()
    {
        if (!Owner.IsEnabled) throw new InvalidOperationException($"'{AutomationId}' is disabled.");

        Owner.SetCurrentValue(DropDownButton.IsDropDownOpenProperty, true);
    }

    public void Collapse() => Owner.SetCurrentValue(DropDownButton.IsDropDownOpenProperty, false);

    /// <summary>What the button shows: its content when it is text, else the text it draws.</summary>
    protected override string NameCore() => ((DropDownButton)Owner).Content as string ?? TextOf(Owner);

    /// <summary>The rows of the list, while it is open; a closed button has none to act on.</summary>
    protected override IReadOnlyList<AutomationPeer> ChildrenCore() =>
        ((DropDownButton)Owner).IsDropDownOpen ? base.ChildrenCore() : [];
}

/// <summary>The peer of a <see cref="DropDownButtonItem"/>: an entry of the list, invoked as a click on it.</summary>
public class DropDownButtonItemAutomationPeer : ButtonBaseAutomationPeer, IInvokeProvider
{
    public DropDownButtonItemAutomationPeer(DropDownButtonItem owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.MenuItem;

    /// <summary>Enabled, and picking it would do something (<see cref="DropDownButtonItem.IsAvailable"/>).</summary>
    public override bool IsEnabled => base.IsEnabled && ((DropDownButtonItem)Owner).IsAvailable;

    /// <summary>Picks the row; refused while picking it would do nothing.</summary>
    public void Invoke()
    {
        if (!IsEnabled) throw new InvalidOperationException($"'{AutomationId}' cannot be picked now.");

        ((DropDownButtonItem)Owner).PerformClick();
    }

    /// <summary>What the row shows - through its template, when it has one, rather than the item behind it.</summary>
    protected override string NameCore() =>
        Owner is ContentControl { ContentTemplate: not null } or ContentControl { ContentTemplateSelector: not null }
            ? TextOf(Owner) ?? base.NameCore()
            : base.NameCore();
}

/// <summary>The peer of a <see cref="SplitButton"/>: its body is invoked, its arrow expands its items.</summary>
public class SplitButtonAutomationPeer : DropDownButtonAutomationPeer, IInvokeProvider
{
    public SplitButtonAutomationPeer(SplitButton owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.SplitButton;

    /// <summary>Presses the body; refused while it cannot be pressed.</summary>
    public void Invoke()
    {
        var button = (SplitButton)Owner;
        if (!button.IsEnabled || !button.IsBodyEnabled) throw new InvalidOperationException($"'{AutomationId}' cannot be pressed now.");

        button.PerformClick();
    }
}

/// <summary>The peer of a <see cref="ToggleSplitButton"/>: a split button whose body is on or off.</summary>
public class ToggleSplitButtonAutomationPeer : SplitButtonAutomationPeer, IToggleProvider
{
    public ToggleSplitButtonAutomationPeer(ToggleSplitButton owner) : base(owner)
    {
    }

    public ToggleState ToggleState => ToggleButtonAutomationPeer.StateOf(((ToggleSplitButton)Owner).IsChecked);

    public void Toggle() => Invoke();
}
