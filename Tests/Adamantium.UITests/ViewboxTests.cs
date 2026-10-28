using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// The scaled child sits centered in room it does not fill, as a WPF Viewbox's does.
[TestFixture]
public class ViewboxTests
{
    [Test]
    public void AUniformChild_IsCentered_InTheRoomItDoesNotFill()
    {
        var child = new Border { Width = 10, Height = 10 };
        var box = new Viewbox { Child = child };

        box.Measure(new Size<double>(200, 100));
        box.Arrange(new Rect(0, 0, 200, 100));

        Assert.Multiple(() =>
        {
            Assert.That(child.RenderTransform.ScaleX, Is.EqualTo(10).Within(1e-9), "as tall as the room");
            Assert.That(child.Bounds.X, Is.EqualTo(50).Within(1e-9), "centered across");
            Assert.That(child.Bounds.Y, Is.EqualTo(0).Within(1e-9));
        });
    }
}
