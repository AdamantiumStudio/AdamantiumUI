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
        image.Measure(new Size(16, 16));
        Assume.That(first.InheritanceParent, Is.SameAs(image), "showing the icon makes the image its owner");

        image.Source = Icon();

        Assert.That(first.InheritanceParent, Is.Null);
    }
}
