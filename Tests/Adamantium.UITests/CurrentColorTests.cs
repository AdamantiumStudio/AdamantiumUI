using System.Collections.Generic;
using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Image = Adamantium.UI.Controls.Image;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An icon written in CurrentColor takes the color of the text around whoever shows it, so one resource is dark
/// on a light surface and the on-accent color on an accent - the theme already puts that color in the Foreground.</summary>
[TestFixture]
public class CurrentColorTests
{
    private sealed class Recorder : IDrawingContext
    {
        public RecordingDrawingSession Session { get; } = new();

        public IDrawingSession ForControl(IUIComponent component) => Session;
    }

    private static Brush CurrentColor() => (Brush)TypeParser.Parse(CurrentColorBrush.Name, typeof(Brush));

    private static DrawingImage Icon(Brush stroke, Brush fill = null) => new()
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry { Rect = new Rect(0, 0, 16, 16) },
            Stroke = stroke,
            Brush = fill
        }
    };

    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;

    [Test]
    public void TheWord_IsReadAsTheMarker()
    {
        Assert.That(CurrentColor(), Is.TypeOf<CurrentColorBrush>());
        Assert.That(TypeParser.Parse("Coral", typeof(Brush)), Is.TypeOf<SolidColorBrush>(), "a color name is still a color");
    }

    [Test]
    public void AStrokeAndAFillInCurrentColor_AreDrawnWithTheColorTheyAreShownIn()
    {
        var session = new RecordingDrawingSession();

        Icon(CurrentColor(), CurrentColor()).Render(session, new Rect(0, 0, 16, 16), new SolidColorBrush(Colors.Red));

        var drawn = session.Geometries[0];
        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(drawn.Pen.Brush), Is.EqualTo(Colors.Red), "stroke");
            Assert.That(ColorOf(drawn.Brush), Is.EqualTo(Colors.Red), "fill");
        });
    }

    [Test]
    public void AShapeOfItsOwnColor_KeepsIt()
    {
        var session = new RecordingDrawingSession();

        Icon(new SolidColorBrush(Colors.Green)).Render(session, new Rect(0, 0, 16, 16), new SolidColorBrush(Colors.Red));

        Assert.That(ColorOf(session.Geometries[0].Pen.Brush), Is.EqualTo(Colors.Green));
    }

    [Test]
    public void ShownWithNoColor_TheMarkerPaintsNothingOfItsOwn()
    {
        var session = new RecordingDrawingSession();

        Icon(CurrentColor(), CurrentColor()).Render(session, new Rect(0, 0, 16, 16), null);

        Assert.Multiple(() =>
        {
            Assert.That(session.Geometries[0].Pen, Is.Null);
            Assert.That(session.Geometries[0].Brush, Is.Null);
        });
    }

    [Test]
    public void AnImage_DrawsTheIconInTheForegroundAroundIt_AndAgainWhenThatChanges()
    {
        var image = new Image { Width = 16, Height = 16, Source = Icon(CurrentColor()) };
        var window = new Window { Width = 100, Height = 100, Foreground = new SolidColorBrush(Colors.Red), Content = image };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);

        var first = new Recorder();
        image.Render(first);
        Assume.That(ColorOf(first.Session.Geometries[0].Pen.Brush), Is.EqualTo(Colors.Red), "the window's color");

        RenderDirty.Clear();
        window.Foreground = new SolidColorBrush(Colors.Blue);

        var dirty = new List<IUIComponent>();
        RenderDirty.SnapshotGeometryInto(dirty);
        Assert.That(dirty, Does.Contain(image), "a new color around the image did not ask it to draw again");

        var second = new Recorder();
        image.Render(second);
        Assert.That(ColorOf(second.Session.Geometries[0].Pen.Brush), Is.EqualTo(Colors.Blue));
    }
}
