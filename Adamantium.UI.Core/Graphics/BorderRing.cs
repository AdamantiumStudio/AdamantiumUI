using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Core.Graphics;

/// <summary>A border of four widths as a shape: the ring between the rect and the rect less each side's width, each
/// inner corner rounded by what is left of its radius - for what draws a border as paths rather than as the border pass.
/// </summary>
internal static class BorderRing
{
    /// <summary>The ring, filled by the even-odd rule; null when every width is none.</summary>
    public static Geometry Of(Rect rect, CornerRadius corners, Thickness thickness)
    {
        var left = Math.Max(0, thickness.Left);
        var top = Math.Max(0, thickness.Top);
        var right = Math.Max(0, thickness.Right);
        var bottom = Math.Max(0, thickness.Bottom);
        if (left + top + right + bottom <= 0)
        {
            return null;
        }

        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
        ring.Children.Add(new RectangleGeometry(rect, corners));
        var width = rect.Width - left - right;
        var height = rect.Height - top - bottom;
        if (width > 0 && height > 0)
        {
            ring.Children.Add(new RectangleGeometry(new Rect(rect.X + left, rect.Y + top, width, height),
                new CornerRadius(
                    Math.Max(0, corners.TopLeft - Math.Max(left, top)),
                    Math.Max(0, corners.TopRight - Math.Max(right, top)),
                    Math.Max(0, corners.BottomRight - Math.Max(right, bottom)),
                    Math.Max(0, corners.BottomLeft - Math.Max(left, bottom)))));
        }

        ring.RecalculateBounds();
        return ring;
    }
}
