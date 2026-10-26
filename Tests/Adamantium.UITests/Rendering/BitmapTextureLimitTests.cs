using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Fonts;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Imaging;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>A picture larger than the device's textures goes up shrunk to fit them, its own pixels and size kept; a
/// disposed picture makes no texture.</summary>
[TestFixture]
[Category("Gpu")]
public class BitmapTextureLimitTests
{
    // The device's own factory, its textures held to a side of 64 and every texture it makes counted.
    private sealed class SmallTextures(IResourceFactory inner) : IResourceFactory
    {
        public int Made { get; private set; }

        public byte[] LastPixels { get; private set; }

        public uint MaxTextureDimension => 64;

        public uint MaxTextureArrayLayers => inner.MaxTextureArrayLayers;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData)
        {
            Made++;
            LastPixels = pixelData;
            return inner.CreateTexture(description, pixelData);
        }

        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) =>
            inner.CreateTextureArray(description, layers);

        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => inner.ImportSharedSurface(descriptor);

        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format,
            ImageLayout desiredLayout) => inner.CreateRenderTarget(width, height, msaa, format, desiredLayout);

        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => inner.GetFontRenderer(graphicsDevice);
    }

    private static BitmapSource Picture(uint width, uint height) =>
        new(width, height, 1, 1, SurfaceFormat.R8G8B8A8.UNorm, new byte[width * height * 4]);

    [Test]
    public void APictureLargerThanTheDevicesTextures_GoesUpShrunkToFit()
    {
        var factory = new SmallTextures(new DeviceResourceFactory(GpuTestDevice.Device));
        using var picture = Picture(200, 100);

        var texture = picture.GetOrCreateTexture(factory);

        Assert.That((texture.Width, texture.Height), Is.EqualTo((64u, 32u)));
        Assert.That((picture.PixelWidth, picture.PixelHeight, picture.PixelBytes.Length), Is.EqualTo((200u, 100u, 80000)));
    }

    // Its texture gone with the device, the picture goes up again from the pixels it was shrunk to, not shrunk again.
    [Test]
    public void APictureShrunkOnce_GoesUpAgainWithoutShrinking()
    {
        var factory = new SmallTextures(new DeviceResourceFactory(GpuTestDevice.Device));
        using var picture = Picture(200, 100);
        var first = picture.GetOrCreateTexture(factory);
        var shrunk = factory.LastPixels;

        first.Dispose();
        var second = picture.GetOrCreateTexture(factory);

        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(factory.LastPixels, Is.SameAs(shrunk));
    }

    // Saved, a picture its texture holds shrunk is written at its own size.
    [Test]
    public void APictureShrunkForTheDevice_IsSavedAtItsOwnSize()
    {
        var factory = new SmallTextures(new DeviceResourceFactory(GpuTestDevice.Device));
        using var picture = Picture(200, 100);
        picture.GetOrCreateTexture(factory);
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shrunk-picture.png");

        picture.Save(new Uri(path), ImageFileType.Png);
        var saved = BitmapLoader.Load(path);
        File.Delete(path);

        Assert.That((saved.Width, saved.Height), Is.EqualTo((200u, 100u)));
    }

    [Test]
    public void APictureThatFits_GoesUpAsItIs()
    {
        var factory = new SmallTextures(new DeviceResourceFactory(GpuTestDevice.Device));
        using var picture = Picture(64, 10);

        var texture = picture.GetOrCreateTexture(factory);

        Assert.That((texture.Width, texture.Height), Is.EqualTo((64u, 10u)));
    }

    private sealed class Refusing(IResourceFactory inner) : IResourceFactory
    {
        public int Asked { get; private set; }

        public uint MaxTextureDimension => inner.MaxTextureDimension;

        public uint MaxTextureArrayLayers => inner.MaxTextureArrayLayers;

        public ITexture CreateTexture(TextureDescription description, byte[] pixelData)
        {
            Asked++;
            throw new NotSupportedException("This device makes no such texture.");
        }

        public ITexture CreateTextureArray(TextureDescription description, IReadOnlyList<byte[]> layers) =>
            inner.CreateTextureArray(description, layers);

        public ITexture ImportSharedSurface(SharedSurfaceDescriptor descriptor) => inner.ImportSharedSurface(descriptor);

        public IRenderTarget CreateRenderTarget(uint width, uint height, MSAALevel msaa, SurfaceFormat format,
            ImageLayout desiredLayout) => inner.CreateRenderTarget(width, height, msaa, format, desiredLayout);

        public FontRenderer GetFontRenderer(IGraphicsDevice graphicsDevice) => inner.GetFontRenderer(graphicsDevice);
    }

    // A picture the device refuses is not drawn, and not asked for again every frame.
    [Test]
    public void APictureTheDeviceRefuses_IsNotAskedForAgain()
    {
        var factory = new Refusing(new DeviceResourceFactory(GpuTestDevice.Device));
        using var picture = Picture(8, 8);

        Assert.That(picture.GetOrCreateTexture(factory), Is.Null);
        Assert.That(picture.GetOrCreateTexture(factory), Is.Null);
        Assert.That(factory.Asked, Is.EqualTo(1));
    }

    [Test]
    public void ADisposedPicture_MakesNoTexture()
    {
        var factory = new SmallTextures(new DeviceResourceFactory(GpuTestDevice.Device));
        var picture = Picture(8, 8);
        picture.Dispose();

        Assert.That(picture.IsDisposed, Is.True);
        Assert.That(picture.GetOrCreateTexture(factory), Is.Null);
        Assert.That(factory.Made, Is.Zero);
    }
}
