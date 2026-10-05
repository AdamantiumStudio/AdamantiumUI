using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An icon is path data or a picture, and the presenter draws either: path data stroked in its color, a picture
/// as it is. Until it, a picture handed to the themes' icon slot went into a Path's Data and drew nothing.</summary>
[TestFixture]
public class IconPresenterTests
{
    private static IconPresenter Shown(object icon, Brush stroke = null)
    {
        var presenter = new IconPresenter { Icon = icon, Stroke = stroke };
        var window = new Window { Width = 100, Height = 100, Content = presenter };
        WindowExtension.UpdateTree(window);
        return presenter;
    }

    [Test]
    public void PathText_IsStrokedInItsColor()
    {
        var stroke = new SolidColorBrush(Colors.Coral);
        var presenter = Shown("M8,2 L14,5 L14,11 L8,14 L2,11 L2,5 Z", stroke);

        Assert.That(presenter.Child, Is.InstanceOf<Path>());
        var path = (Path)presenter.Child;
        Assert.That(path.Data, Is.Not.Null);
        Assert.That(path.Stroke, Is.SameAs(stroke));
    }

    [Test]
    public void AGeometry_IsStrokedAsItIs()
    {
        var geometry = new RectangleGeometry { Rect = new Rect(2, 2, 12, 12) };
        var presenter = Shown(geometry);

        Assert.That(((Path)presenter.Child).Data, Is.SameAs(geometry));
    }

    [Test]
    public void APicture_IsDrawnAsAPicture()
    {
        var picture = new DrawingImage
        {
            Drawing = new GeometryDrawing { Geometry = new RectangleGeometry { Rect = new Rect(0, 0, 16, 16) } }
        };
        var presenter = Shown(picture);

        Assert.That(presenter.Child, Is.InstanceOf<Image>());
        Assert.That(((Image)presenter.Child).Source, Is.SameAs(picture));
    }

    [Test]
    public void ANewColor_ReachesTheStroke()
    {
        var presenter = new IconPresenter { Icon = "M2,2 L14,14" };
        var window = new Window { Width = 100, Height = 100, Content = presenter };
        WindowExtension.UpdateTree(window);
        var stroke = new SolidColorBrush(Colors.Teal);

        presenter.Stroke = stroke;
        WindowExtension.UpdateTree(window);

        Assert.That(((Path)presenter.Child).Stroke, Is.SameAs(stroke));
    }

    [Test]
    public void NoIcon_DrawsNothing()
    {
        var presenter = Shown("M2,2 L14,14");

        presenter.Icon = null;

        Assert.That(presenter.Child, Is.Null);
    }
}
