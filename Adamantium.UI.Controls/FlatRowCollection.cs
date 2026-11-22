using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Core.Collections;

namespace Adamantium.UI.Controls;

/// <summary>The flattened tree's row list. A branch's rows go in and out as one range change (InsertRange,
/// RemoveRange), so expanding a large branch costs O(realized rows) rather than O(inserted rows).</summary>
internal sealed class FlatRowCollection : TrackingCollection<TreeRow>
{
    /// <summary>Replace the whole list with <paramref name="rows"/> as ONE Reset.</summary>
    public void ResetTo(List<TreeRow> rows)
    {
        lock (SyncRoot)
        {
            ClearItems();
            InsertItems(0, CollectionsMarshal.AsSpan(rows));
        }

        NotifyReset();
    }
}
