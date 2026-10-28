using System.Collections;
using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.Panels;

/// <summary>An items host that opens a real gap where a dragged item would drop, the items around it moving aside,
/// instead of the drag's insertion caret.</summary>
public interface IDropGapPanel
{
    /// <summary>Whether the panel opens the gap at all; false shows the caret instead.</summary>
    bool SupportsDropGap { get; }

    /// <summary>The index in its owner's items a drop at <paramref name="point"/> would land before - the count for the
    /// end - or false. It comes from the panel's own slots, not from where the gap has moved the items to;
    /// <paramref name="lifted"/> are the panel's own items being dragged.</summary>
    bool TryGetDropSlot(Vector2<double> point, IList lifted, out int index);

    /// <summary>Opens the gap before the item at <paramref name="index"/>, or closes it at -1. <paramref name="lifted"/>
    /// are the panel's own items being dragged, which a panel may take out of its layout while the gap is open.</summary>
    void ShowDropGap(int index, IList lifted);
}
