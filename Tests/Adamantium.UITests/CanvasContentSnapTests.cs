using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A derived canvas pulls the points a tool places to points of what it shows - a drawing's corner - before
/// the grid, only within the snapping reach, and only for a tool that wants it.</summary>
public class CanvasContentSnapTests
{
    private static CornerSnappingBoard Board(ICanvasTool tool)
    {
        var board = new CornerSnappingBoard
        {
            Scene = new CanvasScene(), SnapToGrid = true, GridStyle = CanvasGridStyle.Lines, GridSpacing = 30, Tool = tool,
        };
        board.Measure(new Size<double>(400, 300));
        board.Arrange(new Rect(0, 0, 400, 300));
        return board;
    }

    [Test]
    public void APointOfTheDrawingNear_PullsBeforeTheGrid_ForAToolThatWantsIt()
    {
        var board = Board(new MeasuringTool());

        var pulled = board.TrySnap(new Vector2<double>(102, 99), out var snapped);

        Assert.Multiple(() =>
        {
            Assert.That(pulled, Is.True);
            Assert.That(snapped, Is.EqualTo(CornerSnappingBoard.Corner), "pulled to the grid, not to the drawing's corner");
        });
    }

    [Test]
    public void APointOfTheDrawingFarOff_LeavesTheGridToPull()
    {
        var board = Board(new MeasuringTool());

        var pulled = board.TrySnap(new Vector2<double>(153, 148), out var snapped);

        Assert.Multiple(() =>
        {
            Assert.That(pulled, Is.True, "the grid did not pull");
            Assert.That(snapped, Is.EqualTo(new Vector2<double>(150, 150)), "not the nearest crossing of the grid");
        });
    }

    [Test]
    public void AToolThatFollowsTheHand_IsNotPulledToTheDrawing()
    {
        var board = Board(new PenTool());

        board.TrySnap(new Vector2<double>(102, 99), out var snapped);

        Assert.That(snapped, Is.Not.EqualTo(CornerSnappingBoard.Corner), "ink was pulled to the drawing's corner");
    }
}
