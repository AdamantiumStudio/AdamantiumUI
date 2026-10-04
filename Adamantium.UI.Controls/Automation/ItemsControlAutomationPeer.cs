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

    public ItemsControlAutomationPeer(ItemsControl owner) : base(owner)
    {
    }

    /// <summary>What an item is to automation while it has no element of its own.</summary>
    protected virtual AutomationControlType ItemControlType => AutomationControlType.ListItem;

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

    /// <summary>The element made for item <paramref name="index"/>, or null while it has none. By default the one its own
    /// generator made; a control whose template lists the items itself says where to look.</summary>
    protected virtual UIComponent ContainerAt(int index) =>
        ((ItemsControl)Owner).ItemContainerGenerator.ContainerFromIndex(index) as UIComponent;

    private AutomationPeer PeerFor(object item)
    {
        if (item == null)
        {
            return new ItemAutomationPeer(this, null, ItemControlType);
        }

        if (!_itemPeers.TryGetValue(item, out var peer))
        {
            peer = new ItemAutomationPeer(this, item, ItemControlType);
            _itemPeers[item] = peer;
        }

        return peer;
    }
}
