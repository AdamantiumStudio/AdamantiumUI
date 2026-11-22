using Adamantium.UI.Core.Collections;

namespace Adamantium.UI.Controls.Panels;

/// <summary>Where a drop lands in a list: before the item at <see cref="Index"/> of its owner's items - the count for the
/// end. In a list that shows groups, also the <see cref="Group"/> it lands in and that group's items, from
/// <see cref="GroupStart"/> up to <see cref="GroupEnd"/>: the end of one group and the start of the next are the same
/// index, and only the group tells them apart.</summary>
public readonly struct DropSlot
{
    /// <summary>No slot: a gap closed.</summary>
    public static readonly DropSlot None = new(-1);

    /// <summary>A slot in a list without groups.</summary>
    public DropSlot(int index) : this(index, null, 0, -1)
    {
    }

    /// <summary>A slot in <paramref name="group"/>, whose items are those from <paramref name="groupStart"/> up to
    /// <paramref name="groupEnd"/>.</summary>
    public DropSlot(int index, CollectionViewGroup group, int groupStart, int groupEnd)
    {
        Index = index;
        Group = group;
        GroupStart = groupStart;
        GroupEnd = groupEnd;
    }

    /// <summary>The index in the owner's items the drop lands before; -1 for none.</summary>
    public int Index { get; }

    /// <summary>The group the drop lands in; null in a list without groups.</summary>
    public CollectionViewGroup Group { get; }

    /// <summary>The index of the group's first item.</summary>
    public int GroupStart { get; }

    /// <summary>The index past the group's last item; -1 in a list without groups.</summary>
    public int GroupEnd { get; }

    /// <summary>Whether this is a slot at all.</summary>
    public bool Exists => Index >= 0;
}
