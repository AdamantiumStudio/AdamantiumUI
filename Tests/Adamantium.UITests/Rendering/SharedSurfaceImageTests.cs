using Adamantium.Graphics;
using Adamantium.Imaging;
using Adamantium.UI.Core.Media.Imaging;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>
/// A panel being resized flickered: its producer hands over a new surface for every size, faster than frames recorded
/// with the old one are drawn, and such a frame found the image let go of and drew nothing. A frame recorded with a
/// replaced image now draws the newest image that replaced it, and an import lives as long as a component holds it.
/// </summary>
[TestFixture]
[Category("Gpu")]
public class SharedSurfaceImageTests
{
    private SharedSurface _first;
    private SharedSurface _second;
    private SharedSurface _third;
    private DeviceResourceFactory _factory;

    [SetUp]
    public void Produce()
    {
        var device = GpuTestDevice.Device;
        _factory = new DeviceResourceFactory(device);
        _first = SharedSurface.CreateExportable(device, 8, 8, SurfaceFormat.B8G8R8A8.UNorm);
        _second = SharedSurface.CreateExportable(device, 16, 16, SurfaceFormat.B8G8R8A8.UNorm);
        _third = SharedSurface.CreateExportable(device, 24, 24, SurfaceFormat.B8G8R8A8.UNorm);
    }

    [TearDown]
    public void Free()
    {
        GpuTestDevice.Reclaim();
        _first.Dispose();
        _second.Dispose();
        _third.Dispose();
    }

    [Test]
    public void AFrameRecordedWithAReplacedImage_DrawsTheNewestImageThatReplacedIt()
    {
        var recorded = new SharedSurfaceImage(_first.Descriptor);
        var next = new SharedSurfaceImage(_second.Descriptor);
        var newest = new SharedSurfaceImage(_third.Descriptor);

        recorded.ReplaceWith(next);
        next.ReplaceWith(newest);
        var texture = recorded.HoldImport(_factory, out var holder);
        try
        {
            Assert.That(holder, Is.SameAs(newest));
            Assert.That(texture.Width, Is.EqualTo(24u));
            Assert.That(recorded.Texture, Is.Null, "a replaced surface was imported, though nothing draws it");
            Assert.That(next.Texture, Is.Null, "a replaced surface was imported, though nothing draws it");
        }
        finally
        {
            holder?.ReleaseImport();
            newest.Dispose();
        }
    }

    [Test]
    public void AHeldImport_OutlivesItsImage()
    {
        var image = new SharedSurfaceImage(_first.Descriptor);
        var texture = image.HoldImport(_factory, out var holder);
        image.Dispose();
        var again = image.HoldImport(_factory, out var second);
        try
        {
            Assert.That(texture.IsDisposed, Is.False, "the import was freed while a component still sampled it");
            Assert.That(again, Is.SameAs(texture), "a frame recorded before the image was let go of drew nothing");
            Assert.That(image.GetOrCreateTexture(_factory), Is.Null, "a let go image is not handed out unheld");
        }
        finally
        {
            holder?.ReleaseImport();
            second?.ReleaseImport();
        }

        Assert.That(image.Texture, Is.Null, "the last hold of a let go image frees its import");
    }

    [Test]
    public void AnImageLetGoOfBeforeAnyImport_GivesNothing()
    {
        var image = new SharedSurfaceImage(_first.Descriptor);

        image.Dispose();

        Assert.That(image.HoldImport(_factory, out var holder), Is.Null);
        Assert.That(holder, Is.Null);
    }
}
