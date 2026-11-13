using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Ink and text turn as shapes do, about the middle of their box: a mark on a sheet turned a quarter turn
/// stands turned with it, and is picked and caught by a band where it is drawn, not where it would be unturned.</summary>
[TestFixture]
public class CanvasTurnedInkTests
{
    private static StrokeItem Horizontal()
    {
        var stroke = new StrokeItem(new Vector2<double>(0, 0), Brushes.White, 2);
        stroke.Add(new Vector2<double>(0, 0));
        stroke.Add(new Vector2<double>(100, 0));
        return stroke;
    }

    [Test]
    public void ATurnedStroke_IsHitWhereItIsDrawn()
    {
        var stroke = Horizontal();
        stroke.Transform = new CanvasTransform(90);

        Assert.Multiple(() =>
        {
            Assert.That(stroke.Pivot, Is.EqualTo(new Vector2<double>(50, 0)));
            Assert.That(stroke.HitTest(new Vector2<double>(50, 40), 1), Is.True, "on the turned line");
            Assert.That(stroke.HitTest(new Vector2<double>(90, 0), 1), Is.False, "where it would lie unturned");
        });
    }

    [Test]
    public void ATurnedStroke_IsCaughtByABandWhereItIsDrawn()
    {
        var stroke = Horizontal();
        stroke.Transform = new CanvasTransform(90);

        Assert.Multiple(() =>
        {
            Assert.That(stroke.Touches(new Rect(45, 35, 10, 10)), Is.True);
            Assert.That(stroke.Touches(new Rect(85, -5, 10, 10)), Is.False);
        });
    }

    [Test]
    public void ATurnedStroke_IsRubbedWhereItIsDrawn_AndItsPiecesStayThere()
    {
        var stroke = Horizontal();
        stroke.Transform = new CanvasTransform(90);
        var pieces = new List<StrokeItem>();

        var rubbed = stroke.Erase(new Vector2<double>(50, 0), 5, pieces);

        Assert.Multiple(() =>
        {
            Assert.That(rubbed, Is.True, "the middle of the turned line is under the eraser");
            Assert.That(pieces, Has.Count.EqualTo(2));
            foreach (var piece in pieces)
            {
                Assert.That(piece.Transform.IsSomething, Is.False, "the turn is laid into the points");
                Assert.That(piece.Bounds.X + piece.Bounds.Width / 2, Is.EqualTo(50).Within(1.5), "still upright at x = 50");
            }

            Assert.That(stroke.Erase(new Vector2<double>(90, 0), 5, []), Is.False, "where it would lie unturned");
        });
    }

    [Test]
    public void ATurnedStroke_IsWrittenToSvgWithItsTurnLaidIn()
    {
        var stroke = Horizontal();
        stroke.Transform = new CanvasTransform(90);

        var read = CanvasSvg.Load(CanvasSvg.Save([stroke]), out _).OfType<StrokeItem>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(read.Bounds.Width, Is.LessThan(5), "standing upright");
            Assert.That(read.Bounds.Height, Is.EqualTo(102).Within(1));
        });
    }

    [Test]
    public void ACopy_KeepsTheTurn()
    {
        var stroke = Horizontal();
        stroke.Transform = new CanvasTransform(90);
        var text = new TextItem(new Vector2<double>(0, 0), "mark", Brushes.White, 12) { Transform = new CanvasTransform(270) };

        Assert.Multiple(() =>
        {
            Assert.That(((ICanvasTransformed)stroke.Copy()).Transform, Is.EqualTo(stroke.Transform));
            Assert.That(((ICanvasTransformed)text.Copy()).Transform, Is.EqualTo(text.Transform));
        });
    }
}
