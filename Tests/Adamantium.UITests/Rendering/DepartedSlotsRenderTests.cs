using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>A rounded clip owner and a fade root each hold a slot of the transform table. Nothing released those slots
/// when their owner left the tree, and the render cache kept every such owner - and through it its view - alive.</summary>
[TestFixture]
[Category("Gpu")]
public class DepartedSlotsRenderTests
{
    private const int Dim = 64;
    private const int Count = 20;

    [Test]
    public void ClipOwnersAndFadeRootsThatLeave_GiveTheirSlotsBack()
    {
        var device = GpuTestDevice.Device;
        using var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)), Dim, Dim);
        Frame(renderer, new VisualRoot(new StackPanel(), Dim, Dim));
        var before = renderer.Cache.LiveTransformSlots;

        var panel = new StackPanel();
        for (var i = 0; i < Count; i++)
        {
            panel.Children.Add(new Border
            {
                Width = 8,
                Height = 2,
                ClipToBounds = true,
                ClipCornerRadius = new CornerRadius(1),
                Opacity = 0.5,
                Child = new Border { Background = Brushes.Red }
            });
        }

        var root = new VisualRoot(panel, Dim, Dim);
        Frame(renderer, root);
        Assume.That(renderer.Cache.LiveTransformSlots, Is.GreaterThanOrEqualTo(before + Count * 2),
            "precondition: every owner took a clip slot and an opacity slot");

        panel.Children.Clear();
        Frame(renderer, root);
        Frame(renderer, root);

        Assert.That(renderer.Cache.LiveTransformSlots, Is.EqualTo(before), "the owners are gone, and so must be their slots");
    }

    private static void Frame(OffscreenTestRenderer renderer, VisualRoot root)
    {
        ((IMeasurableComponent)root).Measure(new Size(Dim, Dim));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Dim, Dim));
        RenderDirty.MarkStructural();
        Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();
    }
}
