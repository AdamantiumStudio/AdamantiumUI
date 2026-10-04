using System.Collections.Generic;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>Stands for an item of an items control that has no element yet. It can be selected, when its control
/// selects, and brought into view, which makes its element; from then on that element's peer stands for the item.</summary>
public class ItemAutomationPeer : AutomationPeer, ISelectionItemProvider, IScrollItemProvider
{
    private readonly AutomationControlType _controlType;

    public ItemAutomationPeer(ItemsControlAutomationPeer itemsOwner, object item, AutomationControlType controlType)
    {
        ItemsOwner = itemsOwner;
        Item = item;
        _controlType = controlType;
    }

    /// <summary>The peer of the items control the item belongs to.</summary>
    public ItemsControlAutomationPeer ItemsOwner { get; }

    public object Item { get; }

    public override AutomationControlType ControlType => _controlType;

    public override string Name => Item as string ?? Item?.ToString() ?? string.Empty;

    public override string AutomationId => string.Empty;

    public override string HelpText => string.Empty;

    public override string ClassName => Item?.GetType().Name ?? "null";

    public override Rect BoundingRectangle => Rect.Empty;

    public override bool IsEnabled => ItemsOwner.IsEnabled;

    public override bool IsOffscreen => true;

    public override bool HasKeyboardFocus => false;

    public override bool IsKeyboardFocusable => false;

    public bool IsSelected => OwningSelector() is { } selector && (selector is ListBox list
        ? list.SelectedItems?.Contains(Item) == true
        : Equals(selector.SelectedItem, Item));

    public AutomationPeer SelectionContainer => ItemsOwner;

    public override IReadOnlyList<AutomationPeer> GetChildren() => [];

    public override AutomationPeer GetParent() => ItemsOwner;

    public override void SetFocus()
    {
    }

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.SelectionItem && OwningSelector() == null ? null : base.GetPattern(pattern);

    /// <summary>Makes the item the selected one, as the control's own selection would.</summary>
    public void Select() => OwningSelector()?.SetCurrentValue(Selector.SelectedItemProperty, Item);

    public void ScrollIntoView() => ((ItemsControl)ItemsOwner.Owner).ScrollIntoView(Item);

    private Selector OwningSelector() => ItemsOwner.Owner as Selector;
}
