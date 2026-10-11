using Adamantium.Mathematics;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

/// <summary>The cue under a finger or a pen held still: a long press has started, so its lift opens a menu instead of
/// tapping. The theme draws it; it stands centered on the contact, as big as its template comes out.</summary>
public class HoldIndicator : Adorner
{
    public HoldIndicator(IUIComponent root, Vector2 center) : base(root)
    {
        Center = center;
    }

    /// <summary>Where the contact is, in the adorned root's own space.</summary>
    public Vector2 Center { get; }

    public override Rect PlaceIn(Size desired) =>
        new(Center.X - desired.Width / 2, Center.Y - desired.Height / 2, desired.Width, desired.Height);
}
