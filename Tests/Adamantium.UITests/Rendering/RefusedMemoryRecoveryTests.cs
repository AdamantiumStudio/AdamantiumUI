using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>
/// The GPU refuses memory halfway through a frame that has to grow its buffers - another application, a game, holds
/// it: the frame fails, and once the GPU gives memory again the next frames draw the scene whole, as a renderer that was
/// never refused draws it. Before, the half-grown rings left holes every later frame tripped on for good. Refused on
/// purpose through the engine's test point, the GPU itself is not filled.
/// </summary>
[TestFixture]
[Category("Gpu")]
public class RefusedMemoryRecoveryTests
{
    private const int Dim = 96;

    private static TestControl Placed(Rect bounds) =>
        new() { Bounds = bounds, RenderSize = new Size<double>(bounds.Width, bounds.Height) };

    // A stage that, once filled, has far more elements than the first frame sized its buffers for.
    private static (OffscreenTestRenderer Renderer, VisualRoot Root, TestControl Stage) Scene()
    {
        var device = GpuTestDevice.Device;
        var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new StubResourceFactory()), Dim, Dim);
        var stage = Placed(new Rect(0, 0, Dim, Dim));
        var root = new VisualRoot(stage, Dim, Dim);
        Assert.That(renderer.RenderFrame(root), Is.True);
        RenderDirty.Clear();
        return (renderer, root, stage);
    }

    private static void Fill(TestControl stage)
    {
        for (var i = 0; i < 400; i++)
        {
            var cell = Placed(new Rect(i % 20 * 4, i / 20 * 4, 3, 3));
            var brush = i % 2 == 0 ? Brushes.Blue : Brushes.Red;
            cell.RenderAction = s =>
            {
                s.DrawRectangle(brush, new Rect(0, 0, 3, 3));
                s.DrawEllipse(new Rect(1, 1, 2, 2), Brushes.Green, 0, 360,
                    Adamantium.ProceduralGeometry.Shapes.EllipseType.Sector);
            };
            stage.Add(cell);
        }
    }

    private sealed class StubResourceFactory : IResourceFactory
    {
        public uint MaxTextureDimension => uint.MaxValue;
        public uint MaxTextureArrayLayers => uint.MaxValue;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData) => throw new NotSupportedException();
        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) => throw new NotSupportedException();
        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => throw new NotSupportedException();
        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format, ImageLayout desiredLayout) => throw new NotSupportedException();
        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => throw new NotSupportedException();
    }

    private static byte[] Pixels(OffscreenTestRenderer renderer)
    {
        using var image = renderer.RenderTarget.ResolveTexture.ToImage();
        var bytes = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    // Refused at each of the allocations the growing frame asks for - three - the later ones halfway through what it
    // makes: a frame that asks for fewer fails "refused" here, and the cases are to be brought in line.
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void AFrameTheGpuRefusesMemory_Fails_AndTheNextFramesDrawTheSceneWhole(int givenFirst)
    {
        var expected = Scene();
        Fill(expected.Stage);
        Assert.That(expected.Renderer.RenderFrame(expected.Root), Is.True);
        RenderDirty.Clear();
        var reference = Pixels(expected.Renderer);
        expected.Renderer.Dispose();

        var (renderer, root, stage) = Scene();
        using var _ = renderer;
        Fill(stage);
        var main = GpuTestDevice.Device.MainDevice;
        Exception refused = null;
        main.RefuseMemory(int.MaxValue, givenFirst);
        try
        {
            renderer.RenderFrame(root);
        }
        catch (Exception exception)
        {
            refused = exception;
        }
        finally
        {
            main.RefuseMemory(0);
        }

        // Every copy of every ring taken in turn: a hole in any of them shows on the frame that reads it.
        for (var frame = 0; frame < 4; frame++)
        {
            Assert.That(renderer.RenderFrame(root), Is.True, $"frame {frame} after the GPU gave memory again");
        }

        RenderDirty.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.Not.Null, "the frame the GPU refused memory failed");
            Assert.That(Pixels(renderer), Is.EqualTo(reference), "drawn whole, as never refused");
        });
    }
}
