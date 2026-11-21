using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// What a ring progress bar shows: a quarter full, its arc is the quarter after its start position, its way round. The
// arc's angles are its own now, so this is the picture - not a property - saying which side it grows on.
[TestFixture]
[Category("Gpu")]
public class RingProgressBarRenderTests
{
    private const int Dim = 64;
    private const int Half = Dim / 2;

    private static OffscreenTestRenderer _renderer;

    [OneTimeSetUp]
    public void CreateRenderer()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)), Dim, Dim)
        {
            ClearColor = Colors.Black
        };
    }

    [OneTimeTearDown]
    public void DisposeRenderer()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    private static ControlTemplate RingTemplate() => new(() =>
    {
        var arc = new Ellipse { Fill = Brushes.White, RingThickness = 10, EllipseType = EllipseType.Sector };
        var result = new TemplateResult { RootComponent = arc };
        result.RegisterName("PART_Indicator", arc);
        return result;
    });

    private static byte[] Draw(RingStartPosition start, SweepDirection direction)
    {
        var ring = new RingProgressBar
        {
            Width = Dim, Height = Dim, Value = 25, StartPosition = start, Direction = direction, Template = RingTemplate()
        };

        var root = new VisualRoot(ring, Dim, Dim);
        ((IMeasurableComponent)root).Measure(new Size<double>(Dim, Dim));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Dim, Dim));
        Assert.That(_renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        using var img = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    // In the middle of the band, on a diagonal.
    private static bool IsLit(byte[] pixels, int signX, int signY)
    {
        var along = (int)Math.Round((Half - 5) * 0.7071);
        var index = ((Half + signY * along) * Dim + Half + signX * along) * 4;
        return pixels[index] > 128;
    }

    [TestCase(RingStartPosition.Top, SweepDirection.Clockwise, 1, -1)]
    [TestCase(RingStartPosition.Top, SweepDirection.CounterClockwise, -1, -1)]
    [TestCase(RingStartPosition.Bottom, SweepDirection.Clockwise, -1, 1)]
    [TestCase(RingStartPosition.Right, SweepDirection.CounterClockwise, 1, -1)]
    public void AQuarterFull_ShowsTheQuarterAfterItsStart_ItsWayRound(RingStartPosition start, SweepDirection direction,
        int litX, int litY)
    {
        var pixels = Draw(start, direction);

        Assert.Multiple(() =>
        {
            foreach (var signX in new[] { -1, 1 })
            {
                foreach (var signY in new[] { -1, 1 })
                {
                    Assert.That(IsLit(pixels, signX, signY), Is.EqualTo(signX == litX && signY == litY),
                        $"the quarter {(signY < 0 ? "up" : "down")} {(signX < 0 ? "left" : "right")}");
                }
            }
        });
    }
}
