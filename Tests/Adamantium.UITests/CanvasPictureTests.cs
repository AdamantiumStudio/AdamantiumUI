using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core.Media;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>Items of a plane drawn away from a canvas: through the same Render a canvas draws them with, one world unit
/// to one unit of the session, and nothing put on whole pixels - what a PDF mark's appearance is drawn with.</summary>
[TestFixture]
public class CanvasPictureTests
{
    // On a canvas a text is moved to a whole pixel to read sharp; a picture has no pixels, and the text stays where the
    // plane has it.
    [Test]
    public void AText_IsDrawnWhereThePlaneHasIt_NotOnAWholePixel()
    {
        var session = new RecordingDrawingSession();
        var text = new TextItem(new Vector2<double>(10.25, 20.75), "Mark", Brushes.Black, 12);

        CanvasPicture.Draw(new List<ICanvasItem> { text }, session);

        Assert.That(session.Texts, Has.Count.EqualTo(1), "the text was drawn");
        var transform = session.Texts[0].Transform;
        Assert.Multiple(() =>
        {
            Assert.That(transform.M41, Is.EqualTo(10.25).Within(1e-4), "x");
            Assert.That(transform.M42, Is.EqualTo(20.75).Within(1e-4), "y");
            Assert.That(transform.M11, Is.EqualTo(1).Within(1e-6), "one world unit is one unit drawn");
        });
    }

    [Test]
    public void Ink_IsDrawnFromItsOwnCorner_InWorldUnits()
    {
        var session = new RecordingDrawingSession();
        var ink = new StrokeItem(new Vector2<double>(30, 40), Brushes.Black, 4);
        ink.Add(new Vector2<double>(30, 40));
        ink.Add(new Vector2<double>(70, 40));

        CanvasPicture.Draw(new List<ICanvasItem> { ink }, session);

        Assert.That(session.Rectangles, Has.Count.EqualTo(1), "ink is drawn by its own brush");
        var brush = (InkBrush)session.Rectangles[0].Brush;
        Assert.Multiple(() =>
        {
            Assert.That(brush.Count, Is.EqualTo(2));
            Assert.That(brush.Thickness, Is.EqualTo(4));
            Assert.That(brush.Points[1].X - brush.Points[0].X, Is.EqualTo(40).Within(1e-4), "world units, unscaled");
        });
    }

    // A group is opened out as a canvas opens it: each of its items draws through its own placement, turn and all.
    [Test]
    public void AGroup_IsOpenedOut_AndATurnedItemInItKeepsItsTurn()
    {
        var session = new RecordingDrawingSession();
        var first = new TextItem(new Vector2<double>(0, 0), "One", Brushes.Black, 12)
        {
            Transform = new CanvasTransform(90)
        };
        var second = new TextItem(new Vector2<double>(0, 30), "Two", Brushes.Black, 12);
        var group = new GroupItem(new List<ICanvasItem> { first, second });

        CanvasPicture.Draw(new List<ICanvasItem> { group }, session);

        Assert.That(session.Texts, Has.Count.EqualTo(2));
        Assert.That(System.Math.Abs(session.Texts[0].Transform.M12), Is.EqualTo(1).Within(1e-4), "turned a quarter");
    }

    // An item is drawn by a copy of it: what the canvas keeps for it - a text's shaping at the canvas's zoom - stays.
    [Test]
    public void TheItemsThemselvesAreNotDrawn()
    {
        var session = new RecordingDrawingSession();
        var text = new TextItem(new Vector2<double>(0, 0), "Mark", Brushes.Black, 12);

        CanvasPicture.Draw(new List<ICanvasItem> { text }, session);
        CanvasPicture.Draw(new List<ICanvasItem> { text }, session);

        Assert.That(session.Texts, Has.Count.EqualTo(2));
        Assert.That(session.Texts[1].Layout, Is.Not.SameAs(session.Texts[0].Layout),
            "each picture shapes a copy's text, never the item's own layout");
    }
}
