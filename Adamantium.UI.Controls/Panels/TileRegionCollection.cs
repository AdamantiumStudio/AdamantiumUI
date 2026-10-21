using System.Collections.ObjectModel;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The parts of a <see cref="TileSplit"/>, in their order along it.</summary>
public sealed class TileRegionCollection : ObservableCollection<TileRegion>
{
    protected override void ClearItems()
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            RemoveAt(i);
        }
    }
}
