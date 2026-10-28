using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;
using NUnit.Framework;

namespace Adamantium.UITests;

// A DrawingImage is a shared resource that outlives whoever shows it, so it may own an element only while that element
// shows it - otherwise a theme icon keeps the first image to show it, and the page around that image, alive.
[TestFixture]
public class DrawingImageOwnerTests
{
    private static DrawingImage Icon() => new()
    {
        Drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = new SolidColorBrush(Colors.White)
        }
    };

    [Test]
    public void AnIconAnImageStoppedShowing_LetsGoOfIt()
    {
        var first = Icon();
        var image = new Image { Source = first };
        image.Measure(new Size<double>(16, 16));
        Assume.That(first.InheritanceParent, Is.SameAs(image), "showing the icon makes the image its owner");

        image.Source = Icon();

        Assert.That(first.InheritanceParent, Is.Null);
    }

    [Test]
    public void AnIconAnImageStoppedShowing_LetsGoOfWhatItsDrawingHung()
    {
        var transform = new Transform();
        var brush = new SolidColorBrush(Colors.White);
        var shape = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = brush };
        var group = new DrawingGroup { Transform = transform, Children = { shape } };
        var icon = new DrawingImage { Drawing = group };
        var image = new Image { Source = icon };
        image.Measure(new Size<double>(16, 16));
        Assume.That(transform.InheritanceParent, Is.SameAs(group));
        Assume.That(brush.InheritanceParent, Is.SameAs(shape));

        image.Source = Icon();

        Assert.Multiple(() =>
        {
            Assert.That(group.InheritanceParent, Is.Null);
            Assert.That(transform.InheritanceParent, Is.Null);
            Assert.That(shape.InheritanceParent, Is.Null);
            Assert.That(brush.InheritanceParent, Is.Null);
        });
    }

    [Test]
    public void AShownDrawing_HangsWhatIsSetOnItLater_AndLetsGoOfWhatItReplaced()
    {
        var group = new DrawingGroup();
        var shape = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) };
        group.Attach(new Image());
        var first = new SolidColorBrush(Colors.White);
        var transform = new Transform();
        var geometryTransform = new Transform();
        var oldGeometry = shape.Geometry;

        group.Transform = transform;
        group.Children.Add(shape);
        shape.Brush = first;
        shape.Brush = new SolidColorBrush(Colors.Red);
        shape.Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) { Transform = geometryTransform };

        Assert.Multiple(() =>
        {
            Assert.That(transform.InheritanceParent, Is.SameAs(group));
            Assert.That(shape.InheritanceParent, Is.SameAs(group));
            Assert.That(shape.Brush.InheritanceParent, Is.SameAs(shape));
            Assert.That(first.InheritanceParent, Is.Null, "the brush replaced is let go");
            Assert.That(shape.Geometry.InheritanceParent, Is.SameAs(shape));
            Assert.That(geometryTransform.InheritanceParent, Is.SameAs(shape));
            Assert.That(oldGeometry.InheritanceParent, Is.Null);
        });
    }

    [Test]
    public void ADrawingTwoImagesShow_IsHungAgain_WhenTheOtherLetsGo()
    {
        var drawing = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) };
        var first = new DrawingImage { Drawing = drawing };
        var second = new DrawingImage { Drawing = drawing };
        var a = new Image();
        var b = new Image();
        first.Attach(a);
        second.Attach(b);

        second.Detach(b);
        first.Attach(a);

        Assert.That(drawing.InheritanceParent, Is.SameAs(first));
    }

    [Test]
    public void ABrushADrawingBorrowed_GoesBackToWhereItHung()
    {
        var brush = new SolidColorBrush(Colors.White);
        var element = new Image();
        brush.InheritanceParent = element;
        var drawing = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = brush };
        drawing.Attach(new Image());
        Assume.That(brush.InheritanceParent, Is.SameAs(drawing));

        drawing.Detach();

        Assert.That(brush.InheritanceParent, Is.SameAs(element));
    }

    [Test]
    public void ABrushTwoDrawingsBorrowed_GoesBackToItsElement_WhenBothLetGo()
    {
        var brush = new SolidColorBrush(Colors.White);
        var element = new Image();
        brush.InheritanceParent = element;
        var a = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = brush };
        var b = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = brush };
        a.Attach(new Image());
        b.Attach(new Image());

        a.Detach();
        b.Detach();

        Assert.That(brush.InheritanceParent, Is.SameAs(element));
    }

    [Test]
    public void ABrushADrawingBorrowed_IsNotGivenBackToADiscardedElement()
    {
        var brush = new SolidColorBrush(Colors.White);
        var element = new Image();
        brush.InheritanceParent = element;
        var drawing = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = brush };
        drawing.Attach(new Image());
        DiscardedVisuals.Publish(element);
        while (DiscardedVisuals.Drain(int.MaxValue) > 0)
        {
        }

        drawing.Detach();

        Assert.That(brush.InheritanceParent, Is.Null);
    }

    [Test]
    public void ABrushBothFillAndStroke_StaysHung_WhenTheFillIsReplaced()
    {
        var shared = new SolidColorBrush(Colors.White);
        var drawing = new GeometryDrawing
        {
            Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)),
            Brush = shared,
            Stroke = shared,
        };
        drawing.Attach(new Image());

        drawing.Brush = new SolidColorBrush(Colors.Red);

        Assert.That(shared.InheritanceParent, Is.SameAs(drawing));
    }

    [Test]
    public void ADrawingTwiceInAGroup_StaysHung_WhenOneIsRemoved()
    {
        var part = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) };
        var group = new DrawingGroup { Children = { part, part } };
        group.Attach(new Image());

        group.Children.RemoveAt(1);

        Assert.That(part.InheritanceParent, Is.SameAs(group));
    }

    [Test]
    public void ClearingAGroup_LetsGoOfEachChild()
    {
        var shape = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) };
        var group = new DrawingGroup { Children = { shape } };
        group.Attach(new Image());
        Assume.That(shape.InheritanceParent, Is.SameAs(group));

        group.Children.Clear();

        Assert.That(shape.InheritanceParent, Is.Null);
    }
}
