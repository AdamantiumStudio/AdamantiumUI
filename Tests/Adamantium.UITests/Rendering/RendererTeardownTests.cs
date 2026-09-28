using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>
/// A renderer that goes away takes its effects with it. None of them was ever freed: the collectors' effects, the three
/// every render unit shares and the material capture's blur stayed on the device after every closed window, popup stage
/// and device swap, each with its shader objects.
/// </summary>
[TestFixture]
[Category("Gpu")]
public class RendererTeardownTests
{
    private const int Dim = 64;

    [Test]
    public void AClosedRenderer_LeavesNoEffectBehind()
    {
        var device = GpuTestDevice.Device;
        var pool = device.DefaultEffectPool;
        var before = pool.RegisteredEffects.Count;
        var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)), Dim, Dim);

        var control = new TestControl
        {
            RenderAction = s => s.DrawRectangle(new SolidColorBrush(Colors.White), new Rect(0, 0, 32, 32)),
            Aura = new Aura { Radius = 8, Color = Colors.Red, Opacity = 1.0 }
        };
        control.Bounds = new Rect(0, 0, 32, 32);
        control.RenderSize = new Size(32, 32);
        control.RenderTransform = new Transform { TranslateX = 16, TranslateY = 16 };
        Assert.That(renderer.RenderFrame(new VisualRoot(control, Dim, Dim)), Is.True);
        Assert.That(pool.RegisteredEffects.Count, Is.GreaterThan(before), "the frame built no effect to leave behind");

        renderer.Dispose();

        Assert.That(pool.RegisteredEffects.Count, Is.EqualTo(before));
    }
}
