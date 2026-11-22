using System.Collections.Generic;

namespace Adamantium.UI.Core.Collections;

/// <summary>One group of a <see cref="CollectionView"/>: its name and what falls in it - the items themselves at the last
/// level of <see cref="CollectionView.GroupDescriptions"/>, the groups of the next level above it - in the view's order.
/// </summary>
public sealed class CollectionViewGroup
{
    internal CollectionViewGroup(object name, IReadOnlyList<object> items, int itemCount, bool isBottomLevel)
    {
        Name = name;
        Items = items;
        ItemCount = itemCount;
        IsBottomLevel = isBottomLevel;
    }

    /// <summary>The name its items share, as the level's <see cref="GroupDescription"/> gives it.</summary>
    public object Name { get; }

    /// <summary>The items in it, or its groups of the next level.</summary>
    public IReadOnlyList<object> Items { get; }

    /// <summary>How many items it holds, at every level below it.</summary>
    public int ItemCount { get; }

    /// <summary>Whether <see cref="Items"/> are the items themselves, not groups.</summary>
    public bool IsBottomLevel { get; }

    public override string ToString() => Name?.ToString() ?? string.Empty;
}
