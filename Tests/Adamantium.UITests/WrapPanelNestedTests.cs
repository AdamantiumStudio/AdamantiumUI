using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A wrap panel measures its items within the room it has, as WPF's does, so an item that wraps its own - a
/// group of buttons on a toolbar that wraps - wraps them within that room too.</summary>
[TestFixture]
public class WrapPanelNestedTests
{
    [Test]
    public void AWrapPanelInAWrapPanel_WrapsWithinTheOuterOnesRoom()
    {
        var inner = new WrapPanel { Orientation = Orientation.Horizontal };
        for (var index = 0; index < 6; index++)
        {
            inner.Children.Add(new Border { Width = 50, Height = 20 });
        }

        var outer = new WrapPanel { Orientation = Orientation.Horizontal, Width = 160 };
        outer.Children.Add(new Border { Width = 100, Height = 20 });
        outer.Children.Add(inner);
        var window = new Window { Width = 400, Height = 300, Content = outer };
        for (var pass = 0; pass < 3; pass++)
        {
            WindowExtension.UpdateTree(window);
        }

        Assert.Multiple(() =>
        {
            Assert.That(inner.RenderSize.Width, Is.LessThanOrEqualTo(160), "the inner panel ran past the outer one's room");
            Assert.That(inner.RenderSize.Height, Is.EqualTo(40).Within(1e-6), "three items a line, two lines");
            Assert.That(inner.Bounds.Y, Is.EqualTo(20).Within(1e-6), "the inner panel went on to the outer one's next line");
        });
    }
}
