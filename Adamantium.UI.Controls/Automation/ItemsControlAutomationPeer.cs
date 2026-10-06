using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an items control whose children are its items, in their order: each item's element where it
/// has one, and where it has none yet - a row a virtualizing list has not made, the list of a closed drop-down - an
/// <see cref="ItemAutomationPeer"/> that stands for it. Its template's own parts are not among them.</summary>
public class ItemsControlAutomationPeer : UIComponentAutomationPeer
{
    private readonly Dictionary<object, ItemAutomationPeer> _itemPeers = new(ReferenceEqualityComparer.Instance);
    private ScrollViewerAutomationPeer _scroll;

    public ItemsControlAutomationPeer(ItemsControl owner) : base(owner)
    {
    }

    /// <summary>What an item is to automation while it has no element of its own.</summary>
    protected virtual AutomationControlType ItemControlType => AutomationControlType.ListItem;

    /// <summary>Scrolls through the scroll viewer of its own template that holds its items, when it has one.</summary>
    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.Scroll ? ScrollOfItems() : base.GetPattern(pattern);

    private IScrollProvider ScrollOfItems()
    {
        for (var node = ((ItemsControl)Owner).ItemsHostPanel?.VisualParent; node != null && node != Owner; node = node.VisualParent)
        {
            if (node is ScrollViewer viewer)
            {
                if (_scroll?.Owner != viewer)
                {
                    _scroll = new ScrollViewerAutomationPeer(viewer);
                }

                return _scroll;
            }
        }

        return null;
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var owner = (ItemsControl)Owner;
        var children = new List<AutomationPeer>(owner.Items.Count);
        for (var index = 0; index < owner.Items.Count; index++)
        {
            var item = owner.Items[index];
            var container = ContainerAt(index) ?? item as UIComponent;
            children.Add(container?.GetAutomationPeer() ?? PeerFor(item));
        }

        return children;
    }

    /// <summary>What an item with no element is called: by default the item as text, and nothing for an item with no text
    /// of its own - the name of its class is not a name.</summary>
    protected internal virtual string NameOfItem(object item) => item switch
    {
        null => string.Empty,
        string text => text,
        _ when item.GetType().GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType == typeof(object) => string.Empty,
        _ => item.ToString()
    };

    /// <summary>Whether an item with no element is selected; false when the control does not select.</summary>
    protected internal virtual bool IsItemSelected(object item) => Owner switch
    {
        ListBox list => list.SelectedItems?.Contains(item) == true,
        Selector selector => Equals(selector.SelectedItem, item),
        _ => false
    };

    /// <summary>Whether the control selects its items at all.</summary>
    protected internal virtual bool CanSelectItems => Owner is Selector;

    /// <summary>Makes an item with no element the selected one, as the control's own selection would.</summary>
    protected internal virtual void SelectItem(object item) => Owner.SetCurrentValue(Selector.SelectedItemProperty, item);

    /// <summary>Adds an item with no element to what is selected, leaving the rest - where the control selects many.
    /// </summary>
    protected internal virtual void AddItemToSelection(object item)
    {
        if (Owner is ListBox list)
        {
            list.AddItemToSelection(item);
        }
        else if (!IsItemSelected(item))
        {
            throw new InvalidOperationException("One item is selected here at a time; select it instead.");
        }
    }

    /// <summary>Takes an item with no element out of what is selected, leaving the rest.</summary>
    protected internal virtual void RemoveItemFromSelection(object item)
    {
        if (Owner is ListBox list)
        {
            list.RemoveItemFromSelection(item);
        }
        else if (IsItemSelected(item))
        {
            throw new InvalidOperationException("Something is always selected here; select another item instead.");
        }
    }

    /// <summary>Scrolls until an item is in view, making its element.</summary>
    protected internal virtual void ScrollItemIntoView(object item) => ((ItemsControl)Owner).ScrollIntoView(item);

    /// <summary>The element made for item <paramref name="index"/>, or null while it has none. By default the one its own
    /// generator made; a control whose template lists the items itself says where to look.</summary>
    protected virtual UIComponent ContainerAt(int index) =>
        ((ItemsControl)Owner).ItemContainerGenerator.ContainerFromIndex(index) as UIComponent;

    /// <summary>Makes the stand-in for an item with no element.</summary>
    protected virtual ItemAutomationPeer CreateItemPeer(object item) => new(this, item, ItemControlType);

    private AutomationPeer PeerFor(object item)
    {
        if (item == null)
        {
            return CreateItemPeer(null);
        }

        if (!_itemPeers.TryGetValue(item, out var peer))
        {
            peer = CreateItemPeer(item);
            _itemPeers[item] = peer;
        }

        return peer;
    }
}
