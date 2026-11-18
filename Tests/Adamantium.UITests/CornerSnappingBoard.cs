using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;

namespace Adamantium.UITests;

/// <summary>A board whose drawing has one corner to snap to, at (100, 100), said to be a corner.</summary>
internal sealed class CornerSnappingBoard : DrawingBoard
{
    public static readonly Vector2<double> Corner = new(100, 100);

    protected override bool TrySnapToContent(Vector2<double> world, double reach, out Vector2<double> snapped,
        out string kind)
    {
        snapped = Corner;
        kind = "Corner";
        if ((world - Corner).Length() <= reach)
        {
            return true;
        }

        snapped = world;
        kind = null;
        return false;
    }
}
