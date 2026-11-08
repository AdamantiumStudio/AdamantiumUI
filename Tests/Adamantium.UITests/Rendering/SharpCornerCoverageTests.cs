using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A sharp rect on whole pixels covers exactly its pixels, at every corner too. The coverage ramp once came from fwidth of
// the distance, which doubles across the crease of a sharp corner, so one corner of four came out cut.
[TestFixture]
[Category("Gpu")]
public class SharpCornerCoverageTests
{
    private const int Dim = 64;
    private const int At = 10;
    private const int Side = 16;

    [TestCase(0.0, TestName = "ASharpRect_OnWholePixels_CoversExactlyItsPixels")]
    [TestCase(2.0, TestName = "ASharpRect_WithABorder_OnWholePixels_CoversExactlyItsPixels")]
    public void ASharpRect_OnWholePixels_CoversExactlyItsPixels(double border)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new StubResourceFactory());
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.Transparent };

        var box = new TestControl
        {
            RenderAction = s =>
            {
                if (border > 0)
                {
                    s.DrawBorder(Brushes.White, new Rect(At, At, Side, Side), CornerRadius.Empty, Brushes.Red,
                        new Thickness(border));
                }
                else
                {
                    s.DrawRectangle(Brushes.White, new Rect(At, At, Side, Side));
                }
            }
        };
        box.Bounds = new Rect(0, 0, Dim, Dim);
        box.RenderSize = new Size<double>(Dim, Dim);

        Assert.That(renderer.RenderFrame(new VisualRoot(box, Dim, Dim)), Is.True);

        using var img = renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)img.TotalSizeInBytes];
        Marshal.Copy(img.DataPointer, pixels, 0, pixels.Length);

        List<string> wrong = [];
        for (var y = At - 2; y < At + Side + 2; y++)
        {
            for (var x = At - 2; x < At + Side + 2; x++)
            {
                var inside = x >= At && x < At + Side && y >= At && y < At + Side;
                var alpha = pixels[(y * Dim + x) * 4 + 3];
                if (alpha != (inside ? 255 : 0))
                {
                    wrong.Add($"({x},{y})={alpha}");
                }
            }
        }

        Assert.That(wrong, Is.Empty, "every pixel inside is whole, every pixel outside is empty");
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
}
