using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What a canvas asks of the application before it draws, makes and frames things - for marks that belong to
/// the sheets of a document: each cut to its sheet, made only on one, and framed as one only with the marks of the same
/// sheet.</summary>
[TestFixture]
public class CanvasSheetHooksTests
{
    private static readonly Rect Sheet = new(0, 0, 100, 80);

    private sealed class SheetBoard : DrawingBoard
    {
        public bool Shared { get; set; } = true;

        protected internal override Rect ClipOf(ICanvasItem item) => Sheet.Contains(Middle(item.Bounds)) ? Sheet : Rect.Empty;

        public override bool CanDrawAt(Vector2<double> world) => Sheet.Contains(world);

        protected override bool SharesFrame(IReadOnlyList<ICanvasItem> selection) => Shared;
    }

    private sealed class Recorder : IDrawingContext
    {
        public RecordingDrawingSession Session { get; } = new();

        public IDrawingSession ForControl(IUIComponent component) => Session;
    }

    private static Vector2<double> Middle(Rect box) => new(box.X + box.Width / 2, box.Y + box.Height / 2);

    private static SheetBoard Board()
    {
        var board = new SheetBoard { Scene = new CanvasScene(), Ink = Brushes.White };
        board.Measure(new Size<double>(400, 300), force: true);
        board.Arrange(new Rect(0, 0, 400, 300));
        return board;
    }

    private static ShapeItem Box(double x, double y) =>
        new(CanvasShape.Rectangle, new Rect(x, y, 20, 20), Brushes.White, 1);

    private static CanvasPointerEventArgs PressAt(double x, double y) =>
        new() { World = new Vector2<double>(x, y), Button = MouseButtons.Left, ClickCount = 1 };

    private static RecordingDrawingSession Drawn(CanvasDrawLayer layer)
    {
        var recorder = new Recorder();
        layer.Render(recorder);
        return recorder.Session;
    }

    [Test]
    public void AnItemOnASheet_IsDrawnInAGroupCutToTheSheet_AndOneOffItIsNot()
    {
        var board = Board();
        var layer = new CanvasDrawLayer { Owner = board };
        layer.Sync([Box(10, 10), Box(300, 10)]);

        var groups = Drawn(layer).Groups;

        Assert.That(groups, Has.Count.EqualTo(1), "the item off the sheet is not cut");
        var clip = (RectangleGeometry)groups[0].Clip;
        Assert.Multiple(() =>
        {
            Assert.That(clip.Rect, Is.EqualTo(new Rect(0, 0, Sheet.Width, Sheet.Height)),
                "stated from the sheet's own corner");
            Assert.That(clip.CornerRadius, Is.EqualTo(CornerRadius.Empty), "a plain rectangle - the scissor path");
            Assert.That(groups[0].ClipPlacement, Is.Not.Null, "cut in the camera's placement");
        });
    }

    [Test]
    public void TheCut_StandsWhereTheCameraShowsTheSheet_AndFollowsIt()
    {
        var board = Board();
        var layer = new CanvasDrawLayer { Owner = board };
        layer.Sync([Box(10, 10)]);
        var placement = Drawn(layer).Groups.Single().ClipPlacement;

        AssertAtSheet(board, placement, "as recorded");

        board.PanBy(new Vector2<double>(15, -5));
        layer.Place();

        AssertAtSheet(board, placement, "after a pan, without drawing again");
    }

    private static void AssertAtSheet(InfiniteCanvas board, Placement placement, string when)
    {
        var corner = board.WorldToScreen(new Vector2<double>(Sheet.X, Sheet.Y));
        var matrix = placement.Matrix;
        Assert.Multiple(() =>
        {
            Assert.That(matrix.M41, Is.EqualTo(corner.X).Within(1e-3), when);
            Assert.That(matrix.M42, Is.EqualTo(corner.Y).Within(1e-3), when);
            Assert.That(matrix.M11, Is.EqualTo(board.Scale).Within(1e-6), when);
        });
    }

    [Test]
    public void ItemsCutToTheSameSheet_ShareOnePlacement()
    {
        var board = Board();
        var layer = new CanvasDrawLayer { Owner = board };
        layer.Sync([Box(10, 10), Box(50, 40)]);

        var groups = Drawn(layer).Groups;

        Assert.That(groups.Select(group => group.ClipPlacement).Distinct().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ToolsThatMakeThings_DoNotStartWhereTheyMayNotDraw()
    {
        foreach (var tool in new ICanvasTool[] { new ShapeTool(CanvasShape.Rectangle), new PenTool(), new CurveTool() })
        {
            var board = Board();

            var off = PressAt(300, 200);
            tool.OnPressed(board, off);
            Assert.That(off.Handled, Is.False, $"{tool.GetType().Name} started off the sheet");

            var on = PressAt(20, 20);
            tool.OnPressed(board, on);
            Assert.That(on.Handled, Is.True, $"{tool.GetType().Name} did not start on the sheet");
            tool.Cancel(board);
        }
    }

    [Test]
    public void TextIsNotStartedOffTheSheet()
    {
        var board = Board();
        var tool = new TextTool();

        var off = PressAt(300, 200);
        tool.OnPressed(board, off);

        Assert.That(off.Handled, Is.False);
    }

    [Test]
    public void ThingsNotFramedAsOne_OfferNoGrips()
    {
        var board = Board();
        var first = Box(10, 10);
        var second = Box(50, 40);
        board.Scene.Add(first);
        board.Scene.Add(second);
        board.SelectMany([first, second], false);

        Assert.That(board.OfferedHandles, Is.Not.EqualTo(CanvasHandles.None), "framed as one by default");

        board.Shared = false;

        Assert.Multiple(() =>
        {
            Assert.That(board.OfferedHandles, Is.EqualTo(CanvasHandles.None));
            var corner = board.FrameOf(board.SelectionBounds!.Value);
            Assert.That(board.HandleAt(new Vector2<double>(corner.X, corner.Y)), Is.EqualTo(CanvasHandle.None),
                "a corner grip no longer answers");
        });
    }
}
