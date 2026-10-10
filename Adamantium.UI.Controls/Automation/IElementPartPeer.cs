using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;

namespace Adamantium.UI.Controls.Automation;

/// <summary>A peer of a part of an element that is not an element itself - a link in a TextBlock: input made for it goes
/// to <see cref="Element"/> at <see cref="Middle"/>.</summary>
public interface IElementPartPeer
{
    /// <summary>The element the part is drawn in.</summary>
    UIComponent Element { get; }

    /// <summary>The middle of the part, in the element's own units; null while the part is not laid out on screen.</summary>
    Vector2? Middle { get; }
}
