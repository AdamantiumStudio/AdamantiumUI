using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The canvas's own pass draws the ground, under everything on the plane. A ground whose grid marks or axes
/// move with the camera is drawn again at each move; a plain one is not - drawing it again made the frame dirty, and the
/// renderer walked everything on the plane again where the move alone is matrices.</summary>
public class CanvasGroundTests
{
    private sealed class Probe : InfiniteCanvas
    {
        public void MarkDrawn() => IsGeometryValid = true;
    }

    private static Probe Sized(CanvasGridStyle style, Brush axis)
    {
        var canvas = new Probe { Scene = new CanvasScene(), GridStyle = style, AxisBrush = axis };
        canvas.Measure(new Size<double>(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));
        canvas.MarkDrawn();
        return canvas;
    }

    [TestCase(CanvasGridStyle.None, false, false)]
    [TestCase(CanvasGridStyle.Transparent, true, false)]
    [TestCase(CanvasGridStyle.None, true, true)]
    [TestCase(CanvasGridStyle.Dots, false, true)]
    [TestCase(CanvasGridStyle.Lines, false, true)]
    public void TheCameraMoving_DrawsTheGroundAgain_OnlyWhenItFollowsTheCamera(CanvasGridStyle style, bool axes,
        bool drawnAgain)
    {
        var moved = Sized(style, axes ? Brushes.Red : null);
        moved.Offset += new Vector2<double>(40, 25);
        var zoomed = Sized(style, axes ? Brushes.Red : null);
        zoomed.Scale *= 2;

        Assert.Multiple(() =>
        {
            Assert.That(!moved.IsGeometryValid, Is.EqualTo(drawnAgain), "moved");
            Assert.That(!zoomed.IsGeometryValid, Is.EqualTo(drawnAgain), "zoomed");
        });
    }
}
