using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// Shapes sharing an edge leave no seam, whether the edge lands on whole or half units.
[TestFixture]
[Category("Gpu")]
public class AbuttingEdgeSeamRenderTests
{
    private const int Dim = 128;

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

    /// <summary>A white block above a blue one, sharing the edge at <paramref name="edge"/>. Both are laid out by the
    /// root, so the arrange - and any snapping under it - is the real one.</summary>
    private static byte[] Draw(double edge)
    {
        var upper = new Border
        {
            Width = 40, Height = edge - 20,
            Background = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(40, 20, 0, 0)
        };
        var lower = new Border
        {
            Width = 40, Height = 40,
            Background = new SolidColorBrush(Colors.Blue),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(40, edge, 0, 0)
        };

        var host = new Grid();
        ((IContainer)host).AddOrSetChildComponent(upper);
        ((IContainer)host).AddOrSetChildComponent(lower);

        var root = new VisualRoot(host, Dim, Dim);
        ((IMeasurableComponent)root).Measure(new Size(Dim, Dim));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Dim, Dim));
        Assert.That(_renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();

        using var img = _renderer.RenderTarget.ResolveTexture.ReadbackToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    /// <summary>Every row across the join, so a seam names ITSELF instead of being asked about one row at a time.</summary>
    private static string Column(byte[] p, int from, int to)
    {
        var text = new System.Text.StringBuilder();
        for (var y = from; y <= to; y++)
        {
            var i = (y * Dim + 60) * 4;
            text.Append($"y={y}: ({p[i + 2]},{p[i + 1]},{p[i + 0]})  ");
        }
        return text.ToString();
    }

    [TestCase(60.0, TestName = "OnAWholeUnit")]
    [TestCase(60.5, TestName = "OnAHalfUnit")]
    public void TwoShapesSharingAnEdge_LeaveNoGapBetweenThem(double edge)
    {
        var pixels = Draw(edge);
        TestContext.WriteLine(Column(pixels, (int)edge - 3, (int)edge + 3));

        // Anywhere across the join there must be white or blue - never the black ground showing through.
        for (var y = (int)edge - 2; y <= (int)edge + 2; y++)
        {
            var i = (y * Dim + 60) * 4;
            var (r, g, b) = (pixels[i + 2], pixels[i + 1], pixels[i + 0]);
            Assert.That(r + g + b, Is.GreaterThan(90),
                $"the ground shows through the join at y={y}: ({r},{g},{b})");
        }
    }
}
