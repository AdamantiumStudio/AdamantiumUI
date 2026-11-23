using System.Collections;
using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.Panels;

/// <summary>An items host that opens a real gap where a dragged item would drop, the items around it moving aside,
/// instead of the drag's insertion caret.</summary>
public interface IDropGapPanel
{
    /// <summary>Whether the panel opens the gap at all; false shows the caret instead.</summary>
    bool SupportsDropGap { get; }

    /// <summary>Where in its owner's items a drop at <paramref name="point"/> would land - in which group, where it shows
    /// groups - or false. It comes from the panel's own slots, not from where the gap has moved the items to;
    /// <paramref name="lifted"/> are the panel's own items being dragged.</summary>
    bool TryGetDropSlot(Vector2<double> point, IList lifted, out DropSlot slot);

    /// <summary>Opens the gap at <paramref name="slot"/>, or closes it at <see cref="DropSlot.None"/>.
    /// <paramref name="lifted"/> are the panel's own items being dragged, which a panel may take out of its layout while
    /// the gap is open.</summary>
    void ShowDropGap(DropSlot slot, IList lifted);
}
