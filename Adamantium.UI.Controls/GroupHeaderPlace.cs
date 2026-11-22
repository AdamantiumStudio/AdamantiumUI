using Adamantium.UI.Core.Collections;

namespace Adamantium.UI.Controls;

/// <summary>Where a group's header stands among an <see cref="ItemsControl"/>'s items: a row of its own before the item
/// at <see cref="BeforeItem"/>, the group's first. An outer group's header comes before the header of its first inner
/// group, so several can stand before one item, outermost first. A collapsed group shows its header alone: its items
/// and its inner groups take no place.</summary>
public readonly struct GroupHeaderPlace
{
    public GroupHeaderPlace(CollectionViewGroup group, int level, int beforeItem, bool isExpanded)
    {
        Group = group;
        Level = level;
        BeforeItem = beforeItem;
        IsExpanded = isExpanded;
    }

    /// <summary>The group the header names.</summary>
    public CollectionViewGroup Group { get; }

    /// <summary>The level of grouping, 0 the outermost.</summary>
    public int Level { get; }

    /// <summary>The index in <see cref="ItemsControl.Items"/> of the group's first item.</summary>
    public int BeforeItem { get; }

    /// <summary>Whether the group's items are shown under its header.</summary>
    public bool IsExpanded { get; }
}
