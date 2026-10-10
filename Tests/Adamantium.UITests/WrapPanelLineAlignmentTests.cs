using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A wrap panel's line is as tall as its tallest item, and each item stands in it by its own vertical
/// alignment, as WPF's does.</summary>
[TestFixture]
public class WrapPanelLineAlignmentTests
{
    private static WrapPanel Line(Border item)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Border { Width = 40, Height = 40 });
        panel.Children.Add(item);
        var window = new Window { Width = 400, Height = 300, Content = panel };
        for (var i = 0; i < 3; i++)
        {
            WindowExtension.UpdateTree(window);
        }

        return panel;
    }

    [TestCase(VerticalAlignment.Top, 0)]
    [TestCase(VerticalAlignment.Center, 15)]
    [TestCase(VerticalAlignment.Bottom, 30)]
    public void AShortItem_StandsInTheLineByItsAlignment(VerticalAlignment alignment, double top)
    {
        var item = new Border { Width = 20, Height = 10, VerticalAlignment = alignment };
        Line(item);

        Assert.That(item.Bounds.Y, Is.EqualTo(top).Within(1e-6));
    }
}
