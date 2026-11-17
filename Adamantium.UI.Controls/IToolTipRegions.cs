using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

/// <summary>Tooltips of the parts of one element, as a Win32 window has a tool for each rectangle: a link on a page, a
/// mark on a drawing. Set on the element with <see cref="ToolTipService.ToolTipRegionsProperty"/>; the card shows under
/// the pointer and stays while it rests on the same part.</summary>
public interface IToolTipRegions
{
    /// <summary>What to show at <paramref name="point"/>, in the element's own units, and the part of the element it holds
    /// for there; false where nothing is. The same part gives the same tip back, or the card starts over.</summary>
    bool TryGetToolTipAt(Vector2<double> point, out object toolTip, out Rect region);
}
