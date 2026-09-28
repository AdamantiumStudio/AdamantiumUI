using System.Collections.Generic;
using Adamantium.Core.Collections;

namespace Adamantium.UI.Controls;

/// <summary>The flattened tree's row list, with bulk edits that raise one range notification, so expanding a large branch
/// costs O(realized rows) rather than O(inserted rows).</summary>
internal sealed class FlatRowCollection : TrackingCollection<TreeRow>
{
    /// <summary>Insert <paramref name="rows"/> at <paramref name="index"/> as ONE range Add.</summary>
    public void InsertMany(int index, List<TreeRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        lock (SyncRoot)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                InsertItem(index + i, rows[i]);
            }
        }

        NotifyAdd(rows, index);
    }

    /// <summary>Remove <paramref name="count"/> rows at <paramref name="index"/> as ONE range Remove.</summary>
    public void RemoveMany(int index, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var removed = new List<TreeRow>(count);
        lock (SyncRoot)
        {
            for (var i = 0; i < count; i++)
            {
                removed.Add(this[index + i]);
            }

            for (var i = 0; i < count; i++)
            {
                RemoveItem(index);   // RemoveItem shifts down, so `index` is always the next to drop
            }
        }

        NotifyRemove(removed, index);
    }

    /// <summary>Replace the whole list with <paramref name="rows"/> as ONE Reset.</summary>
    public void ResetTo(List<TreeRow> rows)
    {
        lock (SyncRoot)
        {
            ClearItems();
            for (var i = 0; i < rows.Count; i++)
            {
                InsertItem(i, rows[i]);
            }
        }

        NotifyReset();
    }
}
